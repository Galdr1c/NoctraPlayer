using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using SharedSettingsCommonSectionsView = Noctra.UI.Views.SettingsCommonSectionsView;
using SharedSettingsThemePickerView = Noctra.UI.Views.SettingsThemePickerView;

namespace Noctra.Mobile.Views;

public partial class MobileSettingsView
{
    private SharedSettingsCommonSectionsView? _sharedCommonSettingsSections;
    private SharedSettingsThemePickerView? _sharedThemePicker;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        InstallSharedSettingsPresentation();
    }

    private void InstallSharedSettingsPresentation()
    {
        if (SettingsScrollViewer.Content is not StackPanel root)
            return;

        InstallSharedProfileAndAccountSections(root);
        InstallSharedThemePicker();
    }

    private void InstallSharedProfileAndAccountSections(StackPanel root)
    {
        if (_sharedCommonSettingsSections is not null &&
            root.Children.Contains(_sharedCommonSettingsSections))
        {
            return;
        }

        // Mobile Settings is the canonical design. Replace only the first three
        // cards whose structure is identical cross-platform: current profile,
        // profile management and provider/account information. Appearance keeps
        // its language selector and Playback keeps its network/quality controls.
        var sharedCandidates = root.Children
            .OfType<Border>()
            .Where(border => border.Classes.Contains("SettingsSectionCard"))
            .Take(3)
            .ToArray();

        if (sharedCandidates.Length != 3)
            return;

        var insertIndex = root.Children.IndexOf(sharedCandidates[0]);
        if (insertIndex < 0)
            return;

        foreach (var candidate in sharedCandidates)
            root.Children.Remove(candidate);

        _sharedCommonSettingsSections = new SharedSettingsCommonSectionsView();
        _sharedCommonSettingsSections.BackToProfilesRequested += SharedCommonSettingsSections_BackToProfilesRequested;
        root.Children.Insert(insertIndex, _sharedCommonSettingsSections);
    }

    private void InstallSharedThemePicker()
    {
        if (_sharedThemePicker is not null && _sharedThemePicker.Parent is not null)
            return;

        // Only replace the two-card theme chooser inside Appearance. The language
        // selector/status controls around it remain mobile-host content.
        if (DarkThemeButton.Parent is not Grid legacyThemeGrid ||
            legacyThemeGrid.Parent is not StackPanel appearanceThemeContainer)
        {
            return;
        }

        var index = appearanceThemeContainer.Children.IndexOf(legacyThemeGrid);
        if (index < 0)
            return;

        appearanceThemeContainer.Children.Remove(legacyThemeGrid);
        _sharedThemePicker = new SharedSettingsThemePickerView();
        appearanceThemeContainer.Children.Insert(index, _sharedThemePicker);
    }

    private void SharedCommonSettingsSections_BackToProfilesRequested(object? sender, RoutedEventArgs e)
        => BackToProfilesRequested?.Invoke(this, EventArgs.Empty);
}
