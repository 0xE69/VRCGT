using System.Reflection;
using VRCGroupTools.Data;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;
using CommunityToolkit.Mvvm.Input;

static class ViewModelTests
{
    public static async Task PerFriendOutcomes()
    {
        var f = new Fixture();
        f.Send = user => user switch { "failed" => Task.FromResult(false), "unknown" => throw new System.IO.IOException("private"), _ => Task.FromResult(true) };
        var vm = Create(f);
        foreach (var id in new[] { "sent", "failed", "unknown" })
            vm.Friends.Add(new(new FriendInfo { UserId = id, DisplayName = id }, vm) { IsSelected = true });
        vm.UpdateSelectedCount();
        await vm.InviteToCurrentInstanceCommand.ExecuteAsync(null);
        var result = typeof(FriendInviterItemViewModel).GetProperty("InstanceInviteResult");
        ApiTests.Assert(result != null, "Per-friend outcome is not exposed to the UI");
        ApiTests.Assert(vm.Friends.Select(f => result!.GetValue(f)?.ToString()?.Split(' ')[0]).SequenceEqual(new[] { "Sent", "Failed", "Unknown" }), "Per-friend outcomes must reflect real transport results");
        ApiTests.Assert(!vm.Friends[0].IsSelected && vm.Friends[1].IsSelected && !vm.Friends[2].IsSelected, "Unknown invites must not remain selected for accidental retry");
    }

    public static async Task BusyBatchBlocksActions()
    {
        var f = new Fixture();
        var pending = new TaskCompletionSource<bool>();
        f.Send = _ => pending.Task;
        var vm = Create(f);
        vm.Friends.Add(new(new FriendInfo { UserId = "a", DisplayName = "A" }, vm) { IsSelected = true });
        vm.UpdateSelectedCount();
        var running = vm.InviteToCurrentInstanceCommand.ExecuteAsync(null);
        try
        {
            ApiTests.Assert(!vm.InviteToGroupCommand.CanExecute(null) && !vm.RefreshCommand.CanExecute(null)
                && !vm.SyncMembersCommand.CanExecute(null) && !vm.SelectAllCommand.CanExecute(null), "Other actions remain enabled during instance invites");
        }
        finally { pending.TrySetResult(true); await running; }
        ApiTests.Assert(vm.RefreshCommand.CanExecute(null), "Actions must recover after the batch");
    }

    public static FriendInviterViewModel Create(Fixture f) => new(
        StubProxy.Create<IVRChatApiService>((m, a) => m.Name == "GetFriendsAsync" ? Task.FromResult(new List<FriendInfo>()) : null),
        StubProxy.Create<ISettingsService>(), StubProxy.Create<IDatabaseService>(), StubProxy.Create<ICacheService>(), f.Workflow);

    public static async Task SelectedFriendsWithoutGroup()
    {
        var f = new Fixture();
        var constructor = typeof(FriendInviterViewModel).GetConstructors().SingleOrDefault(c => c.GetParameters().Any(p => p.ParameterType == typeof(CurrentInstanceInviteWorkflow)));
        ApiTests.Assert(constructor != null, "Friend Manager has no current-instance workflow");
        var api = StubProxy.Create<IVRChatApiService>((method, args) => method.Name == "GetFriendsAsync" ? Task.FromResult(new List<FriendInfo>()) : null);
        var vm = (FriendInviterViewModel)constructor!.Invoke(new object[] { api, StubProxy.Create<ISettingsService>(), StubProxy.Create<IDatabaseService>(), StubProxy.Create<ICacheService>(), f.Workflow });
        var selected = new FriendInviterItemViewModel(new FriendInfo { UserId = "selected", DisplayName = "Selected" }, vm, true) { IsSelected = true };
        vm.Friends.Add(selected);
        vm.Friends.Add(new(new FriendInfo { UserId = "unselected", DisplayName = "Unselected" }, vm));
        vm.UpdateSelectedCount();
        ApiTests.Assert(f.Sent.Count == 0, "Loading must never auto-invite");
        var command = typeof(FriendInviterViewModel).GetProperty("InviteToCurrentInstanceCommand")?.GetValue(vm) as IAsyncRelayCommand;
        ApiTests.Assert(command != null, "Current instance action is missing");
        await command!.ExecuteAsync(null);
        ApiTests.Assert(f.Sent.SequenceEqual(new[] { "selected" }), "Invite selected friends even when in group; no group configuration required");
        ApiTests.Assert(!selected.IsSelected && vm.SelectedCount == 0, "Successful recipients should be deselected");
        ApiTests.Assert(vm.StatusMessage?.Contains("1 sent") == true, "Visible summary must reflect actual sends");
    }
}

public class StubProxy : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?>? Handler;
    public static T Create<T>(Func<MethodInfo, object?[]?, object?>? handler = null) where T : class
    {
        var proxy = DispatchProxy.Create<T, StubProxy>();
        ((StubProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler?.Invoke(targetMethod!, args);
}
