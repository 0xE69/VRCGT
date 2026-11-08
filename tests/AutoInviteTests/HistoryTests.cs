using System.Reflection;
using System.Net;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;

static class HistoryTests
{
    const string Group="grp_11111111-1111-1111-1111-111111111111";
    public static IEnumerable<(string,Func<Task>)> All()
    {
        yield return ("Strict identity rejects id-less API object with filters disabled through central guard",async()=>
        {
            using var f=new PresenceTests.Harness("{}");await f.Discover();f.Handler.AllowWrites=false;
            var eligible=await f.Eligible();
            await f.AttemptGuarded();
            PresenceTests.Check(!eligible&&!f.Guard()&&f.Handler.Writes==0,$"Id-less response authorised: eligible={eligible}, reads={f.Handler.Reads}, writes={f.Handler.Writes}");
            await f.Discover();await f.Eligible();
            PresenceTests.Check(f.Handler.Reads==1,"Unknown identity must be cached, not retried on unchanged polls");
        });
        yield return ("Future LOCAL history discovers new joined IDs without completed join and retains departed clients",async()=>
        {
            using var f=new PresenceTests.Harness(PresenceTests.Json(PresenceTests.User,"offline"));
            System.IO.File.WriteAllText(f.Temp.Log,"[Behaviour] no completed join markers\n");
            PresenceTests.Check(f.Instance.GetCurrentInstance()==null&&f.Instance.DetectionDiagnostic.Code==InstanceDetectionCode.JoinNotObserved,"Expected JoinNotObserved");
            f.Vm.ConfirmAutoInvite=(_,scope)=>scope.Contains("Include future exact-ID");
            var running=f.Vm.StartAutoInviteAsync();
            var session=(AutoGroupInviteSession)typeof(InstanceInviterViewModel).GetField("_autoSession",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(f.Vm)!;
            PresenceTests.Check((await new GroupInviteLedger(f.Temp.Db).ReadEncountersAsync()).Count==0&&f.Handler.Writes==0,"Empty initial scope wrote");
            System.IO.File.AppendAllText(f.Temp.Log,$"[Behaviour] OnPlayerJoined Exact ({PresenceTests.User})\n[Behaviour] OnPlayerLeft Exact ({PresenceTests.User})\n[Behaviour] OnDisconnected\n");f.Running=false;
            await session.TickAsync();await session.PendingSend;
            var retained=await new GroupInviteLedger(f.Temp.Db).ReadEncountersAsync();
            PresenceTests.Check(retained.Any(u=>u.UserId==PresenceTests.User)&&f.Handler.Reads==1&&f.Handler.Writes==1,$"Missing future history: retained={retained.Count}, reads={f.Handler.Reads}, writes={f.Handler.Writes}");
            f.Vm.StopAutoInvite();await running;
        });
        foreach(var body in new[]{"null","{}","{\"userId\":\""+PresenceTests.User+"\",\"membershipStatus\":\"banned\"}"})
        {
            var captured=body;
            yield return ("Bounded exact membership distinguishes proven nonmember/unknown/blocked: "+body,async()=>
            {
                using var f=new PresenceTests.Harness(captured);f.Handler.IdentityBody=PresenceTests.Json(PresenceTests.User,"offline");
                await new GroupInviteLedger(f.Temp.Db).SaveMembershipAsync(new(Group,new(),DateTime.UtcNow,false));
                await f.Discover();var ok=await f.Eligible();
                PresenceTests.Check(ok==(captured=="null"),"Membership uncertainty assumed nonmember or blocked user admitted");
                await f.Discover();await f.Eligible();PresenceTests.Check(f.Handler.Reads==(ok?2:1),"Unchanged five-second polls retried exact membership/identity");
                if(ok){await f.AttemptGuarded();PresenceTests.Check(f.Handler.Writes==1,"Proven nonmember did not send");}
                else {f.Handler.AllowWrites=false;await f.AttemptGuarded();PresenceTests.Check(f.Handler.Writes==0,"Unknown/blocked membership wrote");}
            });
        }
        foreach(var status in new[]{HttpStatusCode.NotFound,HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests,HttpStatusCode.ServiceUnavailable})
        {
            var captured=status;
            yield return ("Membership error stays pending, never assumed nonmember or automatically retried: "+status,async()=>
            {
                using var f=new PresenceTests.Harness("null");f.Handler.ReadStatus=captured;f.Handler.AllowWrites=false;
                await new GroupInviteLedger(f.Temp.Db).SaveMembershipAsync(new(Group,new(),DateTime.UtcNow,false));await f.Discover();
                PresenceTests.Check(!await f.Eligible(),"API error eligible");await f.Discover();PresenceTests.Check(!await f.Eligible()&&f.Handler.Reads==1,"API error retried");await f.AttemptGuarded();PresenceTests.Check(f.Handler.Writes==0,"API error wrote");
            });
        }
        yield return ("No authorised history scope never writes, even valid exact historical identity",async()=>
        {
            using var f=new PresenceTests.Harness("null");await f.Discover();
            ((HashSet<string>)typeof(InstanceInviterViewModel).GetField("_approvedHistory",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(f.Vm)!).Clear();
            f.Handler.AllowWrites=false;PresenceTests.Check(!await f.Eligible()&&!f.Guard(),"Unapproved history eligible");await f.AttemptGuarded();PresenceTests.Check(f.Handler.Reads==0&&f.Handler.Writes==0,"Unapproved queue reached HTTP");
        });
        yield return ("Game History manual invite after room/client closed pins group/account/auth before confirmation",async()=>
        {
            using var f=new PresenceTests.Harness("{}");await f.Discover();f.Running=false;
            var ledger=new GroupInviteLedger(f.Temp.Db);
            var settings=Proxy.Make<ISettingsService>((m,_)=>m.Name switch {"get_CurrentGroupId"=>Group,"get_Settings"=>new AppSettings{GroupId=Group},_=>null});
            var vm=new GameLogViewModel(f.Api,settings,ledger){InvitePacing=()=>Task.CompletedTask};
            await vm.RefreshGroupCommand.ExecuteAsync(null);vm.SelectAllCommand.Execute(null);
            PresenceTests.Check(vm.SelectedCount==1,"Historical visible nonmember not selectable");
            vm.ConfirmGroupInvites=(g,ids)=>{PresenceTests.Check(g==Group&&ids.Count==1&&ids[0].Contains(PresenceTests.User),"Confirmation lacked exact target/group");return Task.FromResult(true);};
            await vm.InviteToGroupCommand.ExecuteAsync(null);PresenceTests.Check(f.Handler.Writes==1,"Manual history after room closure did not send");
            await vm.InviteToGroupCommand.ExecuteAsync(null);PresenceTests.Check(f.Handler.Writes==1,"Shared ledger did not block manual duplicate");
        });
        foreach(var change in new[]{"account","reauth","logout"})
        {
            var captured=change;
            yield return ("Manual history confirmation revoked by "+change,async()=>
            {
                using var f=new PresenceTests.Harness("{}");await f.Discover();f.Running=false;f.Handler.AllowWrites=false;
                var settings=Proxy.Make<ISettingsService>((m,_)=>m.Name switch {"get_CurrentGroupId"=>Group,"get_Settings"=>new AppSettings{GroupId=Group},_=>null});
                var vm=new GameLogViewModel(f.Api,settings,new GroupInviteLedger(f.Temp.Db)){InvitePacing=()=>Task.CompletedTask};await vm.LoadRetainedHistoryAsync();vm.SelectAllCommand.Execute(null);
                vm.ConfirmGroupInvites=(_,_)=>{if(captured=="account")typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(f.Api,PresenceTests.User);else typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(f.Api,captured!="logout");return Task.FromResult(true);};
                await vm.InviteToGroupCommand.ExecuteAsync(null);PresenceTests.Check(f.Handler.Writes==0,"Changed actor/auth sent manual history invite");
            });
        }
        yield return ("JoinNotObserved detection remains informational for authorised historical GROUP invite",async()=>
        {
            using var f=new PresenceTests.Harness(PresenceTests.Json(PresenceTests.User,"private"));await f.Discover();
            System.IO.File.WriteAllText(f.Temp.Log,"[Behaviour] no completed join markers\n");
            PresenceTests.Check(f.Instance.GetCurrentInstance()==null&&f.Instance.DetectionDiagnostic.Code==InstanceDetectionCode.JoinNotObserved,"Fixture must reproduce JoinNotObserved");
            await f.Discover();PresenceTests.Check(await f.Eligible()&&f.Guard(),"JoinNotObserved wrongly blocks GROUP history");await f.AttemptGuarded();PresenceTests.Check(f.Handler.Writes==1,"Historical group invite blocked by diagnostic");
        });
        yield return ("Auto enables without current instance or complete member cache and confirms history scope",async()=>
        {
            using var f=new PresenceTests.Harness("{}");await f.Discover();f.Running=false;
            await new GroupInviteLedger(f.Temp.Db).SaveMembershipAsync(new(Group,new(),DateTime.UtcNow,false));
            var confirms=0;f.Vm.ConfirmAutoInvite=(c,scope)=>{confirms++;PresenceTests.Check(c.GroupId==Group&&scope.Contains("History scope:")&&scope.Contains("Include future exact-ID"),"Scope/account confirmation missing");return true;};
            var running=f.Vm.StartAutoInviteAsync();
            PresenceTests.Check(confirms==1&&f.Vm.AutoInviteEnabled&&!running.IsCompleted,"Blanket complete-cache/current-instance restriction prevented explicit opt-in");
            f.Vm.StopAutoInvite();await running;PresenceTests.Check(f.Handler.Reads==0&&f.Handler.Writes==0,"Enabling sent before the five-second queue tick");
        });
        foreach(var include in new[]{false,true})
        {
            var captured=include;
            yield return ("Future local exact-ID encounter joins queue only under explicitly authorised future scope: "+include,async()=>
            {
                using var f=new PresenceTests.Harness("{}");await f.Discover();
                typeof(InstanceInviterViewModel).GetField("_futureHistoryAuthorised",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(f.Vm,captured);
                const string newcomer="usr_88888888-8888-8888-8888-888888888888";f.Handler.IdentityBody=PresenceTests.Json(newcomer,"offline");
                System.IO.File.AppendAllText(f.Temp.Log,$"[Behaviour] OnPlayerJoined New ({newcomer})\n[Behaviour] OnPlayerJoined Invalid (usr_bad)\n");await f.Discover();
                var user=new InstanceUser{UserId=newcomer,DisplayName="New"};var eligible=await (Task<bool>)f.Invoke("IsAutoEligibleAsync",user)!;
                PresenceTests.Check(eligible==captured,"Future history was silently admitted or consented scope failed");
                PresenceTests.Check(f.Handler.Reads==(captured?1:0)&&f.Handler.Writes==0,"Discovery must be local; eligibility validates exact identity once only under consent");
            });
        }
        yield return ("Invalid exact ID and self remain ineligible in historical consent",async()=>
        {
            using var f=new PresenceTests.Harness("null");await f.Discover();
            foreach(var id in new[]{"usr_bad","usr_99999999-9999-9999-9999-999999999999\n","display name",f.Api.CurrentUserId!})
            {
                ((HashSet<string>)typeof(InstanceInviterViewModel).GetField("_approvedHistory",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(f.Vm)!).Add(id);
                var result=await (Task<bool>)f.Invoke("IsAutoEligibleAsync",new InstanceUser{UserId=id,DisplayName="Suspect"})!;
                PresenceTests.Check(!result,"Invalid identity/self eligible");
            }
            PresenceTests.Check(f.Handler.Reads==0&&f.Handler.Writes==0,"Invalid/self made HTTP calls");
        });
    }
}
