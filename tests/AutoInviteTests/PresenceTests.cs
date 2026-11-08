using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;

static class PresenceTests
{
    const string Group="grp_11111111-1111-1111-1111-111111111111", Account="usr_33333333-3333-3333-3333-333333333333";
    public const string User="usr_99999999-9999-9999-9999-999999999999", Location="wrld_11111111-1111-1111-1111-111111111111:123";
    public static IEnumerable<(string,Func<Task>)> All()
    {
        foreach(var location in new[]{"offline","private","traveling",Location+"4",""})
        {
            var captured=location;
            yield return ("Reviewed historical group user eligible irrespective of API location: "+location,async()=>
            {
                using var f=new Harness(Json(User,captured));await f.Discover();
                Check(await f.Eligible()&&f.Guard(),"Consented historical user blocked by location");
                await f.AttemptGuarded();Check(f.Handler.Writes==1&&f.Handler.Reads==1,"Group invite used presence API or did not dispatch");
                await f.Discover();Check(!await f.Eligible(),"Shared ledger forgot sent history");
            });
        }
        foreach(var change in new[]{"room-ended","room-change","client-closed","expired","account","login","off","logout"})
        {
            var captured=change;
            yield return ("Historical group final dispatch after pacing: "+change,async()=>
            {
                using var f=new Harness(Json(User,"private"));await f.Discover();Check(await f.Eligible(),"Positive precondition missing");
                typeof(VRChatApiService).GetField("_lastRequestTime",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(f.Api,DateTime.Now);
                var blocked=captured is "account" or "login" or "off" or "logout";f.Handler.AllowWrites=!blocked;
                var attempt=f.AttemptGuarded();Check(!attempt.IsCompleted,"Did not exercise real pacing");
                if(captured=="expired")f.Now=f.Now.AddDays(1);
                if(captured=="room-ended")File.AppendAllText(f.Temp.Log,"[Behaviour] OnLeftRoom\n");
                if(captured=="room-change")File.AppendAllText(f.Temp.Log,$"[Behaviour] Joining {Location}4\n[Behaviour] OnJoinedRoom\n");
                if(captured=="client-closed")f.Running=false;
                if(captured=="account")typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(f.Api,User);
                if(captured=="login")typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(f.Api,true);
                if(captured=="logout")typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(f.Api,false);
                if(captured=="off")f.Vm.StopAutoInvite();
                await attempt;Check(f.Handler.Writes==(blocked?0:1),"Historical consent/destination distinction failed");
            });
        }
        yield return ("Previously encountered user remains queued after room closes and VRChat stops",async()=>
        {
            using var f=new Harness(Json(User,"private"));await f.Discover();File.AppendAllText(f.Temp.Log,"[Behaviour] OnLeftRoom\n");f.Running=false;
            await f.Discover();Check(await f.Eligible()&&f.Guard(),"Retained historical identity disappeared with room/client");
            await f.AttemptGuarded();Check(f.Handler.Writes==1&&f.Handler.Reads==1,"Historical group invite did not dispatch");
        });
    }
    public static string Json(string id,string location,string state="online",string traveling="")=>System.Text.Json.JsonSerializer.Serialize(new{id,location,state,travelingToLocation=traveling});
    public static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    public sealed class Harness:IDisposable
    {
        public Temp Temp=new();public PresenceHandler Handler;public VRChatApiService Api;public InstanceInviterViewModel Vm;
        public InstanceInviterService Instance;public bool Running=true;public DateTimeOffset Now=DateTimeOffset.UtcNow;
        InstanceUser? user; public AppSettings Settings=null!;
        public Harness(string body,string envelope="[Behaviour] OnPlayerJoined")
        {
            File.WriteAllText(Temp.Log,$"[Behaviour] Joining {Location}\n[Behaviour] OnJoinedRoom\n{envelope} Exact ({User})\n");
            var start=DateTime.UtcNow.AddMinutes(-1);Instance=new(Temp.Dir,()=>Running?start:null,()=>DateTime.UtcNow);
            var ledger=new GroupInviteLedger(Temp.Db);ledger.SaveMembershipAsync(new(Group,new(),DateTime.UtcNow,true)).GetAwaiter().GetResult();
            Handler=new(body);Api=new(new HttpClient(Handler){BaseAddress=new Uri("https://offline.invalid/")},ledger);
            typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(Api,true);typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(Api,Account);
            var settings=Settings=new AppSettings{GroupId=Group,InstanceInviterOnly18Plus=false};
            Vm=new(Api,Proxy.Make<ISettingsService>((m,_)=>m.Name switch{"get_Settings"=>settings,"get_CurrentGroupId"=>settings.GroupId,_=>null}),Instance,Proxy.Make<ICacheService>((_,_)=>null),ledger);
            typeof(InstanceInviterViewModel).GetField("_autoInviteEnabled",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(Vm,true);
            typeof(InstanceInviterViewModel).GetField("_presenceUtcNow",BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(Vm,(Func<DateTimeOffset>)(()=>Now));
        }
        public object? Invoke(string name,params object[] args)=>typeof(InstanceInviterViewModel).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Vm,args);
        public async Task Discover(){var users=await (Task<List<InstanceUser>>)Invoke("DiscoverAutoUsersAsync")!;user=users.FirstOrDefault(u=>u.UserId==User); ((HashSet<string>)typeof(InstanceInviterViewModel).GetField("_approvedHistory",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(Vm)!).Add(User); }
        public Task<bool> Eligible()=>user==null?Task.FromResult(false):(Task<bool>)Invoke("IsAutoEligibleAsync",user)!;
        public bool Guard()=>user!=null&&(bool)Invoke("StillAutoEligible",user)!;
        public async Task AttemptGuarded(){try{await Api.SendAutomaticGroupInviteAsync(Group,User,Guard,CancellationToken.None);}catch(GroupInviteSuppressedException){}}
        public void Dispose(){Vm.Dispose();Handler.Dispose();Temp.Dispose();}
    }
    public sealed class PresenceHandler(string body):HttpMessageHandler
    {
        public int Reads,Writes;public string? IdentityBody;public bool ThrowRead;public bool AllowWrites=true;public HttpStatusCode ReadStatus=HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)
        {
            if(r.Method==HttpMethod.Get){Reads++;if(ThrowRead)throw new HttpRequestException("Offline fixture");return Task.FromResult(new HttpResponseMessage(ReadStatus){Content=new StringContent(r.RequestUri!.AbsolutePath.Contains("/groups/")?body:IdentityBody??body)});}
            Writes++;if(!AllowWrites)throw new InvalidOperationException("Write transport denied");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")});
        }
    }
}
