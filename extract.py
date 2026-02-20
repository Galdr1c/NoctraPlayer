import sys
import re
import os

views = ['Home', 'Live', 'Movies', 'Series', 'MyList', 'Favorites', 'History', 'Downloads']

axaml_path = r'd:\\IPTVPlayer\\Noctra.Avalonia\\MainWindow.axaml'
with open(axaml_path, 'r', encoding='utf-8') as f:
    content = f.read()

for view in views:
    start_str = f'<ScrollViewer x:Name="{view}View"'
    start_idx = content.find(start_str)
    if start_idx == -1:
        print(f"Could not find {view}View")
        continue

    # Find the namespace prefix if any, usually empty space before
    line_start = content.rfind('\n', 0, start_idx)
    indent = content[line_start+1:start_idx]

    temp_idx = start_idx
    depth = 0
    end_idx = -1

    while temp_idx < len(content):
        next_open = content.find('<ScrollViewer', temp_idx)
        next_close = content.find('</ScrollViewer>', temp_idx)

        if next_close == -1:
            break

        if next_open != -1 and next_open < next_close:
            depth += 1
            temp_idx = next_open + 13
        else:
            depth -= 1
            temp_idx = next_close + 15
            if depth == 0:
                end_idx = temp_idx
                break

    if end_idx != -1:
        view_content = content[start_idx:end_idx]

        first_bracket = content.find('>', start_idx)
        tag_content = content[start_idx:first_bracket+1]

        is_visible_match = re.search(r'IsVisible="[^"]*"', tag_content)
        is_visible_attr = is_visible_match.group(0) if is_visible_match else ""

        replacement = f'<views:{view}View {is_visible_attr} Grid.Column="1" />'
        # Wait, the ScrollViewer was inside Grid.Column="1" maybe?
        # Yes, <Grid Grid.Column="1"> is the parent of all these scroll viewers.
        # But replacing it inline is fine without Grid.Column="1" because the parent is already Grid.Column="1".
        replacement = f'{indent}<views:{view}View {is_visible_attr} />\n'

        user_control_content = f"""<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:Noctra.ViewModels"
             xmlns:models="using:Noctra.Models"
             xmlns:controls="using:Noctra.Avalonia.Controls"
             xmlns:views="using:Noctra.Avalonia.Views"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="600"
             x:Class="Noctra.Avalonia.Views.{view}View"
             x:DataType="vm:MainViewModel">
    {view_content}
</UserControl>"""

        with open(f'd:\\IPTVPlayer\\Noctra.Avalonia\\Views\\{view}View.axaml', 'w', encoding='utf-8') as f_out:
            f_out.write(user_control_content)

        # Update content string
        content = content[:start_idx] + replacement + content[end_idx:]

with open(axaml_path, 'w', encoding='utf-8') as f:
    f.write(content)

print("Done extracting XAML.")

# Create the C# code-behind files
cs_template = """using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.ViewModels;
using Noctra.Models;

namespace Noctra.Avalonia.Views;

public partial class {view}View : UserControl
{{
    public {view}View()
    {{
        InitializeComponent();
    }}

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void ClearGroupSelection_Click(object? sender, RoutedEventArgs e)
    {{
        if (ViewModel != null) ViewModel.SelectedGroup = null;
    }}

    private void {view}View_ScrollChanged(object? sender, ScrollChangedEventArgs e)
    {{
        // Logic will be moved here
    }}

    // Shared Context menu handlers
    private async void Context_AddToMyList_Click(object? sender, RoutedEventArgs e)
    {{
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.AddToMyListCommand.ExecuteAsync(media);
    }}

    private async void Context_ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {{
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.ToggleFavoriteCommand.ExecuteAsync(media);
    }}

    private async void Context_RemoveFromMyList_Click(object? sender, RoutedEventArgs e)
    {{
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.RemoveFromMyListCommand.ExecuteAsync(media);
    }}

    private async void Context_RemoveFromFavorites_Click(object? sender, RoutedEventArgs e)
    {{
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.RemoveFromFavoritesCommand.ExecuteAsync(media);
    }}

    private static object? ResolveContextMedia(MenuItem menuItem)
    {{
        if (menuItem.CommandParameter is Channel || menuItem.CommandParameter is Series) return menuItem.CommandParameter;
        if (menuItem.Tag is Channel || menuItem.Tag is Series) return menuItem.Tag;
        if (menuItem.DataContext is Channel || menuItem.DataContext is Series) return menuItem.DataContext;
        if (menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is Avalonia.Controls.StyledElement placementTarget &&
            (placementTarget.DataContext is Channel || placementTarget.DataContext is Series))
            return placementTarget.DataContext;
        if (menuItem.Parent is ContextMenu ownerMenu &&
            ownerMenu.PlacementTarget is Control placementControl)
        {{
            var parent = placementControl.Parent;
            while (parent != null)
            {{
                if (parent.DataContext is Channel || parent.DataContext is Series) return parent.DataContext;
                parent = parent.Parent;
            }}
        }}
        return null;
    }}
}}
"""

for view in views:
    with open(f'd:\\IPTVPlayer\\Noctra.Avalonia\\Views\\{view}View.axaml.cs', 'w', encoding='utf-8') as f_out:
        f_out.write(cs_template.format(view=view))

print("Done extracting C#.")
