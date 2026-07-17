using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Noctra.Mobile.Controls;

public enum MobileCardPresentationMode
{
    Standard,
    MyList,
    Favorites,
    History
}

/// <summary>
/// Shared recycled card-row renderer for both primary grids and sectioned feeds.
/// Card controls are created only when the row shape changes; recycling otherwise
/// updates their data context, visibility, and responsive dimensions in place.
/// </summary>
internal sealed class MobileCardRowPresenter : WrapPanel
{
    private const double CardGap = 16;
    private readonly List<Control> _cards = new();
    private MobileCardGridKind _kind;
    private MobileCardPresentationMode _mode;
    private int _slotCount;

    public MobileCardRowPresenter()
    {
        Orientation = Orientation.Horizontal;
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    public void Populate(
        MobileCardGridKind kind,
        MobileCardPresentationMode mode,
        int slotCount,
        double cardWidth,
        IReadOnlyList<object> items)
    {
        EnsureCardSlots(kind, mode, slotCount);
        for (var index = 0; index < _cards.Count; index++)
        {
            var card = _cards[index];
            var item = index < items.Count ? items[index] : null;
            card.DataContext = item;
            card.IsVisible = item != null;
            card.Width = cardWidth;
            card.Height = kind is MobileCardGridKind.Vod or MobileCardGridKind.Series
                ? Math.Round(cardWidth * 1.5)
                : double.NaN;
            card.Margin = new Thickness(
                0,
                0,
                index < items.Count - 1 ? CardGap : 0,
                kind == MobileCardGridKind.Live ? 0 : CardGap);
        }
    }

    private void EnsureCardSlots(
        MobileCardGridKind kind,
        MobileCardPresentationMode mode,
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
        MobileCardGridKind kind,
        MobileCardPresentationMode mode)
    {
        Control card = kind switch
        {
            MobileCardGridKind.Live => new MobileLiveTvCard(),
            MobileCardGridKind.Vod => new MobileVodCard(),
            _ => new MobileSeriesCard()
        };
        ApplyPresentationMode(card, mode);
        return card;
    }

    private static void ApplyPresentationMode(Control card, MobileCardPresentationMode mode)
    {
        if (card is MobileLiveTvCard live)
        {
            live.ShowHistoryMenu = mode == MobileCardPresentationMode.History;
            live.ShowMyListMenu = mode == MobileCardPresentationMode.MyList;
            live.ShowFavoriteMenu = mode is not MobileCardPresentationMode.History and
                not MobileCardPresentationMode.Favorites;
            live.ShowRemoveFavoriteMenu = mode == MobileCardPresentationMode.Favorites;
            return;
        }

        if (card is MobileVodCard vod)
        {
            vod.ShowHistoryMenu = mode == MobileCardPresentationMode.History;
            vod.ShowRemoveFavoriteMenu = mode == MobileCardPresentationMode.Favorites;
            vod.ShowRemoveMyListMenu = mode == MobileCardPresentationMode.MyList;
            return;
        }

        if (card is MobileSeriesCard series)
        {
            series.ShowHistoryMenu = mode == MobileCardPresentationMode.History;
            series.ShowRemoveFavoriteMenu = mode == MobileCardPresentationMode.Favorites;
            series.ShowRemoveMyListMenu = mode == MobileCardPresentationMode.MyList;
        }
    }
}
