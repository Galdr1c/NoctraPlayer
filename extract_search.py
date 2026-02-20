import re

axaml_path = r'd:\IPTVPlayer\Noctra.Avalonia\MainWindow.axaml'
with open(axaml_path, 'r', encoding='utf-8') as f:
    content = f.read()

start_idx = content.find('<ScrollViewer x:Name="SearchView"')
empty_search_idx = content.find('<!-- Empty search -->')
end_idx = content.find('</ScrollViewer>', empty_search_idx) + 15

if start_idx == -1 or empty_search_idx == -1 or end_idx == -1:
    print('Could not find bounds for SearchView.')
    exit(1)

view_content = content[start_idx:end_idx]

first_bracket = content.find('>', start_idx)
tag_content = content[start_idx:first_bracket+1]
is_visible_match = re.search('IsVisible="[^"]*"', tag_content)
is_visible_attr = is_visible_match.group(0) if is_visible_match else ''
indent = '        '
replacement = f'{indent}<views:SearchView {is_visible_attr} />\n'

user_control_content = f'''<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="using:Noctra.ViewModels"
             xmlns:models="using:Noctra.Models"
             xmlns:controls="using:Noctra.Avalonia.Controls"
             xmlns:views="using:Noctra.Avalonia.Views"
             xmlns:materialIcons="clr-namespace:Material.Icons.Avalonia;assembly=Material.Icons.Avalonia"
             mc:Ignorable="d" d:DesignWidth="1000" d:DesignHeight="600"
             x:Class="Noctra.Avalonia.Views.SearchView"
             x:DataType="vm:MainViewModel">
    {view_content.replace('x:Name="SearchView"', 'x:Name="SearchScrollViewer"')}
</UserControl>'''

with open(r'd:\IPTVPlayer\Noctra.Avalonia\Views\SearchView.axaml', 'w', encoding='utf-8') as f:
    f.write(user_control_content)

content = content[:start_idx] + replacement + content[end_idx:]

with open(axaml_path, 'w', encoding='utf-8') as f:
    f.write(content)

cs_template = '''using Avalonia.Controls;
using Avalonia.Interactivity;
using Noctra.ViewModels;
using Noctra.Models;

namespace Noctra.Avalonia.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private async void Context_AddToMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.AddToMyListCommand.ExecuteAsync(media);
    }

    private async void Context_ToggleFavorite_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.ToggleFavoriteCommand.ExecuteAsync(media);
    }

    private async void Context_RemoveFromMyList_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.RemoveFromMyListCommand.ExecuteAsync(media);
    }

    private async void Context_RemoveFromFavorites_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem) return;
        var media = ResolveContextMedia(menuItem);
        if (media != null && ViewModel != null) await ViewModel.RemoveFromFavoritesCommand.ExecuteAsync(media);
    }

    private static object? ResolveContextMedia(MenuItem menuItem)
    {
        if (menuItem.CommandParameter is Channel || menuItem.CommandParameter is Series) return menuItem.CommandParameter;
        if (menuItem.Tag is Channel || menuItem.Tag is Series) return menuItem.Tag;
        if (menuItem.DataContext is Channel || menuItem.DataContext is Series) return menuItem.DataContext;
        if (menuItem.Parent is ContextMenu contextMenu &&
            contextMenu.PlacementTarget is global::Avalonia.StyledElement placementTarget &&
            (placementTarget.DataContext is Channel || placementTarget.DataContext is Series))
            return placementTarget.DataContext;
        if (menuItem.Parent is ContextMenu ownerMenu &&
            ownerMenu.PlacementTarget is Control placementControl)
        {
            var parent = placementControl.Parent;
            while (parent != null)
            {
                if (parent is global::Avalonia.StyledElement styled && (styled.DataContext is Channel || styled.DataContext is Series)) return styled.DataContext;
                parent = parent.Parent;
            }
        }
        return null;
    }
}'''

with open(r'd:\IPTVPlayer\Noctra.Avalonia\Views\SearchView.axaml.cs', 'w', encoding='utf-8') as f:
    f.write(cs_template)

print('Done extracting Search!')
