using System.Collections;
using System.Reflection;
using Noctra.ViewModels;

namespace Noctra.Tests;

public sealed class MobileSettingsCustomEpgAndStatusTests
{
    [Theory]
    [InlineData("https://example.com/guide.xml", true)]
    [InlineData(" http://epg.example.org/feed.xml.gz ", true)]
    [InlineData("", false)]
    [InlineData("not-a-url", false)]
    [InlineData("ftp://example.com/guide.xml", false)]
    [InlineData("https:///guide.xml", false)]
    public void CustomEpgPolicy_AcceptsOnlyAbsoluteHttpUrls(string value, bool expected)
    {
        var method = GetPolicyMethod("IsValid");

        var actual = (bool)method.Invoke(null, [value])!;

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void CustomEpgPolicy_AllowsOnlyOneIncompleteDraft()
    {
        var method = GetPolicyMethod("CanAdd");

        Assert.True(InvokeCanAdd(method, [], isPremium: true));
        Assert.False(InvokeCanAdd(method, [string.Empty], isPremium: true));
        Assert.False(InvokeCanAdd(method, ["invalid"], isPremium: true));
        Assert.True(InvokeCanAdd(method, ["https://example.com/first.xml"], isPremium: true));
    }

    [Fact]
    public void CustomEpgPolicy_PreservesTheLastPersistedValueDuringAnInvalidEdit()
    {
        var item = new EpgUrlItem { Url = "invalid while editing" };
        var persistedUrl = typeof(EpgUrlItem).GetProperty(
            "PersistedUrl",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(persistedUrl);
        persistedUrl!.SetValue(item, "https://example.com/original.xml");

        var method = GetPolicyMethod("BuildPersistedSources");
        var result = Assert.IsAssignableFrom<IEnumerable>(method.Invoke(null, [new[] { item }]));

        Assert.Equal(
            ["https://example.com/original.xml"],
            result.Cast<string>().ToArray());
    }

    [Fact]
    public void CustomEpgPolicy_OmitsANewInvalidDraftButPersistsValidEdits()
    {
        var invalidDraft = new EpgUrlItem { Url = string.Empty };
        var valid = new EpgUrlItem { Url = " https://example.com/new.xml " };
        var method = GetPolicyMethod("BuildPersistedSources");

        var result = Assert.IsAssignableFrom<IEnumerable>(
            method.Invoke(null, [new[] { invalidDraft, valid }]));

        Assert.Equal(["https://example.com/new.xml"], result.Cast<string>().ToArray());
    }

    [Fact]
    public async Task SettingsChangeOriginGate_SuppressesOnlyTheOwnedSaveNotification()
    {
        var gateType = typeof(SettingsViewModel).Assembly.GetType(
            "Noctra.ViewModels.SettingsChangeOriginGate");
        Assert.NotNull(gateType);

        var gate = Activator.CreateInstance(gateType!, nonPublic: true);
        Assert.NotNull(gate);
        var shouldReload = gateType!.GetProperty(
            "ShouldReload",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var runOwnedSave = gateType.GetMethod(
            "RunOwnedSaveAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(shouldReload);
        Assert.NotNull(runOwnedSave);

        bool? shouldReloadDuringSave = null;
        bool? shouldReloadForExternalFlow = null;
        Func<Task> save = async () =>
        {
            shouldReloadDuringSave = (bool)shouldReload!.GetValue(gate)!;
            Task externalCheck;
            using (ExecutionContext.SuppressFlow())
            {
                externalCheck = Task.Run(() =>
                {
                    shouldReloadForExternalFlow = (bool)shouldReload.GetValue(gate)!;
                });
            }

            await externalCheck;
        };

        var task = Assert.IsAssignableFrom<Task>(runOwnedSave!.Invoke(gate, [save]));
        await task;

        Assert.False(shouldReloadDuringSave);
        Assert.True(shouldReloadForExternalFlow);
        Assert.True((bool)shouldReload!.GetValue(gate)!);
    }

    [Fact]
    public void MobileSettings_RoutesFeedbackToItsOwningPanels()
    {
        var view = ReadProjectFile("Noctra.Mobile", "Views", "MobileSettingsView.axaml");

        Assert.Equal(1, CountOccurrences(view, "<TextBlock Text=\"{Binding StatusMessage}\""));
        foreach (var property in new[]
                 {
                     "PlaybackStatusMessage",
                     "AppearanceStatusMessage",
                     "AudioStatusMessage",
                     "DownloadStatusMessage",
                     "ChannelStatusMessage",
                     "EpgStatusMessage",
                     "PrivacyStatusMessage",
                     "CacheStatusMessage",
                     "ResetStatusMessage"
                 })
        {
            Assert.Contains($"Text=\"{{Binding {property}}}\"", view, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CustomEpgValidationMessage_IsLocalizedForEverySupportedLanguage()
    {
        foreach (var language in new[] { "de-DE", "en-US", "es-ES", "fr-FR", "tr-TR" })
        {
            var translation = ReadProjectFile(
                "Noctra.Core", "Localization", "Translations", $"{language}.json");
            Assert.Contains("\"Settings.Error.EpgInvalidUrl\"", translation, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void SettingsViewModel_DoesNotSaveOutsideItsChangeOriginGate()
    {
        var viewModel = ReadProjectFile("Noctra.Core", "ViewModels", "SettingsViewModel.cs");

        Assert.DoesNotContain("await _settingsService.SaveAsync();", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = _settingsService.SaveAsync();", viewModel, StringComparison.Ordinal);
    }

    private static MethodInfo GetPolicyMethod(string name)
    {
        var type = typeof(SettingsViewModel).Assembly.GetType(
            "Noctra.ViewModels.CustomEpgSourcePolicy");
        Assert.NotNull(type);
        var method = type!.GetMethod(
            name,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method!;
    }

    private static bool InvokeCanAdd(MethodInfo method, string?[] values, bool isPremium) =>
        (bool)method.Invoke(null, [values, isPremium])!;

    private static int CountOccurrences(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static string ReadProjectFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NoctraPlayer.sln")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the repository root.");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
