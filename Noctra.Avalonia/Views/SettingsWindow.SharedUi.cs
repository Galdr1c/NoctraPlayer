using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Noctra.UI.Views;

namespace Noctra.Avalonia.Views;

public partial class SettingsWindow
{
    private bool _sharedProfileSettingsInstalled;
    private bool _sharedThemePickerInstalled;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        InstallSharedProfileSettings();
        InstallSharedThemePicker();
    }

    private void InstallSharedProfileSettings()
    {
        if (_sharedProfileSettingsInstalled)
            return;

        // The Profile tab is selected by default, so its three legacy SettingsCard
        // blocks are realized when the window opens. Replace only that exact group
        // with the same mobile-first shared control used by MobileSettingsView.
        var profileCards = this.GetVisualDescendants()
            .OfType<Border>()
            .Where(border => border.Classes.Contains("SettingsCard"))
            .Take(3)
            .ToArray();

        if (profileCards.Length != 3 ||
            profileCards[0].Parent is not StackPanel parent ||
            profileCards.Any(card => !ReferenceEquals(card.Parent, parent)))
        {
            return;
        }

        var insertIndex = parent.Children.IndexOf(profileCards[0]);
        if (insertIndex < 0)
            return;

        foreach (var card in profileCards)
            parent.Children.Remove(card);

        var sharedSections = new SettingsCommonSectionsView();
        sharedSections.BackToProfilesRequested += SharedSettingsBackToProfilesRequested;
        parent.Children.Insert(insertIndex, sharedSections);
        _sharedProfileSettingsInstalled = true;
    }

    private void InstallSharedThemePicker()
    {
        if (_sharedThemePickerInstalled)
            return;

        // Keep language and every other Appearance control desktop-specific for now,
        // but use the exact same theme picker presentation as mobile.
        if (DarkThemeButton.Parent is not StackPanel themeHost ||
            !ReferenceEquals(LightThemeButton.Parent, themeHost))
        {
            return;
        }

        themeHost.Children.Clear();
        themeHost.Children.Add(new SettingsThemePickerView { Width = 276 });
        _sharedThemePickerInstalled = true;
    }

    private void SharedSettingsBackToProfilesRequested(object? sender, RoutedEventArgs e)
        => BackToProfiles_Click(sender, e);
}
