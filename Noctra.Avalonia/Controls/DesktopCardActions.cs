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

        if (request.CardKind == DesktopCardGridKind.ContinueWatching)
            return BuildStandardActions(supportsMyList, supportsFavorite);

        if (request.CardKind == DesktopCardGridKind.Live)
        {
            return request.PresentationMode switch
            {
                DesktopCardPresentationMode.MyList =>
                [
                    DesktopCardActionKind.ToggleFavorite,
                    DesktopCardActionKind.RemoveFromMyList
                ],
                DesktopCardPresentationMode.Favorites =>
                [
                    DesktopCardActionKind.RemoveFromFavorites
                ],
                DesktopCardPresentationMode.History =>
                [
                    DesktopCardActionKind.RemoveFromHistory
                ],
                _ => supportsFavorite
                    ? [DesktopCardActionKind.ToggleFavorite]
                    : []
            };
        }

        return request.PresentationMode switch
        {
            DesktopCardPresentationMode.MyList => supportsFavorite
                ?
                [
                    DesktopCardActionKind.ToggleFavorite,
                    DesktopCardActionKind.RemoveFromMyList
                ]
                :
                [
                    DesktopCardActionKind.RemoveFromMyList
                ],
            DesktopCardPresentationMode.Favorites => supportsMyList
                ?
                [
                    DesktopCardActionKind.AddToMyList,
                    DesktopCardActionKind.RemoveFromFavorites
                ]
                :
                [
                    DesktopCardActionKind.RemoveFromFavorites
                ],
            DesktopCardPresentationMode.History =>
                BuildHistoryActions(supportsMyList, supportsFavorite),
            _ => BuildStandardActions(supportsMyList, supportsFavorite)
        };
    }

    public static bool IsDestructive(DesktopCardActionKind action) =>
        action is DesktopCardActionKind.RemoveFromMyList
            or DesktopCardActionKind.RemoveFromFavorites
            or DesktopCardActionKind.RemoveFromHistory;

    private static IReadOnlyList<DesktopCardActionKind> BuildStandardActions(
        bool supportsMyList,
        bool supportsFavorite)
    {
        if (supportsMyList && supportsFavorite)
            return [DesktopCardActionKind.AddToMyList, DesktopCardActionKind.ToggleFavorite];
        if (supportsMyList)
            return [DesktopCardActionKind.AddToMyList];
        return supportsFavorite
            ? [DesktopCardActionKind.ToggleFavorite]
            : [];
    }

    private static IReadOnlyList<DesktopCardActionKind> BuildHistoryActions(
        bool supportsMyList,
        bool supportsFavorite)
    {
        var actions = new List<DesktopCardActionKind>(3);
        if (supportsMyList)
            actions.Add(DesktopCardActionKind.AddToMyList);
        if (supportsFavorite)
            actions.Add(DesktopCardActionKind.ToggleFavorite);
        actions.Add(DesktopCardActionKind.RemoveFromHistory);
        return actions;
    }
}
