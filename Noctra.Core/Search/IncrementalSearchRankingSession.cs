using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Noctra.Models;
using Noctra.Services;

namespace Noctra.Search;

internal sealed class SearchDocumentCache
{
    private readonly object _gate = new();
    private readonly Dictionary<(char Kind, int PlaylistId, int Id), Entry> _entries = new();
    private int _episodeNormalizationCount;

    public int DocumentBuildCount { get; private set; }
    public int DocumentReuseCount { get; private set; }
    public int EpisodeNormalizationCount => Volatile.Read(ref _episodeNormalizationCount);

    public ChannelSearchDocument GetOrCreate(Channel item, CancellationToken token = default)
    {
        var key = ('c', item.PlaylistId, item.Id);
        var fingerprint = string.Join('\u001f', item.Name, item.TvgName, item.GroupTitle,
            item.Language, item.Country, item.ReleaseYear?.ToString(CultureInfo.InvariantCulture));
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_entries.TryGetValue(key, out var cached) &&
                cached.Fingerprint == fingerprint && cached.Document is ChannelSearchDocument document)
            {
                DocumentReuseCount++;
                return document;
            }

            document = new ChannelSearchDocument(
                Normalize(item.Name),
                Normalize(item.TvgName),
                Normalize(item.GroupTitle),
                Normalize(item.Language),
                Normalize(item.Country),
                item.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
            _entries[key] = new Entry(fingerprint, document);
            DocumentBuildCount++;
            return document;
        }
    }

    public SeriesSearchDocument GetOrCreate(Series item, CancellationToken token)
    {
        var key = ('s', item.PlaylistId, item.Id);
        var episodeNames = new List<string?>();
        foreach (var season in item.Seasons)
        {
            token.ThrowIfCancellationRequested();
            foreach (var episode in season.Episodes)
            {
                token.ThrowIfCancellationRequested();
                episodeNames.Add(episode.Name);
            }
        }

        var fingerprint = string.Join('\u001f', item.Name, item.TmdbTitle, item.GroupTitle, item.Genre,
            item.NetworkName, item.ReleaseYear?.ToString(CultureInfo.InvariantCulture),
            string.Join('\u001e', episodeNames));
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_entries.TryGetValue(key, out var cached) &&
                cached.Fingerprint == fingerprint && cached.Document is SeriesSearchDocument existingDocument)
            {
                DocumentReuseCount++;
                return existingDocument;
            }
        }

        var normalizedEpisodeNames = new List<string>(episodeNames.Count);
        foreach (var episodeName in episodeNames)
        {
            token.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _episodeNormalizationCount);
            var normalizedEpisodeName = Normalize(episodeName);
            if (normalizedEpisodeName.Length > 0)
            {
                normalizedEpisodeNames.Add(normalizedEpisodeName);
            }
        }

        var document = new SeriesSearchDocument(
            Normalize(item.Name),
            Normalize(item.TmdbTitle),
            Normalize(item.GroupTitle),
            Normalize(item.Genre),
            Normalize(item.NetworkName),
            item.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            normalizedEpisodeNames.ToArray());
        lock (_gate)
        {
            token.ThrowIfCancellationRequested();
            if (_entries.TryGetValue(key, out var cached) &&
                cached.Fingerprint == fingerprint && cached.Document is SeriesSearchDocument concurrentlyBuilt)
            {
                DocumentReuseCount++;
                return concurrentlyBuilt;
            }

            _entries[key] = new Entry(fingerprint, document);
            DocumentBuildCount++;
            return document;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            builder.Append(character switch
            {
                '\u0131' => 'i', '\u015f' => 's', '\u011f' => 'g',
                '\u00fc' => 'u', '\u00f6' => 'o', '\u00e7' => 'c', '\u0130' => 'i',
                _ => char.IsLetterOrDigit(character) || char.IsWhiteSpace(character) ? character : ' '
            });
        }
        return Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
    }

    private sealed record Entry(string Fingerprint, object Document);
}

internal sealed class IncrementalSearchRankingSession
{
    private const int PrimaryThreshold = 55;
    private const int SimilarThreshold = 35;
    private readonly Query _query;
    private readonly SearchDocumentCache _cache;
    private HashSet<(int PlaylistId, int Id)> _channels = new();
    private HashSet<(int PlaylistId, int Id)> _series = new();
    private RankedBucket<Channel> _livePrimary = new(96);
    private RankedBucket<Channel> _vodPrimary = new(96);
    private RankedBucket<Series> _seriesPrimary = new(96);
    private RankedBucket<Channel> _liveSimilar = new(18);
    private RankedBucket<Channel> _vodSimilar = new(18);
    private RankedBucket<Series> _seriesSimilar = new(18);
    private Suggestion? _channelSuggestion;
    private Suggestion? _seriesSuggestion;

