using Avalonia.Controls;
using Noctra.Mobile.Navigation;

namespace Noctra.Mobile.Views;

public partial class MobileFavoritesView : UserControl, IMobileNavigationStateParticipant
{
    public MobileFavoritesView()
    {
        InitializeComponent();
    }

    bool IMobileNavigationStateParticipant.TryCaptureNavigationState(out MobilePageScrollState state)
        => MobileNavigationScrollState.TryCapture(PrimaryScrollContent, out state);

    bool IMobileNavigationStateParticipant.TryRestoreNavigationState(
        MobilePageScrollState state,
        bool allowClamping)
        => MobileNavigationScrollState.TryRestore(PrimaryScrollContent, state, allowClamping);
}
