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

    [ObservableProperty]
    private GameLogViewModel? _gameLogVM;

    [ObservableProperty]
    private BulkInviterViewModel? _bulkInviterVM;

    public InviterHubViewModel(
        InviteToGroupViewModel inviteToGroupVM,
        InstanceInviterViewModel instanceInviterVM,
        FriendInviterViewModel friendInviterVM,
        GameLogViewModel gameLogVM,
        BulkInviterViewModel bulkInviterVM)
    {
        InviteToGroupVM = inviteToGroupVM;
        InstanceInviterVM = instanceInviterVM;
        FriendInviterVM = friendInviterVM;
        GameLogVM = gameLogVM;
        BulkInviterVM = bulkInviterVM;
    }

    [RelayCommand]
    private void SelectMode(string mode)
    {
        SelectedMode = mode;
    }
}