    public IncrementalSearchRankingSession(string query, SearchDocumentCache cache)
    {
        _query = Query.Create(query);
        _cache = cache;
    }

    public string NormalizedQuery => _query.Normalized;
    public int ChannelEvaluationCount { get; private set; }
    public int SeriesEvaluationCount { get; private set; }
    public int SeriesInputVisitCount { get; private set; }
    public int ReusedItemCount { get; private set; }
    public int WorkItemEvaluationCount { get; private set; }
    public int WorkItemReuseCount { get; private set; }

    public void AppendChannels(IEnumerable<Channel> items, CancellationToken token)
    {
        var prepared = new List<PreparedChannel>();
        var pendingIdentities = new HashSet<(int PlaylistId, int Id)>();
        var reused = 0;
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            if (item.Type is not (ChannelType.Live or ChannelType.VOD))
            {
                continue;
            }

            var identity = (item.PlaylistId, item.Id);
            if (_channels.Contains(identity) || !pendingIdentities.Add(identity))
            {
                reused++;
                WorkItemReuseCount++;
                continue;
            }

            var document = _cache.GetOrCreate(item, token);
            var score = ScoreChannel(document, token);
            WorkItemEvaluationCount++;
            prepared.Add(new PreparedChannel(item, score, HasImage(item)));
        }

        token.ThrowIfCancellationRequested();
        var replacementIdentities = new HashSet<(int PlaylistId, int Id)>(_channels);
        var replacementLivePrimary = _livePrimary.Clone(HasImage);
        var replacementVodPrimary = _vodPrimary.Clone(HasImage);
        var replacementLiveSimilar = _liveSimilar.Clone(HasImage);
        var replacementVodSimilar = _vodSimilar.Clone(HasImage);
        var replacementSuggestion = RefreshSuggestionImage(_channelSuggestion);
        var refreshedSeriesPrimary = _seriesPrimary.Clone(HasImage);
        var refreshedSeriesSimilar = _seriesSimilar.Clone(HasImage);
        var refreshedSeriesSuggestion = RefreshSuggestionImage(_seriesSuggestion);
        foreach (var candidate in prepared)
        {
            token.ThrowIfCancellationRequested();
            var item = candidate.Item;
            var score = candidate.Score;
            var primary = item.Type == ChannelType.Live ? replacementLivePrimary : replacementVodPrimary;
            var similar = item.Type == ChannelType.Live ? replacementLiveSimilar : replacementVodSimilar;
            AddByThreshold(item, score.Primary, primary, similar, item.Name, candidate.HasImage, token);
            ConsiderSuggestion(item, item.Name, score.Suggestion, candidate.HasImage, ref replacementSuggestion);
            replacementIdentities.Add((item.PlaylistId, item.Id));
        }

