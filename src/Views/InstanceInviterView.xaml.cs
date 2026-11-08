using System.Windows.Controls;

namespace VRCGroupTools.Views;

public partial class InstanceInviterView : UserControl
{
    private void OnFilterTextChanged(object sender, TextChangedEventArgs args)
    {
        if (DataContext is VRCGroupTools.ViewModels.InstanceInviterViewModel { AutoInviteEnabled: true } vm)
            vm.StopAutoInvite("Auto Invite off: filter text edited. Correct/review filters and explicitly enable again.");
    }
    private void OnApplicationExit(object sender, System.Windows.ExitEventArgs args) => (DataContext as VRCGroupTools.ViewModels.InstanceInviterViewModel)?.Dispose();
    public InstanceInviterView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (System.Windows.Application.Current is { } app) { app.Exit -= OnApplicationExit; app.Exit += OnApplicationExit; }
        };
        Unloaded += (_, _) =>
        {
            (DataContext as VRCGroupTools.ViewModels.InstanceInviterViewModel)?.StopAutoInvite("Auto Invite stopped: left Instance Inviter view.");
            if (System.Windows.Application.Current is { } app) app.Exit -= OnApplicationExit;
        };
        DataContextChanged += (_, e) => (e.OldValue as VRCGroupTools.ViewModels.InstanceInviterViewModel)?.StopAutoInvite("Auto Invite stopped: view context changed.");
    }
}
