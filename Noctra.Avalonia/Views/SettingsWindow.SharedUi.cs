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

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        InstallSharedProfileSettings();
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

    private void SharedSettingsBackToProfilesRequested(object? sender, RoutedEventArgs e)
        => BackToProfiles_Click(sender, e);
}
