using System.Collections.Generic;
using System.Linq;
using Material.Icons;
using Noctra.Models;
using Noctra.ViewModels;

namespace Noctra.Avalonia.Views;

internal static class DesktopContentSortSelection
{
    public static IReadOnlyList<DesktopSelectionOption> BuildOptions(MainViewModel viewModel)
        => viewModel.SortOptions
            .Select(option => new DesktopSelectionOption(
                option.Key,
                option.Value,
                option.Key == viewModel.SelectedSortOrder))
            .ToArray();

    public static string GetSelectedLabel(MainViewModel viewModel)
        => viewModel.SortOptions
            .FirstOrDefault(option => option.Key == viewModel.SelectedSortOrder)
            .Value ?? string.Empty;

    public static MaterialIconKind GetIcon(ChannelSortOrder sortOrder)
        => sortOrder switch
        {
            ChannelSortOrder.NewestFirst => MaterialIconKind.SortCalendarDescending,
            ChannelSortOrder.OldestFirst => MaterialIconKind.SortCalendarAscending,
            ChannelSortOrder.NameAsc => MaterialIconKind.SortAlphabeticalAscending,
            ChannelSortOrder.NameDesc => MaterialIconKind.SortAlphabeticalDescending,
            _ => MaterialIconKind.Sort
        };
}
