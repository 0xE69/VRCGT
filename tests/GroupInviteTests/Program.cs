using VRCGroupTools.Services;
using System.Net;
using System.Net.Http;
using System.IO;
var path = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")!, "vrcgt-tests-" + Guid.NewGuid() + ".db");
try
{
 var ledger = new GroupInviteLedger(path);
 var handler = new FakeHandler();
 using var http = new HttpClient(handler) { BaseAddress = new Uri("https://offline.invalid/") };
 var ctor = typeof(VRChatApiService).GetConstructor(new[] { typeof(HttpClient), typeof(GroupInviteLedger) });
 Check(ctor != null, "Central dispatch must use injectable shared ledger");
 var first = (VRChatApiService)ctor!.Invoke(new object[] { http, ledger });
 typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(first,true);
 Check(await first.SendGroupInviteAsync("grp_11111111-1111-1111-1111-111111111111", "user-a"), "Initial invite succeeds");
 var second = (VRChatApiService)ctor.Invoke(new object[] { http, new GroupInviteLedger(path) });
 typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(second,true);
 await Suppressed(() => second.SendGroupInviteAsync("grp_11111111-1111-1111-1111-111111111111", "user-a"));
 Check(handler.Calls == 1, "Reload/cross-entrypoint duplicate must not dispatch HTTP");
 Check(await second.SendGroupInviteAsync("group-b", "user-a"), "Different group must remain eligible");
 Check(handler.Calls == 2, "Per-group scope");
 Console.WriteLine("PASS persistence, cross-service suppression, per-group scope");
 var gate = new TaskCompletionSource<HttpResponseMessage>();
 handler.Respond = () => gate.Task;
 var pending = first.SendGroupInviteAsync("grp_11111111-1111-1111-1111-111111111111", "overlap");
 await Suppressed(() => second.SendGroupInviteAsync("grp_11111111-1111-1111-1111-111111111111", "overlap"));
 gate.SetException(new HttpRequestException("lost response"));
 await Suppressed(() => pending);
 Check(await ledger.GetStateAsync("grp_11111111-1111-1111-1111-111111111111", "overlap") == GroupInviteState.Unknown, "Uncertainty must persist");
 var calls = handler.Calls;
 await Suppressed(() => second.SendGroupInviteAsync("grp_11111111-1111-1111-1111-111111111111", "overlap"));
 Check(handler.Calls == calls, "Unknown repeat must not dispatch");
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
 Check(!await first.SendGroupInviteAsync("grp_11111111-1111-1111-1111-111111111111", "rejected"), "Proven rejection returns false");
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
 Check(await second.SendGroupInviteAsync("grp_11111111-1111-1111-1111-111111111111", "rejected"), "Explicit retry after proven rejection");
 Console.WriteLine("PASS concurrent claims, unknown suppression, explicit failure retry");
 var filterType = typeof(VRChatApiService).Assembly.GetType("VRCGroupTools.Services.GroupInviteEligibility");
 Check(filterType != null, "Missing membership-aware selection policy");
 var settings = System.Reflection.DispatchProxy.Create<ISettingsService, SettingsProxy>();
 var vm = new VRCGroupTools.ViewModels.GameLogViewModel(first, settings);
 vm.SeenUsers.Add(new(new() { UserId = "unresolved-membership", DisplayName = "Unknown membership" }, vm));
 vm.SearchQuery = "Unknown";
 vm.SelectAllCommand.Execute(null);
 Check(vm.SelectedCount == 0, "Unknown membership must not be selected as a nonmember");
 vm.SeenUsers.Add(new(new() { UserId = "member", DisplayName = "Member" }, vm) { Membership = GroupMembershipState.CachedMember });
 vm.SeenUsers.Add(new(new() { UserId = "usr_55555555-5555-5555-5555-555555555555", DisplayName = "Nonmember" }, vm) { Membership = GroupMembershipState.CachedNonMember });
 vm.SearchQuery = "";
 vm.HideAlreadyInGroup = true;
 Check(vm.FilteredUsers.Count == 1 && vm.FilteredUsers[0].User.UserId == "usr_55555555-5555-5555-5555-555555555555", "Nonmember filter must exclude members AND unknowns");
 vm.SelectAllCommand.Execute(null);
 Check(vm.SelectedCount == 1, "Select eligible filtered only");
 vm.SearchQuery = "no match";
 Check(vm.SelectedCount == 0, "Filtering must clear hidden selection");
 Console.WriteLine("PASS unknown membership, nonmember filtering, visible eligible selection");
 Check(typeof(GroupInviteLedger).GetMethod("SaveMembershipAsync") != null, "Need persistent complete membership snapshot, not absence in a partial cache");
 await ledger.SaveMembershipAsync(new("grp_11111111-1111-1111-1111-111111111111", new() { "member" }, DateTime.UtcNow, true));
 Check(GroupInviteEligibility.Membership("absent", await new GroupInviteLedger(path).ReadMembershipAsync("grp_11111111-1111-1111-1111-111111111111")) == GroupMembershipState.CachedNonMember, "Complete snapshot persists");
 Check(GroupInviteEligibility.Membership("absent", new("grp_11111111-1111-1111-1111-111111111111", new() { "member" }, DateTime.UtcNow, false)) == GroupMembershipState.Unknown, "Partial absence is unknown");
 Check(typeof(VRCGroupTools.ViewModels.GameLogViewModel).GetMethod("RefreshGroupContextAsync") != null, "Game Log must load selected-group membership and history");
 Check(typeof(VRCGroupTools.ViewModels.GameLogViewModel).GetProperty("ConfirmGroupInvites") != null, "Explicit confirmable group invite snapshot missing");
 Check(typeof(VRChatApiService).Assembly.GetType("VRCGroupTools.Services.ModerationUserSearch") != null, "Ban UUID lookup routing missing");
 string uuid = "ABCDEF01-2345-6789-abcd-0123456789ab", requested = "";
 int lookups = 0, names = 0;
 Task<UserDetails?> Lookup(string id) { lookups++; requested = id; return Task.FromResult<UserDetails?>(new() { UserId = id, DisplayName = "Not in loaded members" }); }
 Task<List<UserSearchResult>> Names(string q) { names++; return Task.FromResult(new List<UserSearchResult>()); }
 await ModerationUserSearch.SearchAsync("usr_" + uuid, Lookup, Names);
 Check(requested == "usr_" + uuid && names == 0, "Prefixed UUID routes direct and preserves casing");
 await ModerationUserSearch.SearchAsync(uuid, Lookup, Names);
 Check(requested == "usr_" + uuid && lookups == 2 && names == 0, "Bare UUID routes direct, independent of member pagination");
 foreach (var invalid in new[] { "usr_bad", " usr_" + uuid, "usr_" + uuid + " ", "wrld_" + uuid, uuid[..35], "ZBCDEF01-2345-6789-abcd-0123456789ab" })
 {
   try { await ModerationUserSearch.SearchAsync(invalid, Lookup, Names); throw new Exception("Invalid ID was accepted"); } catch (ArgumentException) { }
 }
 Check(lookups == 2 && names == 0, "Invalid IDs must not make API calls");
 Check((await ModerationUserSearch.SearchAsync(uuid, _ => Task.FromResult<UserDetails?>(null), Names)).Count == 0, "Not found stays distinct");
 try { await ModerationUserSearch.SearchAsync(uuid, _ => throw new HttpRequestException("offline"), Names); throw new Exception("API error hidden"); } catch (HttpRequestException) { }
 Console.WriteLine("PASS exact/bare UUID routing, invalid IDs, not found versus API error");
 Check(typeof(VRChatApiService).GetMethod("LookupUserByIdAsync") != null, "Strict API lookup must distinguish 404 from API errors");
 Check(typeof(VRChatApiService).Assembly.GetType("VRCGroupTools.Services.GroupBanWorkflow") != null, "Game Log needs group/identity-pinned confirmation before ban dispatch");
 var banTarget = new GroupBanTarget("grp_11111111-1111-1111-1111-111111111111", "usr_" + uuid, "Not a member", "actor");
 GroupBanTarget? current = banTarget;
 int banCalls = 0, confirms = 0;
 Task<bool> Permission(GroupBanTarget t) => Task.FromResult(true);
 Task<VRCGroupTools.Models.ModerationActionRequest?> Confirm(GroupBanTarget t) { confirms++; Check(t == banTarget, "Confirmation identity/group changed"); return Task.FromResult<VRCGroupTools.Models.ModerationActionRequest?>(new() { ActionType = "ban", TargetUserId = t.UserId }); }
 Task<bool> Ban(GroupBanTarget t, VRCGroupTools.Models.ModerationActionRequest r) { banCalls++; return Task.FromResult(true); }
 await GroupBanWorkflow.RunAsync(banTarget, () => current, Permission, Confirm, Ban);
 Check(confirms == 1 && banCalls == 1, "Explicit confirmed non-member ban");
 await GroupBanWorkflow.RunAsync(banTarget, () => current, _ => Task.FromResult(false), Confirm, Ban);
 Check(confirms == 1 && banCalls == 1, "Permission denied must not confirm or dispatch");
 await GroupBanWorkflow.RunAsync(banTarget, () => current, Permission, t => { current = t with { GroupId = "other" }; return Confirm(t); }, Ban);
 Check(banCalls == 1, "Group change during confirmation must abort");
 current = banTarget;
 await GroupBanWorkflow.RunAsync(banTarget, () => current, Permission, _ => Task.FromResult<VRCGroupTools.Models.ModerationActionRequest?>(null), Ban);
 Check(banCalls == 1, "Cancelled confirmation must not dispatch");
 vm.OpenUserActionsCommand.Execute(new VRCGroupTools.ViewModels.GameLogUserViewModel(new() { UserId = banTarget.UserId, DisplayName = banTarget.DisplayName }, vm));
 Check(vm.ActionTarget?.GroupId == "grp_11111111-1111-1111-1111-111111111111" && vm.ActionTarget.UserId == banTarget.UserId && banCalls == 1, "Game Log click opens exact group/user actions without banning");
 Console.WriteLine("PASS ban permission, exact target/group confirmation, cancellation and group-change safety; row click is read-only");
 Check(typeof(VRChatApiService).Assembly.GetType("VRCGroupTools.Services.GroupInvitePresentation") != null, "All entrypoints need distinct suppressed/unknown reporting");
 var root = new DirectoryInfo(AppContext.BaseDirectory);
 while (root != null && !File.Exists(Path.Combine(root.FullName, "src", "VRCGroupTools.csproj"))) root = root.Parent;
 Check(root != null, "Cannot locate source integration fixtures");
 foreach (var source in new[] { "ViewModels/InviteToGroupViewModel.cs", "ViewModels/UserSearchViewModel.cs", "ViewModels/BulkInviterViewModel.cs", "ViewModels/FriendInviterViewModel.cs", "ViewModels/InstanceInviterViewModel.cs", "Services/MemberBackupService.cs" })
 {
   var text = File.ReadAllText(Path.Combine(root!.FullName, "src", source));
   Check(text.Contains("SendTrackedAsync"), source + " must distinguish sent, rejected and suppressed/unknown results");
 }
 Check(!File.ReadAllText(Path.Combine(root!.FullName, "src/ViewModels/InstanceInviterViewModel.cs")).Contains("InviteInstanceUsersInternal(true)"), "No group invites may be dispatched on refresh/load");
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
 typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(first, "self");
 await first.GetGroupMembersAsync("self-group");
 Check(GroupInviteEligibility.Membership("self", await ledger.ReadMembershipAsync("self-group")) == GroupMembershipState.Unknown, "Member endpoint excludes caller: self must not be classified as a nonmember");
 var beforeGuard = handler.Calls;
 var changedAccount = await first.SendTrackedAsync("grp_11111111-1111-1111-1111-111111111111", "account-change", () => { typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(first, "other-account"); return true; });
 Check(changedAccount.State == GroupInviteState.NotSent && handler.Calls == beforeGuard, "Account change at dispatch must not send or masquerade as rejection");
 Check(typeof(VRCGroupTools.ViewModels.MembersListViewModel).GetProperty("LookupUuidCommand") != null, "Members panel must support direct UUID lookup outside loaded/cached rows");
 var logVm = new VRCGroupTools.ViewModels.GameLogViewModel(first, settings, ledger) { InvitePacing = () => Task.CompletedTask };
 var fresh = new VRCGroupTools.ViewModels.GameLogUserViewModel(new() { UserId = "usr_66666666-6666-6666-6666-666666666666", DisplayName = "Fresh target" }, logVm);
 var hidden = new VRCGroupTools.ViewModels.GameLogUserViewModel(new() { UserId = "usr_77777777-7777-7777-7777-777777777777", DisplayName = "Hidden target" }, logVm);
 logVm.SeenUsers.Add(fresh); logVm.SeenUsers.Add(hidden);
 var beforeLoad = handler.Calls;
 await logVm.RefreshGroupContextAsync();
 Check(handler.Calls == beforeLoad, "Local membership/history refresh must make zero API requests");
 logVm.SearchQuery = "Fresh";
 hidden.IsSelected = true; // Simulate stale selection left by another view/filter update.
 logVm.SelectAllCommand.Execute(null);
 Check(logVm.SelectedCount == 1 && !hidden.IsSelected, "Select all must replace stale/hidden selection with eligible visible rows only");
 logVm.ConfirmGroupInvites = (g, ids) => { Check(g == "grp_11111111-1111-1111-1111-111111111111" && ids.SequenceEqual(new[] { "Fresh target (usr_66666666-6666-6666-6666-666666666666)" }), "Confirmation must show exact visible target/group"); return Task.FromResult(false); };
 await logVm.InviteToGroupCommand.ExecuteAsync(null);
 Check(handler.Calls == beforeLoad && logVm.StatusMessage!.Contains("cancelled"), "Declined confirmation must send nothing");
 logVm.ConfirmGroupInvites = (g, ids) => { ((SettingsProxy)(object)settings).Group = "group-changed"; return Task.FromResult(true); };
 await logVm.InviteToGroupCommand.ExecuteAsync(null);
 Check(handler.Calls == beforeLoad, "Changing group during confirmation aborts before dispatch");
 ((SettingsProxy)(object)settings).Group = "grp_11111111-1111-1111-1111-111111111111";
 await logVm.RefreshGroupContextAsync(); logVm.SelectAllCommand.Execute(null);
 logVm.ConfirmGroupInvites = (g, ids) => Task.FromResult(true);
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
 await logVm.InviteToGroupCommand.ExecuteAsync(null);
 Check(handler.Calls == beforeLoad + 1 && await ledger.GetStateAsync("grp_11111111-1111-1111-1111-111111111111", "usr_66666666-6666-6666-6666-666666666666") == GroupInviteState.Sent && await ledger.GetStateAsync("grp_11111111-1111-1111-1111-111111111111", "usr_77777777-7777-7777-7777-777777777777") == GroupInviteState.None, "Only explicitly selected visible eligible recipient may be dispatched");
 Check(!fresh.CanInvite && logVm.InviteHistory.Any(r => r.UserId == "usr_66666666-6666-6666-6666-666666666666"), "History and eligibility update after successful send");
 await logVm.InviteToGroupCommand.ExecuteAsync(null);
 Check(handler.Calls == beforeLoad + 1, "Loading/reclicking does not reinvite");
 Console.WriteLine("PASS local-only refresh, exact selection confirmation, decline/group-change suppression, successful history update");
 // Exercise API response distinctions with the real service and fake transport.
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
 var beforeUnknown = handler.Calls;
 var uncertain = await first.SendTrackedAsync("grp_11111111-1111-1111-1111-111111111111", "server-error");
 Check(uncertain.State == GroupInviteState.Unknown && handler.Calls == beforeUnknown + 1, "5xx must be unknown and never retried");
 await second.SendTrackedAsync("grp_11111111-1111-1111-1111-111111111111", "server-error");
 Check(handler.Calls == beforeUnknown + 1, "Unknown 5xx survives service reload");
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));
 var limited = await first.SendTrackedAsync("grp_11111111-1111-1111-1111-111111111111", "rate-limited");
 Check(limited.State == GroupInviteState.Failed && handler.Calls == beforeUnknown + 2, "429 is a rejection, with no automatic retry");
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
 var conflict = await first.SendTrackedAsync("grp_11111111-1111-1111-1111-111111111111", "conflict");
 Check(conflict.State == GroupInviteState.Unknown, "Undocumented conflict must not be treated as safely retryable rejection");
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
 await first.GetGroupMembersAsync("incomplete-group");
 Check(GroupInviteEligibility.Membership("absent", await ledger.ReadMembershipAsync("incomplete-group")) == GroupMembershipState.Unknown, "Failed bulk fetch must never produce nonmembers");
 var strictId = "usr_" + uuid;
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
 Check(await first.LookupUserByIdAsync(strictId) == null, "404 is not found");
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
 try { await first.LookupUserByIdAsync(strictId); throw new Exception("403 was hidden as not found"); } catch (HttpRequestException) { }
 Console.WriteLine("PASS 5xx/429 no automatic retries, incomplete fetch safety, strict API 404 versus permission error");
 using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
 {
   connection.Open(); using var trigger = connection.CreateCommand();
   trigger.CommandText = "CREATE TRIGGER FailCompletion BEFORE UPDATE ON GroupInviteLedger WHEN NEW.UserId='save-failure' BEGIN SELECT RAISE(FAIL,'offline fixture'); END";
   trigger.ExecuteNonQuery();
 }
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
 var saveFailure = await first.SendTrackedAsync("grp_11111111-1111-1111-1111-111111111111", "save-failure");
 Check(saveFailure.State == GroupInviteState.InFlight && await ledger.GetStateAsync("grp_11111111-1111-1111-1111-111111111111", "save-failure") == GroupInviteState.InFlight, "Accepted invite with failed completion persistence must remain blocked, not reported as rejection");
 var afterSaveFailure = handler.Calls;
 await second.SendTrackedAsync("grp_11111111-1111-1111-1111-111111111111", "save-failure");
 Check(handler.Calls == afterSaveFailure, "Durable claim blocks repeats after completion write failure");
 Console.WriteLine("PASS completion storage failure keeps durable claim and accurate uncertainty");
 typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(first, true);
 handler.Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"id\":\"usr_first\",\"displayName\":\"Ambiguous\"},{\"id\":\"usr_second\",\"displayName\":\"Ambiguous\"}]") });
 var ambiguousVm = new VRCGroupTools.ViewModels.GameLogViewModel(first, settings, ledger);
 var ambiguousRow = new VRCGroupTools.ViewModels.GameLogUserViewModel(new() { DisplayName = "Ambiguous" }, ambiguousVm);
 ambiguousVm.SeenUsers.Add(ambiguousRow);
 await (Task)typeof(VRCGroupTools.ViewModels.GameLogViewModel).GetMethod("FetchUserDetailsAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(ambiguousVm, null)!;
 Check(!ambiguousRow.HasUserId, "Ambiguous display-name resolution must not choose a moderation/invitation target");
 Console.WriteLine("PASS ambiguous Game Log names remain unresolved rather than choosing a ban target");
 var callsBeforeView = handler.Calls;
 await GroupUiTests.RunAsync(first, settings, ledger);
 Check(handler.Calls == callsBeforeView, "Rendering Game Log must not dispatch HTTP");
 Console.WriteLine("PASS compiled Game Log action/filter/history bindings and no network writes on load");
}
finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); File.Delete(path); }
static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
static async Task Suppressed(Func<Task<bool>> action) { try { await action(); } catch (GroupInviteSuppressedException) { return; } throw new Exception("Expected explicit suppression"); }
public class SettingsProxy : System.Reflection.DispatchProxy
{
 public string Group = "grp_11111111-1111-1111-1111-111111111111";
 protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args) => method!.Name switch { "get_CurrentGroupId" => Group, "get_Settings" => new VRCGroupTools.Services.AppSettings { GroupId = Group }, _ => null };
}
sealed class FakeHandler : HttpMessageHandler
{
 public int Calls;
 public Func<Task<HttpResponseMessage>> Respond = () => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
 protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { Calls++; return Respond(); }
}