        token.ThrowIfCancellationRequested();
        _channels = replacementIdentities;
        _livePrimary = replacementLivePrimary;
        _vodPrimary = replacementVodPrimary;
        _liveSimilar = replacementLiveSimilar;
        _vodSimilar = replacementVodSimilar;
        _channelSuggestion = replacementSuggestion;
        _seriesPrimary = refreshedSeriesPrimary;
        _seriesSimilar = refreshedSeriesSimilar;
        _seriesSuggestion = refreshedSeriesSuggestion;
        ChannelEvaluationCount += prepared.Count;
        ReusedItemCount += reused;
    }

    public void AppendSeries(IEnumerable<Series> items, CancellationToken token)
    {
        var prepared = PrepareSeries(items, _series, token, out var reused, out var inputVisits);
        token.ThrowIfCancellationRequested();
        var replacementIdentities = new HashSet<(int PlaylistId, int Id)>(_series);
        var replacementPrimary = _seriesPrimary.Clone(HasImage);
        var replacementSimilar = _seriesSimilar.Clone(HasImage);
        var replacementSuggestion = RefreshSuggestionImage(_seriesSuggestion);
        CommitSeries(prepared, replacementIdentities, replacementPrimary, replacementSimilar,
            ref replacementSuggestion, token);
        token.ThrowIfCancellationRequested();
        _series = replacementIdentities;
        _seriesPrimary = replacementPrimary;
        _seriesSimilar = replacementSimilar;
        _seriesSuggestion = replacementSuggestion;
        SeriesEvaluationCount += prepared.Count;
        ReusedItemCount += reused;
        SeriesInputVisitCount += inputVisits;
    }

    public void ReplaceSeries(IEnumerable<Series> items, CancellationToken token)
    {
        var replacementIdentities = new HashSet<(int PlaylistId, int Id)>();
        var prepared = PrepareSeries(items, replacementIdentities, token, out var reused, out var inputVisits);
        token.ThrowIfCancellationRequested();

        var replacementPrimary = new RankedBucket<Series>(96);
        var replacementSimilar = new RankedBucket<Series>(18);
        Suggestion? replacementSuggestion = null;
        CommitSeries(prepared, replacementIdentities, replacementPrimary, replacementSimilar,
            ref replacementSuggestion, token);

        token.ThrowIfCancellationRequested();
        _series = replacementIdentities;
        _seriesPrimary = replacementPrimary;
        _seriesSimilar = replacementSimilar;
        _seriesSuggestion = replacementSuggestion;
        SeriesEvaluationCount += prepared.Count;
        ReusedItemCount += reused;
        SeriesInputVisitCount += inputVisits;
    }

    private List<PreparedSeries> PrepareSeries(
        IEnumerable<Series> items,
        HashSet<(int PlaylistId, int Id)> committedIdentities,
        CancellationToken token,
        out int reused,
        out int inputVisits)
    {
        var prepared = new List<PreparedSeries>();
        var pendingIdentities = new HashSet<(int PlaylistId, int Id)>();
        reused = 0;
        inputVisits = 0;
        foreach (var item in items)
        {
            token.ThrowIfCancellationRequested();
            inputVisits++;
            var identity = (item.PlaylistId, item.Id);
            if (committedIdentities.Contains(identity) || !pendingIdentities.Add(identity))
            {
                reused++;
                WorkItemReuseCount++;
                continue;
            }

            var document = _cache.GetOrCreate(item, token);
            var score = ScoreSeries(document, token);
            WorkItemEvaluationCount++;
            prepared.Add(new PreparedSeries(item, score, HasImage(item)));
        }

        return prepared;
    }

    private void CommitSeries(
        IReadOnlyList<PreparedSeries> prepared,
        HashSet<(int PlaylistId, int Id)> identities,
        RankedBucket<Series> primary,
        RankedBucket<Series> similar,
        ref Suggestion? suggestion,
        CancellationToken token)
    {
        foreach (var candidate in prepared)
        {
            token.ThrowIfCancellationRequested();
            var item = candidate.Item;
            var score = candidate.Score;
            if (score.Primary >= PrimaryThreshold)
                primary.Add(item, score.Primary, candidate.HasImage, item.Name, token);
            if (score.Similar >= SimilarThreshold && score.Similar < PrimaryThreshold)
                similar.Add(item, score.Similar, candidate.HasImage, item.Name, token);
            ConsiderSuggestion(item, item.Name, score.Suggestion, candidate.HasImage, ref suggestion);
            identities.Add((item.PlaylistId, item.Id));
        }
    }

    public SearchRankingSnapshot CreateSnapshot()
    {
        var primaryTitles = _livePrimary.Items.Select(item => SearchDocumentCache.Normalize(item.Name))
            .Concat(_vodPrimary.Items.Select(item => SearchDocumentCache.Normalize(item.Name)))
            .Concat(_seriesPrimary.Items.Select(item => SearchDocumentCache.Normalize(item.Name)))
            .ToHashSet(StringComparer.Ordinal);
        var bestSuggestion = BestSuggestion(_channelSuggestion, _seriesSuggestion);
        var suggestion = bestSuggestion is not null && bestSuggestion.NormalizedTitle != _query.Normalized &&
                         !primaryTitles.Contains(bestSuggestion.NormalizedTitle)
            ? bestSuggestion.Title
            : string.Empty;
        return new SearchRankingSnapshot(_livePrimary.Items, _seriesPrimary.Items, _vodPrimary.Items,
            suggestion, _liveSimilar.Items, _seriesSimilar.Items, _vodSimilar.Items);
    }

    private Score ScoreChannel(ChannelSearchDocument item, CancellationToken token)
    {
        var titleScore = ScoreField(item.Name, 100, token);
        var primary = Max(titleScore, ScoreField(item.TvgName, 86, token),
            ScoreField(item.GroupTitle, 58, token), ScoreField(item.Language, 42, token),
            ScoreField(item.Country, 42, token),
            item.ReleaseYear == _query.Normalized ? 56 : 0);
        return new Score(primary, primary, ScoreSuggestion(item.Name, titleScore));
    }

    private Score ScoreSeries(SeriesSearchDocument item, CancellationToken token)
    {
        var titleScore = ScoreField(item.Name, 100, token);
        var withoutEpisodes = Max(titleScore, ScoreField(item.TmdbTitle, 92, token),
            ScoreField(item.GroupTitle, 58, token), ScoreField(item.Genre, 54, token),
            ScoreField(item.NetworkName, 45, token),
            item.ReleaseYear == _query.Normalized ? 56 : 0);
        var primary = withoutEpisodes;
        if (_query.Normalized.Length >= 4 && _query.Tokens.Length > 0)
        {
            foreach (var episodeName in item.EpisodeNames)
            {
                token.ThrowIfCancellationRequested();
                primary = Math.Max(primary, ScoreField(episodeName, 42, token));
                if (primary >= 42) break;
            }
        }
        return new Score(primary, withoutEpisodes, ScoreSuggestion(item.Name, titleScore));
    }

    private int ScoreSuggestion(string title, int titleScore)
    {
        if (title.Length == 0 || title == _query.Normalized) return 0;
        var score = titleScore;
        if (title.StartsWith(_query.Normalized, StringComparison.Ordinal)) score += 8;
        score -= Math.Min(18, Math.Abs(title.Length - _query.Normalized.Length));
        return Math.Clamp(score, 0, 100);
    }

    private int ScoreField(string value, int weight, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (value.Length == 0 || _query.Normalized.Length == 0) return 0;
        var score = ScoreNormalized(value, token);
        if (!_query.HasEpisodeIntent && Regex.IsMatch(value,
                @"\b(s\d{1,2}\s*e\d{1,2}|season|sezon|episode|bolum|buelum)\b", RegexOptions.IgnoreCase))
            score = Math.Max(0, score - 12);
        return Math.Clamp((int)Math.Round(score * (weight / 100.0)), 0, 100);
    }

    private int ScoreNormalized(string value, CancellationToken token)
    {
        if (value == _query.Normalized || (_query.NormalizedSeries.Length > 0 && value == _query.NormalizedSeries)) return 100;
        if (value.StartsWith(_query.Normalized, StringComparison.Ordinal)) return 92;
        if (value.Contains($" {_query.Normalized} ", StringComparison.Ordinal) || value.EndsWith($" {_query.Normalized}", StringComparison.Ordinal)) return 84;
        if (value.Contains(_query.Normalized, StringComparison.Ordinal)) return 74;
        if (_query.NormalizedSeries.Length > 0 && value.Contains(_query.NormalizedSeries, StringComparison.Ordinal)) return 70;
        var tokenScore = ScoreTokenCoverage(value, token);
        if (tokenScore > 0) return tokenScore;
        if (_query.Normalized.Length < 3) return 0;
        var fuzzy = FuzzySimilarity(_query.Normalized, value, token);
        return fuzzy switch { >= 0.92 => 72, >= 0.84 => 62, >= 0.74 => 44, _ => 0 };
    }

    private int ScoreTokenCoverage(string value, CancellationToken token)
    {
        if (_query.Tokens.Length == 0) return 0;
        var words = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var exact = 0;
        var prefix = 0;
        foreach (var queryToken in _query.Tokens)
        {
            token.ThrowIfCancellationRequested();
            if (words.Any(word => word == queryToken)) { exact++; prefix++; }
            else if (words.Any(word => word.StartsWith(queryToken, StringComparison.Ordinal))) prefix++;
        }
        if (exact == _query.Tokens.Length) return 68;
        if (prefix == _query.Tokens.Length) return 62;
        return _query.Tokens.Length > 1 && prefix >= Math.Max(1, _query.Tokens.Length - 1) ? 48 : 0;
    }

    private static double FuzzySimilarity(string query, string candidate, CancellationToken token)
    {
        var queryWords = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var candidateWords = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (queryWords.Length == 0 || candidateWords.Length == 0) return 0;
        double total = 0;
        foreach (var queryWord in queryWords)
        {
            token.ThrowIfCancellationRequested();
            double best = 0;
            foreach (var candidateWord in candidateWords)
            {
                token.ThrowIfCancellationRequested();
                if (candidateWord == queryWord) { best = 1; break; }
                if (candidateWord.StartsWith(queryWord, StringComparison.OrdinalIgnoreCase)) best = Math.Max(best, 0.9);
                else if (candidateWord.Contains(queryWord, StringComparison.OrdinalIgnoreCase)) best = Math.Max(best, 0.6);
                var maxDistance = DistanceThreshold(Math.Max(queryWord.Length, candidateWord.Length));
                if (Math.Abs(queryWord.Length - candidateWord.Length) > maxDistance + 1) continue;
                var distance = Levenshtein(queryWord, candidateWord, maxDistance, token);
                if (distance >= 0) best = Math.Max(best, 1.0 - (double)distance / Math.Max(queryWord.Length, candidateWord.Length));
            }
            total += best;
        }
        var penalty = candidateWords.Length > queryWords.Length + 2 ? (candidateWords.Length - queryWords.Length - 2) * 0.05 : 0;
        return Math.Max(0, total / queryWords.Length - penalty);
    }

    private static int Levenshtein(string source, string target, int maximum, CancellationToken token)
    {
        if (source == target) return 0;
        if (Math.Abs(source.Length - target.Length) > maximum) return -1;
        var pool = ArrayPool<int>.Shared;
        var previousPrevious = pool.Rent(target.Length + 1);
        var previous = pool.Rent(target.Length + 1);
        var current = pool.Rent(target.Length + 1);
        try
        {
            for (var column = 0; column <= target.Length; column++) previous[column] = column;
            for (var row = 1; row <= source.Length; row++)
            {
                token.ThrowIfCancellationRequested();
                current[0] = row;
                var rowMinimum = row;
                for (var column = 1; column <= target.Length; column++)
                {
                    var cost = source[row - 1] == target[column - 1] ? 0 : 1;
                    current[column] = Math.Min(Math.Min(current[column - 1] + 1, previous[column] + 1), previous[column - 1] + cost);
                    if (row > 1 && column > 1 && source[row - 1] == target[column - 2] && source[row - 2] == target[column - 1])
                        current[column] = Math.Min(current[column], previousPrevious[column - 2] + 1);
                    rowMinimum = Math.Min(rowMinimum, current[column]);
                }
                if (rowMinimum > maximum) return -1;
                var recycled = previousPrevious;
                previousPrevious = previous;
                previous = current;
                current = recycled;
            }
            return previous[target.Length] <= maximum ? previous[target.Length] : -1;
        }
        finally
        {
            pool.Return(previousPrevious);
            pool.Return(previous);
            pool.Return(current);
        }
    }

    private void ConsiderSuggestion(object source, string? title, int score, bool hasImage, ref Suggestion? current)
    {
        if (_query.Normalized.Length < 3 || string.IsNullOrWhiteSpace(title) || score < 74) return;
        var candidate = new Suggestion(source, title, SearchDocumentCache.Normalize(title), score, hasImage);
        if (current is null || RankedBucket<Channel>.IsBetter(candidate.Score, candidate.HasImage,
                candidate.Title, current.Score, current.HasImage, current.Title)) current = candidate;
    }

    private static Suggestion? BestSuggestion(Suggestion? left, Suggestion? right)
        => left is null ? right : right is null || RankedBucket<Channel>.IsBetter(
            left.Score, left.HasImage, left.Title, right.Score, right.HasImage, right.Title) ? left : right;

    private static Suggestion? RefreshSuggestionImage(Suggestion? suggestion)
        => suggestion?.Source switch
        {
            Channel channel => suggestion with { HasImage = HasImage(channel) },
            Series series => suggestion with { HasImage = HasImage(series) },
            _ => suggestion
        };

    private static void AddByThreshold(Channel item, int score, RankedBucket<Channel> primary,
        RankedBucket<Channel> similar, string title, bool hasImage, CancellationToken token)
    {
        if (score >= PrimaryThreshold) primary.Add(item, score, hasImage, title, token);
        else if (score >= SimilarThreshold) similar.Add(item, score, hasImage, title, token);
    }

    private static bool HasImage(Channel item) => IsDisplayImageUrl(item.CoverUrl) || IsDisplayImageUrl(item.LogoUrl);
    private static bool HasImage(Series item) => IsDisplayImageUrl(item.CoverUrl);

    private static bool IsDisplayImageUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        var normalized = url.Trim().Trim('"', '\'');
        if (normalized.Length < 12 ||
            normalized.Equals("logo n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("n/a", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("none", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("null", StringComparison.OrdinalIgnoreCase)) return false;
        return normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("//", StringComparison.Ordinal) ||
               normalized.StartsWith("avares://", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) ||
               normalized.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase);
    }
    private static int Max(int first, int second, int third, int fourth, int fifth, int sixth)
        => Math.Max(Math.Max(Math.Max(first, second), Math.Max(third, fourth)), Math.Max(fifth, sixth));
    private static int DistanceThreshold(int length) => length switch { <= 4 => 1, <= 7 => 2, <= 11 => 3, <= 15 => 4, _ => 5 };
    private readonly record struct Score(int Primary, int Similar, int Suggestion);
    private readonly record struct PreparedChannel(Channel Item, Score Score, bool HasImage);
    private readonly record struct PreparedSeries(Series Item, Score Score, bool HasImage);
    private sealed record Suggestion(object Source, string Title, string NormalizedTitle, int Score, bool HasImage);
    private readonly record struct Query(string Normalized, string NormalizedSeries, string[] Tokens, bool HasEpisodeIntent)
    {
        public static Query Create(string raw)
        {
            var normalized = SearchDocumentCache.Normalize(raw);
            return new Query(normalized, SeriesInfoParser.NormalizeKey(raw), normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(token => token.Length > 1).Distinct(StringComparer.Ordinal).ToArray(),
                Regex.IsMatch(normalized, @"\b(s\d{1,2}|e\d{1,2}|season|sezon|episode|bolum|buelum)\b", RegexOptions.IgnoreCase));
        }
    }
}

