using System.Collections.Generic;
using System.Linq;
using Material.Icons;
using Noctra.Avalonia.Localization;
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

    public static IReadOnlyList<DesktopSelectionOption> BuildDownloadOptions(MainViewModel viewModel)
        => new[]
        {
            CreateDownloadOption(viewModel, DownloadSortOrder.Latest, "Downloads.Sort.Recent"),
            CreateDownloadOption(viewModel, DownloadSortOrder.NameAZ, "Downloads.Sort.Name"),
            CreateDownloadOption(viewModel, DownloadSortOrder.SizeLarge, "Downloads.Sort.Size")
        };

    private static DesktopSelectionOption CreateDownloadOption(
        MainViewModel viewModel,
        DownloadSortOrder sortOrder,
        string localizationKey)
        => new(
            sortOrder,
            LocalizationSource.Instance[localizationKey],
            viewModel.SelectedDownloadSortOrder == sortOrder);

    public static string GetDownloadLabel(MainViewModel viewModel)
        => LocalizationSource.Instance[viewModel.SelectedDownloadSortOrder switch
        {
            DownloadSortOrder.NameAZ => "Downloads.Sort.Name",
            DownloadSortOrder.SizeLarge => "Downloads.Sort.Size",
            _ => "Downloads.Sort.Recent"
        }];

    public static MaterialIconKind GetDownloadIcon(DownloadSortOrder sortOrder)
        => sortOrder switch
        {
            DownloadSortOrder.Latest => MaterialIconKind.SortCalendarDescending,
            DownloadSortOrder.NameAZ => MaterialIconKind.SortAlphabeticalAscending,
            DownloadSortOrder.SizeLarge => MaterialIconKind.SortNumericDescending,
            _ => MaterialIconKind.Sort
        };

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
