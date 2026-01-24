using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace VRCGroupTools.ViewModels;

public partial class InviterHubViewModel : ObservableObject
{
    [ObservableProperty]
    private string _selectedMode = "GroupInviter";

    [ObservableProperty]
    private InviteToGroupViewModel? _inviteToGroupVM;

    [ObservableProperty]
    private InstanceInviterViewModel? _instanceInviterVM;

    [ObservableProperty]
    private FriendInviterViewModel? _friendInviterVM;

    public InviterHubViewModel(
        InviteToGroupViewModel inviteToGroupVM,
        InstanceInviterViewModel instanceInviterVM,
        FriendInviterViewModel friendInviterVM)
    {
        InviteToGroupVM = inviteToGroupVM;
        InstanceInviterVM = instanceInviterVM;
        FriendInviterVM = friendInviterVM;
    }

    [RelayCommand]
    private void SelectMode(string mode)
    {
        SelectedMode = mode;
    }
}
