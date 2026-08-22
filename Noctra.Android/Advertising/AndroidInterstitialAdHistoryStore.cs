using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Android.Content;
using Noctra.Core.Advertising;

namespace Noctra.Android.Advertising;

internal sealed class AndroidInterstitialAdHistoryStore : IInterstitialAdHistoryStore
{
    private const string PreferencesName = "noctra_ad_policy";
    private const string ImpressionsKey = "playback_exit_impressions";
    private readonly ISharedPreferences _preferences;

    public AndroidInterstitialAdHistoryStore(Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _preferences = context.GetSharedPreferences(
                PreferencesName,
                FileCreationMode.Private)
            ?? throw new InvalidOperationException("Advertising policy preferences are unavailable.");
    }

    public IReadOnlyList<DateTimeOffset> Read()
    {
        var serialized = _preferences.GetString(ImpressionsKey, string.Empty);
        if (string.IsNullOrWhiteSpace(serialized))
        {
            return Array.Empty<DateTimeOffset>();
        }

        var impressions = new List<DateTimeOffset>();
        foreach (var value in serialized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixSeconds))
            {
                continue;
            }

            try
            {
                impressions.Add(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
            }
            catch (ArgumentOutOfRangeException)
            {
                // Ignore only the malformed entry; valid history remains usable.
            }
        }

        return impressions;
    }

    public void Write(IReadOnlyList<DateTimeOffset> impressions)
    {
        ArgumentNullException.ThrowIfNull(impressions);
        var serialized = string.Join(
            ",",
            impressions
                .OrderBy(timestamp => timestamp)
                .Select(timestamp => timestamp.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));

        var editor = _preferences.Edit()
            ?? throw new InvalidOperationException("Advertising policy preferences cannot be edited.");
        editor.PutString(ImpressionsKey, serialized);
        if (!editor.Commit())
        {
            throw new InvalidOperationException(
                "Advertising impression history could not be committed.");
        }
    }
}
