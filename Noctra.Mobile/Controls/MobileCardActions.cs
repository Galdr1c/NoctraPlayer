using System;
using System.Collections.Generic;
using Avalonia.Interactivity;
using Noctra.Models;

namespace Noctra.Mobile.Controls;

public enum MobileCardActionKind
{
    AddToMyList,
    ToggleFavorite,
    RemoveFromMyList,
    RemoveFromFavorites,
    RemoveFromHistory
}

public sealed record MobileCardActionRequest(
    object Media,
    MobileCardGridKind CardKind,
    MobileCardPresentationMode PresentationMode);

public sealed class MobileCardActionsRequestedEventArgs : RoutedEventArgs
{
    public MobileCardActionsRequestedEventArgs(MobileCardActionRequest request)
        : base(MobileCardActions.RequestedEvent)
    {
        Request = request;
    }

    public MobileCardActionRequest Request { get; }
}

public static class MobileCardActions
{
    public static readonly RoutedEvent<MobileCardActionsRequestedEventArgs> RequestedEvent =
        RoutedEvent.Register<MobilePressableCard, MobileCardActionsRequestedEventArgs>(
            "CardActionsRequested",
            RoutingStrategies.Bubble);

    public static void Raise(
        Interactive source,
        object media,
        MobileCardGridKind cardKind,
        MobileCardPresentationMode presentationMode)
    {
        source.RaiseEvent(new MobileCardActionsRequestedEventArgs(
            new MobileCardActionRequest(media, cardKind, presentationMode)));
    }

    public static IReadOnlyList<MobileCardActionKind> BuildActions(
        MobileCardActionRequest request)
    {
        var supportsFavorite = request.Media is Channel or Series;
        var supportsMyList = request.Media is Series ||
            request.Media is Channel { Type: not ChannelType.Live };

        if (request.CardKind == MobileCardGridKind.ContinueWatching)
        {
            var actions = new List<MobileCardActionKind>(
                BuildStandardActions(supportsMyList, supportsFavorite));
            actions.Add(MobileCardActionKind.RemoveFromHistory);
            return actions;
        }

        if (request.CardKind == MobileCardGridKind.Live)
        {
            return request.PresentationMode switch
            {
                MobileCardPresentationMode.MyList =>
                [
                    MobileCardActionKind.ToggleFavorite,
                    MobileCardActionKind.RemoveFromMyList
                ],
                MobileCardPresentationMode.Favorites =>
                [
                    MobileCardActionKind.RemoveFromFavorites
                ],
                MobileCardPresentationMode.History =>
                [
                    MobileCardActionKind.RemoveFromHistory
                ],
                _ => supportsFavorite
                    ? [MobileCardActionKind.ToggleFavorite]
                    : []
            };
        }

        return request.PresentationMode switch
        {
            MobileCardPresentationMode.MyList => supportsFavorite
                ?
                [
                    MobileCardActionKind.ToggleFavorite,
                    MobileCardActionKind.RemoveFromMyList
                ]
                :
                [
                    MobileCardActionKind.RemoveFromMyList
                ],
            MobileCardPresentationMode.Favorites => supportsMyList
                ?
                [
                    MobileCardActionKind.AddToMyList,
                    MobileCardActionKind.RemoveFromFavorites
                ]
                :
                [
                    MobileCardActionKind.RemoveFromFavorites
                ],
            MobileCardPresentationMode.History =>
                BuildHistoryActions(supportsMyList, supportsFavorite),
            _ => BuildStandardActions(supportsMyList, supportsFavorite)
        };
    }

    public static bool IsDestructive(MobileCardActionKind action) =>
        action is MobileCardActionKind.RemoveFromMyList
            or MobileCardActionKind.RemoveFromFavorites
            or MobileCardActionKind.RemoveFromHistory;

    private static IReadOnlyList<MobileCardActionKind> BuildStandardActions(
        bool supportsMyList,
        bool supportsFavorite)
    {
        if (supportsMyList && supportsFavorite)
        {
            return
            [
                MobileCardActionKind.AddToMyList,
                MobileCardActionKind.ToggleFavorite
            ];
        }

        if (supportsMyList)
        {
            return [MobileCardActionKind.AddToMyList];
        }

        return supportsFavorite
            ? [MobileCardActionKind.ToggleFavorite]
            : [];
    }

    private static IReadOnlyList<MobileCardActionKind> BuildHistoryActions(
        bool supportsMyList,
        bool supportsFavorite)
    {
        var actions = new List<MobileCardActionKind>(3);
        if (supportsMyList)
        {
            actions.Add(MobileCardActionKind.AddToMyList);
        }

        if (supportsFavorite)
        {
            actions.Add(MobileCardActionKind.ToggleFavorite);
        }

        actions.Add(MobileCardActionKind.RemoveFromHistory);
        return actions;
    }
}