internal sealed record SearchRankingSnapshot(IReadOnlyList<Channel> LivePrimary, IReadOnlyList<Series> SeriesPrimary,
    IReadOnlyList<Channel> VodPrimary, string Suggestion, IReadOnlyList<Channel> LiveSimilar,
    IReadOnlyList<Series> SeriesSimilar, IReadOnlyList<Channel> VodSimilar);
internal sealed record ChannelSearchDocument(
    string Name,
    string TvgName,
    string GroupTitle,
    string Language,
    string Country,
    string ReleaseYear);
internal sealed record SeriesSearchDocument(
    string Name,
    string TmdbTitle,
    string GroupTitle,
    string Genre,
    string NetworkName,
    string ReleaseYear,
    string[] EpisodeNames);

internal sealed class RankedBucket<T>
{
    private readonly int _capacity;
    private readonly List<Entry> _entries = new();
    public RankedBucket(int capacity) => _capacity = capacity;
    public IReadOnlyList<T> Items => _entries.Select(entry => entry.Item).ToArray();
    public RankedBucket<T> Clone(Func<T, bool> hasImage)
    {
        var clone = new RankedBucket<T>(_capacity);
        clone._entries.AddRange(_entries.Select(entry => entry with { HasImage = hasImage(entry.Item) }));
        clone._entries.Sort((left, right) =>
        {
            if (IsBetter(left.Score, left.HasImage, left.Title, right.Score, right.HasImage, right.Title)) return -1;
            if (IsBetter(right.Score, right.HasImage, right.Title, left.Score, left.HasImage, left.Title)) return 1;
            return 0;
        });
        return clone;
    }
    public void Add(T item, int score, bool hasImage, string title, CancellationToken token)
    {
        var entry = new Entry(item, score, hasImage, title);
        var index = -1;
        for (var candidateIndex = 0; candidateIndex < _entries.Count; candidateIndex++)
        {
            if ((candidateIndex & 15) == 0)
            {
                token.ThrowIfCancellationRequested();
            }

            var existing = _entries[candidateIndex];
            if (IsBetter(score, hasImage, title, existing.Score, existing.HasImage, existing.Title))
            {
                index = candidateIndex;
                break;
            }
        }
        if (index < 0)
        {
            if (_entries.Count >= _capacity) return;
            _entries.Add(entry);
        }
        else _entries.Insert(index, entry);
        if (_entries.Count > _capacity) _entries.RemoveAt(_entries.Count - 1);
    }
    internal static bool IsBetter(int leftScore, bool leftImage, string leftTitle, int rightScore, bool rightImage, string rightTitle)
    {
        if (leftScore != rightScore) return leftScore > rightScore;
        if (leftImage != rightImage) return leftImage;
        return string.Compare(leftTitle, rightTitle, StringComparison.CurrentCulture) < 0;
    }
    private sealed record Entry(T Item, int Score, bool HasImage, string Title);
}
