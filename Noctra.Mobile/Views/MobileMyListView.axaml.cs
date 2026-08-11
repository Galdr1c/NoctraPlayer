using Avalonia.Controls;
using Noctra.Mobile.Navigation;

namespace Noctra.Mobile.Views;

public partial class MobileMyListView : UserControl, IMobileNavigationStateParticipant
{
    public MobileMyListView()
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
