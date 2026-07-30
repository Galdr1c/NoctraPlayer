using System;
using System.Collections.Generic;
using Avalonia.Interactivity;
using Noctra.Models;

namespace Noctra.Avalonia.Controls;

public enum DesktopCardActionKind
{
    AddToMyList,
    ToggleFavorite,
    RemoveFromMyList,
    RemoveFromFavorites,
    RemoveFromHistory
}

public sealed record DesktopCardActionRequest(
    object Media,
    DesktopCardGridKind CardKind,
    DesktopCardPresentationMode PresentationMode);

public sealed class DesktopCardActionsRequestedEventArgs : RoutedEventArgs
{
    public DesktopCardActionsRequestedEventArgs(DesktopCardActionRequest request)
        : base(DesktopCardActions.RequestedEvent)
    {
        Request = request;
    }

    public DesktopCardActionRequest Request { get; }
}

public static class DesktopCardActions
{
    public static readonly RoutedEvent<DesktopCardActionsRequestedEventArgs> RequestedEvent =
        RoutedEvent.Register<DesktopPressableCard, DesktopCardActionsRequestedEventArgs>(
            "DesktopCardActionsRequested",
            RoutingStrategies.Bubble);

    public static void Raise(
        Interactive source,
        object media,
        DesktopCardGridKind cardKind,
        DesktopCardPresentationMode presentationMode)
    {
        source.RaiseEvent(new DesktopCardActionsRequestedEventArgs(
            new DesktopCardActionRequest(media, cardKind, presentationMode)));
    }

    public static IReadOnlyList<DesktopCardActionKind> BuildActions(
        DesktopCardActionRequest request)
    {
        var supportsFavorite = request.Media is Channel or Series;
        var supportsMyList = request.Media is Series ||
            request.Media is Channel { Type: not ChannelType.Live };

        var actions = new List<DesktopCardActionKind>(3);

        switch (request.PresentationMode)
        {
            case DesktopCardPresentationMode.MyList:
                AddFavoriteAction(actions, request.Media, supportsFavorite);
                if (supportsMyList)
                    actions.Add(DesktopCardActionKind.RemoveFromMyList);
                break;

            case DesktopCardPresentationMode.Favorites:
                AddMyListAction(actions, request.Media, supportsMyList);
                if (supportsFavorite)
                    actions.Add(DesktopCardActionKind.RemoveFromFavorites);
                break;

            case DesktopCardPresentationMode.History:
                AddMyListAction(actions, request.Media, supportsMyList);
                AddFavoriteAction(actions, request.Media, supportsFavorite);
                actions.Add(DesktopCardActionKind.RemoveFromHistory);
                break;

            default:
                AddMyListAction(actions, request.Media, supportsMyList);
                AddFavoriteAction(actions, request.Media, supportsFavorite);
                break;
        }

        // Continue Watching is a history-backed rail. It should expose the same
        // explicit removal action even though its normal card presentation mode is Default.
        if (request.CardKind == DesktopCardGridKind.ContinueWatching &&
            !actions.Contains(DesktopCardActionKind.RemoveFromHistory))
        {
            actions.Add(DesktopCardActionKind.RemoveFromHistory);
        }

        return actions;
    }

    public static bool IsFavorite(object media) => media switch
    {
        Channel channel => channel.IsFavorite,
        Series series => series.IsFavorite,
        _ => false
    };

    public static bool IsInMyList(object media) => media switch
    {
        Channel channel => channel.IsInMyList,
        Series series => series.IsInMyList,
        _ => false
    };

    public static bool IsDestructive(
        DesktopCardActionKind action,
        object media) =>
        action is DesktopCardActionKind.RemoveFromMyList
            or DesktopCardActionKind.RemoveFromFavorites
            or DesktopCardActionKind.RemoveFromHistory ||
        action == DesktopCardActionKind.ToggleFavorite && IsFavorite(media);

    private static void AddMyListAction(
        ICollection<DesktopCardActionKind> actions,
        object media,
        bool supportsMyList)
    {
        if (!supportsMyList)
            return;

        actions.Add(IsInMyList(media)
            ? DesktopCardActionKind.RemoveFromMyList
            : DesktopCardActionKind.AddToMyList);
    }

    private static void AddFavoriteAction(
        ICollection<DesktopCardActionKind> actions,
        object media,
        bool supportsFavorite)
    {
        if (supportsFavorite)
            actions.Add(DesktopCardActionKind.ToggleFavorite);
    }
}
