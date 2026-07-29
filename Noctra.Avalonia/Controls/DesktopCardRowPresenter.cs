using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Noctra.Avalonia.Controls;

public enum DesktopCardGridKind
{
    Default,
    Live,
    Vod,
    Series,
    ContinueWatching
}

public enum DesktopCardPresentationMode
{
    Default,
    Standard,
    MyList,
    Favorites,
    History
}

public sealed record DesktopCardGridRow(IReadOnlyList<object> Items);

/// <summary>
/// Recycled desktop row renderer. A row owns only the small number of controls that
/// can be visible at once; changing data updates those controls instead of recreating
/// the full catalogue visual tree.
/// </summary>
internal sealed class DesktopCardRowPresenter : WrapPanel
{
    private const double CardGap = 16;
    private readonly List<Control> _cards = new();
    private DesktopCardGridKind _kind;
    private DesktopCardPresentationMode _mode;
    private int _slotCount;

    public DesktopCardRowPresenter()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        ClipToBounds = false;
    }

    public void Populate(
        DesktopCardGridKind kind,
        DesktopCardPresentationMode mode,
        int slotCount,
        double cardWidth,
        IReadOnlyList<object> items)
    {
        if (slotCount <= 0 || !double.IsFinite(cardWidth) || cardWidth < 2)
        {
            HideAllCards();
            return;
        }

        EnsureCardSlots(kind, mode, slotCount);
        for (var index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            var item = index < items.Count ? items[index] : null;
            card.DataContext = item;
            card.IsVisible = item is not null;
            card.Width = cardWidth;
            card.Height = kind switch
            {
                DesktopCardGridKind.Vod or DesktopCardGridKind.Series => Math.Round(cardWidth * 1.5),
                _ => 96
            };
            card.Margin = new Thickness(
                0,
                0,
                index < items.Count - 1 ? CardGap : 0,
                kind == DesktopCardGridKind.Live ? 10 : CardGap);
        }
    }

    private void HideAllCards()
    {
        foreach (var card in _cards)
        {
            card.DataContext = null;
            card.IsVisible = false;
            card.Width = double.NaN;
            card.Height = double.NaN;
            card.Margin = new Thickness(0);
        }
    }

    private void EnsureCardSlots(
        DesktopCardGridKind kind,
        DesktopCardPresentationMode mode,
        int slotCount)
    {
        if (_slotCount == slotCount && _kind == kind && _mode == mode)
        {
            return;
        }

        Children.Clear();
        _cards.Clear();
        _slotCount = slotCount;
        _kind = kind;
        _mode = mode;

        for (var index = 0; index < _slotCount; index++)
        {
            var card = CreateCard(kind, mode);
            _cards.Add(card);
            Children.Add(card);
        }
    }

    private static Control CreateCard(
        DesktopCardGridKind kind,
        DesktopCardPresentationMode mode)
    {
        Control card = kind switch
        {
            DesktopCardGridKind.Live => new LiveTvCard(),
            DesktopCardGridKind.Vod => new VodCard(),
            _ => new SeriesCard()
        };

        ApplyPresentationMode(card, mode);
        return card;
    }

    private static void ApplyPresentationMode(Control card, DesktopCardPresentationMode mode)
    {
        var showHistory = mode == DesktopCardPresentationMode.History;
        var showRemoveFavorite = mode == DesktopCardPresentationMode.Favorites;
        var showRemoveMyList = mode == DesktopCardPresentationMode.MyList;

        switch (card)
        {
            case LiveTvCard live:
                live.ShowHistoryMenu = showHistory;
                live.ShowRemoveFavoriteMenu = showRemoveFavorite;
                live.ShowRemoveMyListMenu = showRemoveMyList;
                break;
            case VodCard vod:
                vod.ShowHistoryMenu = showHistory;
                vod.ShowRemoveFavoriteMenu = showRemoveFavorite;
                vod.ShowRemoveMyListMenu = showRemoveMyList;
                break;
            case SeriesCard series:
                series.ShowHistoryMenu = showHistory;
                series.ShowRemoveFavoriteMenu = showRemoveFavorite;
                series.ShowRemoveMyListMenu = showRemoveMyList;
                break;
        }
    }
}
