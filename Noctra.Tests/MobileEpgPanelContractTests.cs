using System.Text.Json;
using System.Text.RegularExpressions;

namespace Noctra.Tests;

public sealed class MobileEpgPanelContractTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void PlayerView_UsesNewGuideLifecycle()
    {
        var source = Read("Noctra.Mobile", "Views", "MobilePlayerView.axaml.cs");

        Assert.Contains("EpgPanel.OpenAsync()", source, StringComparison.Ordinal);
        Assert.Contains("EpgPanel.CloseGuide()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InitializeTimelineHeader", source, StringComparison.Ordinal);
        Assert.DoesNotContain("EpgFocusRowIndex", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileCompositionRoot_ProvidesLiveChannelsToTheGuide()
    {
        var resolver = Read("Noctra.Mobile", "Services", "MobileViewModelResolver.cs");

        Assert.Contains("player.LiveChannelsLoader ??=", resolver, StringComparison.Ordinal);
        Assert.Contains("GetChannelsFilteredAsync", resolver, StringComparison.Ordinal);
        Assert.Contains("ChannelType.Live", resolver, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreGuide_IsTimeBasedAndClosingDoesNotDiscardCachedRows()
    {
        var guide = Read("Noctra.Core", "ViewModels", "EpgGuideRow.cs");
        var player = Read("Noctra.Core", "ViewModels", "PlayerViewModel.cs");

        Assert.DoesNotContain("Pixel", guide, StringComparison.Ordinal);
        Assert.DoesNotContain("EpgPxPerMinute", player, StringComparison.Ordinal);
        Assert.DoesNotContain("EpgCanvasPixelWidth", player, StringComparison.Ordinal);

        var closeHandlerStart = player.IndexOf("partial void OnIsEpgPanelOpenChanged", StringComparison.Ordinal);
        var nextSection = player.IndexOf("// ──", closeHandlerStart + 1, StringComparison.Ordinal);
        var closeHandler = player[closeHandlerStart..nextSection];
        Assert.DoesNotContain("EpgRows.Clear", closeHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("EpgRows.ReplaceAll", closeHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreGuide_DoesNotDisposeCancellationSourceOwnedByAnInFlightLoad()
    {
        var player = Read("Noctra.Core", "ViewModels", "PlayerViewModel.cs");
        var loadStart = player.IndexOf("public async Task LoadEpgPanelAsync", StringComparison.Ordinal);
        var loadEnd = player.IndexOf("[RelayCommand]\n    private Task RetryEpgPanel", loadStart, StringComparison.Ordinal);
        var loadMethod = player[loadStart..loadEnd];

        Assert.Contains("var loadToken = loadCts.Token", loadMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("previousCts?.Dispose()", loadMethod, StringComparison.Ordinal);
        Assert.Contains("if (loadToken.IsCancellationRequested", loadMethod, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(Volatile.Read(ref _epgLoadCts), loadCts)", loadMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void MobileGuide_DoesNotDisposeCancellationSourceOwnedByAnInFlightOpen()
    {
        var panel = Read("Noctra.Mobile", "Views", "MobilePlayerEpgPanel.axaml.cs");
        var openStart = panel.IndexOf("public async Task OpenAsync", StringComparison.Ordinal);
        var openEnd = panel.IndexOf("private void OnDataContextChanged", openStart, StringComparison.Ordinal);
        var lifecycle = panel[openStart..openEnd];

        Assert.Contains("var openToken = openCts.Token", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("previousCts?.Dispose()", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("_openCts?.Dispose()", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_UsesFocusableVirtualizedThemeDrivenControls()
    {
        var axaml = Read("Noctra.Mobile", "Views", "MobilePlayerEpgPanel.axaml");
        var presentation = Read("Noctra.Mobile", "ViewModels", "MobileEpgGuidePresentation.cs");

        Assert.Contains("VirtualizingStackPanel", axaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"epgProgram\"", axaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name", axaml, StringComparison.Ordinal);
        Assert.Contains("DynamicResource", axaml, StringComparison.Ordinal);
        Assert.DoesNotMatch("#[0-9A-Fa-f]{6,8}", axaml);
        Assert.Contains("BatchObservableCollection<MobileEpgRow>", presentation, StringComparison.Ordinal);
        Assert.Contains("Rows.ReplaceAll", presentation, StringComparison.Ordinal);

        var codeBehind = Read("Noctra.Mobile", "Views", "MobilePlayerEpgPanel.axaml.cs");
        Assert.Contains("Guide.ConfigureScale", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BindPlayer(null)", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_TracksSelectedChannelIndependentlyFromSelectedProgram()
    {
        var presentation = Read("Noctra.Mobile", "ViewModels", "MobileEpgGuidePresentation.cs");

        Assert.Contains("private Channel? _selectedChannel", presentation, StringComparison.Ordinal);
        Assert.Contains("SelectedChannel = row.Channel", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "public Channel? SelectedChannel => SelectedProgram?.Channel",
            presentation,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_DebouncesResizeAndAvoidsDuplicateLoadRebuilds()
    {
        var codeBehind = Read("Noctra.Mobile", "Views", "MobilePlayerEpgPanel.axaml.cs");

        Assert.Contains("_resizeTimer", codeBehind, StringComparison.Ordinal);
        Assert.Contains("&& _openCts is null", codeBehind, StringComparison.Ordinal);
        Assert.Contains("EpgGuideState == EpgGuideLoadState.Ready", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_ProvidesInitialRemoteFocusWithoutProgramData()
    {
        var axaml = Read("Noctra.Mobile", "Views", "MobilePlayerEpgPanel.axaml");
        var codeBehind = Read("Noctra.Mobile", "Views", "MobilePlayerEpgPanel.axaml.cs");

        Assert.Contains("x:Name=\"CloseGuideButton\"", axaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RetryGuideButton\"", axaml, StringComparison.Ordinal);
        Assert.Contains("FocusChannel", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RetryGuideButton.Focus()", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Guide_DynamicResourcesExistInMobileTheme()
    {
        var panel = Read("Noctra.Mobile", "Views", "MobilePlayerEpgPanel.axaml");
        var mobileRoot = Path.Combine(Root, "Noctra.Mobile");
        var definedKeys = Directory
            .EnumerateFiles(mobileRoot, "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("MobilePlayerEpgPanel.axaml", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => Regex.Matches(File.ReadAllText(path), "x:Key=\"([^\"]+)\"")
                .Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        var referencedKeys = Regex.Matches(panel, "\\{DynamicResource\\s+([^}\\s]+)\\}")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal);

        foreach (var key in referencedKeys)
            Assert.Contains(key, definedKeys);
    }

    [Theory]
    [InlineData("de-DE.json")]
    [InlineData("en-US.json")]
    [InlineData("es-ES.json")]
    [InlineData("fr-FR.json")]
    [InlineData("tr-TR.json")]
    public void GuideFriendlyStates_AreLocalized(string fileName)
    {
        using var json = JsonDocument.Parse(Read(
            "Noctra.Core",
            "Localization",
            "Translations",
            fileName));

        var root = json.RootElement;
        foreach (var key in new[]
        {
            "Player.Epg.Live",
            "Player.Epg.WatchChannel",
            "Player.Epg.LoadingDescription",
            "Player.Epg.NoProgramsDescription",
            "Player.Epg.EmptyTitle",
            "Player.Epg.EmptyDescription",
            "Player.Epg.ErrorTitle",
            "Player.Epg.ErrorDescription",
            "Player.Epg.Retry"
        })
        {
            Assert.True(root.TryGetProperty(key, out var value), $"{fileName} is missing {key}");
            Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"{fileName}:{key} is empty");
        }
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { Root }.Concat(parts).ToArray()));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? Directory.GetCurrentDirectory();
    }
}
