using VRCGroupTools.Services;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;

var tests = new List<(string, Func<Task>)>();
void Add(string name, Func<Task> run) => tests.Add((name,run));
void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
Add("Opt-in polls at exact five-second cadence, never on start", async () => {
    var f = new Fixture(); var running = f.Session.RunAsync();
    Check(f.Polls == 0 && f.Waits.Single() == TimeSpan.FromSeconds(5), "Must not run on startup or poll faster than 5s");
    await f.Advance(); Check(f.Polls == 1 && f.Waits.Count == 2 && f.Waits[1] == TimeSpan.FromSeconds(5), "Cadence drifted");
    f.Session.Stop(); await running;
});
Add("Pending request does not overlap and local polling continues", async () => {
    var f = new Fixture(); var gate = new TaskCompletionSource<GroupInviteAttempt>(); f.Send = (_,guard,_) => { Check(guard(), "Invalid guard"); return gate.Task; };
    await f.Session.TickAsync(); await f.Session.TickAsync();
    Check(f.Sends == 1 && f.Polls == 2, "Overlapping sends or local detection blocked by HTTP");
    gate.SetResult(new(true,GroupInviteState.Sent,"Sent")); await f.Session.PendingSend;
});
Add("Minimum 13-second pacing independent of 5-second polling", async () => {
    var f = new Fixture(); await f.Session.TickAsync();
    f.Now += TimeSpan.FromSeconds(5); await f.Session.TickAsync();
    f.Now += TimeSpan.FromSeconds(5); await f.Session.TickAsync(); Check(f.Sends == 1,"5s detection bypassed pacing");
    f.Now += TimeSpan.FromSeconds(5); await f.Session.TickAsync(); Check(f.Sends == 2,"Paced second send missing");
});
foreach (var field in new[]{"group","account","login","client","world","instance","logout"}) Add("Stops on context change: " + field, async () => {
    var f = new Fixture(); f.Context = field switch {
        "group" => f.Context! with {GroupId="other"}, "account" => f.Context! with {AccountId="other"},
        "login" => f.Context! with {LoginSession=2}, "client" => f.Context! with {ClientSession="other"},
        "world" => f.Context! with {WorldId="other"}, "instance" => f.Context! with {InstanceId="other"}, _ => null};
    await f.Session.TickAsync(); Check(!f.Session.IsRunning && f.Sends==0,"Changed context sent request");
});
Add("Off during pacing invalidates HTTP guard",async()=>{
    var f=new Fixture(); Func<bool>? guard=null; var gate=new TaskCompletionSource<GroupInviteAttempt>();
    f.Send=(_,g,_)=>{guard=g; return gate.Task;}; await f.Session.TickAsync(); f.Session.Stop();
    Check(!guard!(),"Off did not revoke dispatch guard"); gate.SetResult(new(false,GroupInviteState.NotSent,"Not sent")); await f.Session.PendingSend;
});
Add("Change during eligibility never sends",async()=>{var f=new Fixture(); f.Eligible=_=>{f.Context=null;return Task.FromResult(true);};await f.Session.TickAsync();Check(f.Sends==0,"Context changed during async enrichment");});
Add("Filtered and departed users do not send",async()=>{var f=new Fixture();f.StillEligible=_=>false;await f.Session.TickAsync();Check(f.Sends==0,"Filtered/departed user sent");});
Add("Rejected request stops instead of automatic retry",async()=>{var f=new Fixture();f.Send=(_,_,_)=>Task.FromResult(new GroupInviteAttempt(false,GroupInviteState.Failed,"Rejected"));await f.Session.TickAsync();f.Now+=TimeSpan.FromMinutes(1);await f.Session.TickAsync();Check(f.Sends==1&&!f.Session.IsRunning,"Rejected request looped");});
Add("Unknown request stops instead of automatic retry",async()=>{var f=new Fixture();f.Send=(_,_,_)=>Task.FromResult(new GroupInviteAttempt(false,GroupInviteState.Unknown,"Unknown"));await f.Session.TickAsync();f.Now+=TimeSpan.FromMinutes(1);await f.Session.TickAsync();Check(f.Sends==1&&!f.Session.IsRunning,"Unknown request looped");});
Add("Concurrent local refreshes cannot overlap",async()=>{var f=new Fixture();var gate=new TaskCompletionSource<List<InstanceUser>>();f.Discover=()=>gate.Task;var first=f.Session.TickAsync();await f.Session.TickAsync();Check(f.Polls==1,"Poll overlap");gate.SetResult(new());await first;});
Add("Automatic central boundary never retries Failed, unlike deliberate manual retry",async()=>{
    using var temp=new Temp(); var ledger=new GroupInviteLedger(temp.Db);await ledger.TryClaimAsync("g","u");await ledger.CompleteAsync("g","u",GroupInviteState.Failed);
    var method=typeof(GroupInviteLedger).GetMethod("TryClaimAutomaticAsync");Check(method!=null,"Automatic claim must atomically refuse Failed too");
    Check(!await (Task<bool>)method!.Invoke(ledger,new object[]{"g","u"})!,"Automatic retry claimed Failed");
    Check(await ledger.TryClaimAsync("g","u"),"Deliberate manual retries must remain available");
});
Add("Local discovery never calls API or searches ambiguous names; unchanged cycles are local",async()=>{
    using var temp=new Temp(); var world="wrld_11111111-1111-1111-1111-111111111111";
    File.WriteAllText(temp.Log,$"[Behaviour] Joining {world}:123\n[Behaviour] OnJoinedRoom\n[Behaviour] OnPlayerJoined Name Only\n[Behaviour] OnPlayerJoined Exact (usr_22222222-2222-2222-2222-222222222222)\n");
    var start=DateTime.UtcNow.AddMinutes(-1); var service=new InstanceInviterService(temp.Dir,()=>start,()=>DateTime.UtcNow);
    var handler=new Handler();var api=new VRChatApiService(new HttpClient(handler){BaseAddress=new Uri("https://offline.invalid/")});
    typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(api,true);
    var first=await service.GetCurrentInstanceUsersAsync(api);var second=await service.GetCurrentInstanceUsersAsync(api);
    Check(handler.Calls==0,"Local cycle spent API requests");Check(first.Count==2&&second.Count==2,"Fixture users missing");
    Check(first.Single(u=>u.DisplayName=="Name Only").UserId=="","Unresolved name guessed");
    File.AppendAllText(temp.Log,"[Behaviour] OnPlayerLeft Exact (usr_22222222-2222-2222-2222-222222222222)\n");
    Check((await service.GetCurrentInstanceUsersAsync(api)).Count==1,"Departed user remained eligible");
});
Add("WPF opt-in property is wired to real session lifecycle",()=>{
    var vm=typeof(VRCGroupTools.ViewModels.InstanceInviterViewModel);
    Check(vm.GetMethod("OnAutoInviteEnabledChanged",BindingFlags.NonPublic|BindingFlags.Instance)!=null,"Checkbox property is dead");
    Check(vm.GetMethod("StopAutoInvite")!=null,"View unload cannot stop auto session");return Task.CompletedTask;
});
Add("Central automatic API uses automatic ledger claim",()=>{
    Check(typeof(VRChatApiService).GetMethod("SendAutomaticGroupInviteAsync")!=null,"Automatic HTTP boundary missing");return Task.CompletedTask;
});
Add("Same-location rejoin changes session identity and incremental reads keep completed joins",()=>{
    using var temp=new Temp(); var start=DateTime.UtcNow.AddMinutes(-1); var location="wrld_11111111-1111-1111-1111-111111111111:123";
    File.WriteAllText(temp.Log,$"[Behaviour] Joining {location}\n[Behaviour] OnJoinedRoom\n");
    var service=new InstanceInviterService(temp.Dir,()=>start,()=>DateTime.UtcNow);var first=service.GetCurrentInstance();
    File.AppendAllText(temp.Log,$"[Behaviour] OnLeftRoom\n[Behaviour] Joining {location}\n[Behaviour] OnJoinedRoom\n");
    var next=service.GetCurrentInstance();Check(first!=null&&next!=null&&first.SessionKey!=next.SessionKey,"Same destination rejoin silently retained old consent");return Task.CompletedTask;
});
Add("Compiled WPF checkbox/DI, exact confirmation, filters, unload and shared persistent history",UiTests.Integrated);
Add("Slow detail lookup never blocks local five-second detection",async()=>{
    var f=new Fixture();var gate=new TaskCompletionSource<bool>();f.Eligible=_=>gate.Task;
    var first=f.Session.TickAsync();var completed=first.IsCompleted;
    if(!completed){gate.SetResult(false);await first;throw new Exception("Detail lookup blocked local poll loop");}
    await f.Session.TickAsync();Check(f.Polls==2&&f.Sends==0,"Local detection stalled or overlapping send");gate.SetResult(false);await f.Session.PendingSend;
});
tests.AddRange(ApiTests.All());
tests.AddRange(UntrustedLogTests.All());
tests.AddRange(PresenceTests.All());
tests.AddRange(PresenceDispatchTests.All());
tests.AddRange(HistoryTests.All());
tests.AddRange(ReviewRegressionTests.All());
Add("Historical group context needs account and group, not a current instance",()=>{
    using var temp=new Temp();
    var api=Proxy.Make<IVRChatApiService>((m,_)=>m.Name switch {"get_IsLoggedIn"=>true,"get_CurrentUserId"=>"usr_33333333-3333-3333-3333-333333333333","get_InvitationSession"=>1L,_=>null});
    var settings=new AppSettings {GroupId="grp_11111111-1111-1111-1111-111111111111"};
    var vm=new VRCGroupTools.ViewModels.InstanceInviterViewModel(api,Proxy.Make<ISettingsService>((m,_)=>m.Name switch {"get_Settings"=>settings,"get_CurrentGroupId"=>settings.GroupId,_=>null}),Proxy.Make<IInstanceInviterService>((_,_)=>null),Proxy.Make<ICacheService>((_,_)=>null),new GroupInviteLedger(temp.Db));
    var context=typeof(VRCGroupTools.ViewModels.InstanceInviterViewModel).GetMethod("ReadAutoContext",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(vm,null);
    Check(context!=null,"Historical GROUP invitations wrongly require an active instance"); vm.Dispose();return Task.CompletedTask;
});
Add("Encounter history persists independently of instance/client and defaults to no consent",async()=>{
    using var temp=new Temp();var ledger=new GroupInviteLedger(temp.Db);
    var save=typeof(GroupInviteLedger).GetMethod("SaveEncountersAsync"); var read=typeof(GroupInviteLedger).GetMethod("ReadEncountersAsync");
    Check(save!=null&&read!=null,"Retained encounter queue missing");
    var users=new List<VRCGroupTools.ViewModels.SeenUser>{new(){UserId=PresenceTests.User,DisplayName="Past user",LastSeenAt=DateTime.Now.AddDays(-1)}};
    await (Task)save!.Invoke(ledger,new object[]{users})!;
    var reloaded=await (Task<List<VRCGroupTools.ViewModels.SeenUser>>)read!.Invoke(new GroupInviteLedger(temp.Db),null)!;
    Check(reloaded.Single().UserId==PresenceTests.User,"Restart discarded historical identity");
});
Add("Historical group eligibility ignores API private location",async()=>{
    using var f=new PresenceTests.Harness(PresenceTests.Json(PresenceTests.User,"private"));await f.Discover();
    Check(await f.Eligible(),"Valid consented nonmember wrongly blocked by private location");
});
var filter=Environment.GetEnvironmentVariable("VRCGT_TEST_FILTER");
var executed=tests.Where(t=>filter==null || t.Item1.Contains(filter,StringComparison.OrdinalIgnoreCase)).ToList();
var failures=0;
foreach(var (name,run) in executed)try{await run();Console.WriteLine("PASS "+name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+name+": "+e.Message);}
Console.WriteLine($"{executed.Count-failures}/{executed.Count} passed");return failures==0?0:1;

sealed class Fixture {
    public AutoGroupInviteContext? Context=new("g","a",1,"client","world","instance");
    public DateTimeOffset Now=DateTimeOffset.UtcNow; public int Polls,Sends; public List<TimeSpan> Waits=new();
    public Func<Task<List<InstanceUser>>> Discover=()=>Task.FromResult(new List<InstanceUser>{new(){UserId="u",DisplayName="User"}});
    public Func<InstanceUser,Task<bool>> Eligible=_=>Task.FromResult(true); public Func<InstanceUser,bool> StillEligible=_=>true;
    public Func<string,Func<bool>,CancellationToken,Task<GroupInviteAttempt>> Send=(_,_,_)=>Task.FromResult(new GroupInviteAttempt(true,GroupInviteState.Sent,"Sent"));
    TaskCompletionSource<bool>? pending; public AutoGroupInviteSession Session;
    public Fixture(){Session=new(Context!,()=>Context,()=>{Polls++;return Discover();},u=>Eligible(u),u=>StillEligible(u),(u,g,c)=>{Sends++;return Send(u,g,c);},_=>{},()=>Now,(d,c)=>{Waits.Add(d);pending=new();c.Register(()=>pending.TrySetCanceled(c));return pending.Task;});}
    public async Task Advance(){var old=pending!;Now+=TimeSpan.FromSeconds(5);old.SetResult(true);for(int i=0;i<100&&ReferenceEquals(old,pending);i++)await Task.Delay(1);}
}
sealed class Handler:HttpMessageHandler {public int Calls;public string Body="{}";protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Calls++;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Body)});}}
sealed class Temp:IDisposable {public string Dir=Path.Combine(Environment.GetEnvironmentVariable("TMPDIR")??Path.GetTempPath(),"vrcgt-auto-"+Guid.NewGuid());public string Db=>Path.Combine(Dir,"test.db");public string Log=>Path.Combine(Dir,"output_log_fixture.txt");public Temp(){Directory.CreateDirectory(Dir);} public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(Dir,true);}}

