using Avalonia.Controls;
using Noctra.Mobile.Navigation;

namespace Noctra.Mobile.Views;

public partial class MobileHomeView : UserControl, IMobileNavigationStateParticipant
{
    public MobileHomeView()
    {
        InitializeComponent();
    }

    bool IMobileNavigationStateParticipant.TryCaptureNavigationState(out MobilePageScrollState state)
        => MobileNavigationScrollState.TryCapture(ContinueWatchingGrid, out state);

    bool IMobileNavigationStateParticipant.TryRestoreNavigationState(
        MobilePageScrollState state,
        bool allowClamping)
        => MobileNavigationScrollState.TryRestore(ContinueWatchingGrid, state, allowClamping);
}
