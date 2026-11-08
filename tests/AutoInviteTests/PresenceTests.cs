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
        foreach(var body in new[]{"{}", Json(User,"offline"),Json(User,"private"),Json(User,"traveling"),Json(User,Location+"4"),Json(Account,Location),Json(User,Location,"offline"),Json(User,Location,"active"),Json(User,Location,"online","other"),"{\"location\":\""+Location+"\",\"state\":\"online\"}"})
        {
            var captured=body;
            yield return ("Compiled automatic eligibility rejects unknown/hidden/mismatched presence: "+body, async()=>
            {
                using var f=new Harness(captured); f.Handler.AllowWrites=false; await f.Discover();
                Check(!await f.Eligible(),"Uncorroborated user eligible");
                Check(!f.Guard(),"Uncorroborated final guard passed");
                await f.AttemptGuarded();
                Check(f.Handler.Writes==0,"Uncorroborated write reached HTTP");
                Check(f.Handler.Reads==1,"Missing paced presence read");
                for(var i=0;i<3;i++){await f.Discover();Check(!await f.Eligible(),"Cached unknown became eligible");}
                Check(f.Handler.Reads==1,"Unchanged polling retried unknown presence");
            });
        }
        yield return ("Compiled corroborated occupancy is eligible, fresh dispatch succeeds, unchanged polls remain local",async()=>
        {
            using var f=new Harness(Json(User,Location));await f.Discover();
            Check(await f.Eligible()&&f.Guard(),"Corroborated current occupant rejected");
            await f.AttemptGuarded();Check(f.Handler.Writes==1&&f.Handler.Reads==1,"Expected one read and one central write");
            await f.Discover();Check(!await f.Eligible(),"Ledger no longer excludes sent user");Check(f.Handler.Reads==1,"Sent candidate rechecked");
        });
        yield return ("New-candidate API reads paced independently; unchanged/expired evidence never refetched",async()=>
        {
            using var f=new Harness("{}");await f.Discover();Check(!await f.Eligible(),"Missing-location precondition");
            const string secondId="usr_88888888-8888-8888-8888-888888888888";
            File.AppendAllText(f.Temp.Log,$"[Behaviour] OnPlayerJoined Second ({secondId})\n");
            await f.Discover();var second=f.Instance.GetLocalUsers().Single(u=>u.UserId==secondId);
            Check(!await (Task<bool>)f.Invoke("IsAutoEligibleAsync",second)!&&f.Handler.Reads==1,"New candidate bypassed presence pacing");
            await Task.Delay(AutoGroupInviteSession.InviteInterval); // production dispatch clock is monotonic, not the fixture's admission clock
            f.Now=f.Now.AddSeconds(13);
            Check(!await (Task<bool>)f.Invoke("IsAutoEligibleAsync",second)!&&f.Handler.Reads==2,"Next paced candidate not read");
            f.Now=f.Now.AddMinutes(1);await f.Discover();Check(!await f.Eligible(),"Unknown/expired evidence became eligible");
            Check(!await (Task<bool>)f.Invoke("IsAutoEligibleAsync",second)!&&f.Handler.Reads==2,"Unchanged candidates refetched");
        });
        foreach(var status in new[]{HttpStatusCode.Forbidden,HttpStatusCode.NotFound,HttpStatusCode.TooManyRequests})
        {
            var captured=status;
            yield return ("Read-only presence API error fails closed without retry: "+status,async()=>
            {
                using var f=new Harness(Json(User,Location));f.Handler.ReadStatus=captured;f.Handler.AllowWrites=false;await f.Discover();
                Check(!await f.Eligible()&&!f.Guard(),"API failure eligible");await f.AttemptGuarded();
                await f.Discover();Check(!await f.Eligible()&&f.Handler.Reads==1&&f.Handler.Writes==0,"API error retried/wrote");
            });
        }
        foreach(var change in new[]{"expired","departed","account","login","instance","offline","off"})
        {
            var captured=change;
            yield return ("Final HTTP guard revalidates corroboration after real API pacing: "+change,async()=>
            {
                using var f=new Harness(Json(User,Location)); f.Handler.AllowWrites=false; await f.Discover();Check(await f.Eligible(),"Positive precondition missing");
                typeof(VRChatApiService).GetField("_lastRequestTime",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(f.Api,DateTime.Now);
                var attempt=f.AttemptGuarded();Check(!attempt.IsCompleted,"Did not exercise pacing");
                if(captured=="expired") f.Now=f.Now.AddMinutes(1);
                if(captured=="departed")File.AppendAllText(f.Temp.Log,$"[Behaviour] OnPlayerLeft Exact ({User})\n");
                if(captured=="account")typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(f.Api,User);
                if(captured=="login")typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(f.Api,true);
                if(captured=="instance")File.AppendAllText(f.Temp.Log,$"[Behaviour] Joining {Location}4\n[Behaviour] OnJoinedRoom\n");
                if(captured=="offline") f.Running=false;
                if(captured=="off")typeof(InstanceInviterViewModel).GetField("_autoInviteEnabled",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(f.Vm,false);
                await attempt;Check(f.Handler.Writes==0,"Changed/expired presence reached write HTTP");
            });
        }
    }
    public static string Json(string id,string location,string state="online",string traveling="")=>System.Text.Json.JsonSerializer.Serialize(new{id,location,state,travelingToLocation=traveling});
    public static void Check(bool ok,string reason){if(!ok)throw new Exception(reason);}
    public sealed class Harness:IDisposable
    {
        public Temp Temp=new();public PresenceHandler Handler;public VRChatApiService Api;public InstanceInviterViewModel Vm;
        public InstanceInviterService Instance;public bool Running=true;public DateTimeOffset Now=DateTimeOffset.UtcNow;
        InstanceUser? user;
        public Harness(string body,string envelope="[Behaviour] OnPlayerJoined")
        {
            File.WriteAllText(Temp.Log,$"[Behaviour] Joining {Location}\n[Behaviour] OnJoinedRoom\n{envelope} Exact ({User})\n");
            var start=DateTime.UtcNow.AddMinutes(-1);Instance=new(Temp.Dir,()=>Running?start:null,()=>DateTime.UtcNow);
            var ledger=new GroupInviteLedger(Temp.Db);ledger.SaveMembershipAsync(new(Group,new(),DateTime.UtcNow,true)).GetAwaiter().GetResult();
            Handler=new(body);Api=new(new HttpClient(Handler){BaseAddress=new Uri("https://offline.invalid/")},ledger);
            typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(Api,true);typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(Api,Account);
            var settings=new AppSettings{GroupId=Group,InstanceInviterOnly18Plus=false};
            Vm=new(Api,Proxy.Make<ISettingsService>((m,_)=>m.Name switch{"get_Settings"=>settings,"get_CurrentGroupId"=>Group,_=>null}),Instance,Proxy.Make<ICacheService>((_,_)=>null),ledger);
            typeof(InstanceInviterViewModel).GetField("_autoInviteEnabled",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(Vm,true);
            typeof(InstanceInviterViewModel).GetField("_presenceUtcNow",BindingFlags.NonPublic|BindingFlags.Instance)?.SetValue(Vm,(Func<DateTimeOffset>)(()=>Now));
        }
        public object? Invoke(string name,params object[] args)=>typeof(InstanceInviterViewModel).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Vm,args);
        public async Task Discover(){var users=await (Task<List<InstanceUser>>)Invoke("DiscoverAutoUsersAsync")!;user=users.FirstOrDefault(u=>u.UserId==User);}
        public Task<bool> Eligible()=>user==null?Task.FromResult(false):(Task<bool>)Invoke("IsAutoEligibleAsync",user)!;
        public bool Guard()=>user!=null&&(bool)Invoke("StillAutoEligible",user)!;
        public async Task AttemptGuarded(){try{await Api.SendAutomaticGroupInviteAsync(Group,User,Guard,CancellationToken.None);}catch(GroupInviteSuppressedException){}}
        public void Dispose(){Vm.Dispose();Handler.Dispose();Temp.Dispose();}
    }
    public sealed class PresenceHandler(string body):HttpMessageHandler
    {
        public int Reads,Writes;public bool AllowWrites=true;public HttpStatusCode ReadStatus=HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c)
        {
            if(r.Method==HttpMethod.Get){Reads++;return Task.FromResult(new HttpResponseMessage(ReadStatus){Content=new StringContent(body)});}
            Writes++;if(!AllowWrites)throw new InvalidOperationException("Write transport denied");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{}")});
        }
    }
}
