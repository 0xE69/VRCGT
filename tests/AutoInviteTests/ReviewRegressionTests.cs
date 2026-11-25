using System.IO;
using System.Net;
using System.Reflection;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;

static class ReviewRegressionTests
{
    static void Check(bool ok,string why)=>PresenceTests.Check(ok,why);
    static void Field(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(target,value);
    public static IEnumerable<(string,Func<Task>)> All()
    {
        foreach(var body in new[]{"{}","null","[]","not json","{\"id\":42}","{\"id\":\"usr_88888888-8888-8888-8888-888888888888\"}"})
        foreach(var filters in new[]{false,true})
        {
            var captured=body;var filtered=filters;
            yield return ($"Strict identity caches untrusted response; filters={filters}: {body}",async()=>
            {
                using var f=new PresenceTests.Harness(captured);f.Vm.SkipKnown=filtered;Field(f.Vm,"_autoInviteEnabled",true);
                await f.Discover();f.Handler.AllowWrites=false;
                Check(!await f.Eligible()&&!f.Guard(),"Unproven identity authorised");await f.AttemptGuarded();
                await f.Discover();Check(!await f.Eligible()&&f.Handler.Reads==1&&f.Handler.Writes==0,"Unchanged poll retried or central guard wrote");
            });
        }
        foreach(var status in new[]{HttpStatusCode.NotFound,HttpStatusCode.Forbidden,HttpStatusCode.TooManyRequests,HttpStatusCode.ServiceUnavailable})
        {
            var captured=status;
            yield return ("Strict identity HTTP error never authorises/retries: "+status,async()=>
            {
                using var f=new PresenceTests.Harness(PresenceTests.Json(PresenceTests.User,"offline"));f.Handler.ReadStatus=captured;f.Handler.AllowWrites=false;
                await f.Discover();Check(!await f.Eligible(),"Identity error authorised");await f.AttemptGuarded();
                await f.Discover();Check(!await f.Eligible()&&f.Handler.Reads==1&&f.Handler.Writes==0,"Identity error retried/wrote");
            });
        }
        yield return ("Strict identity network failure cached across unchanged polls",async()=>
        {
            using var f=new PresenceTests.Harness("{}");f.Handler.ThrowRead=true;f.Handler.AllowWrites=false;await f.Discover();
            Check(!await f.Eligible(),"Network error authorised");await f.Discover();Check(!await f.Eligible()&&f.Handler.Reads==1,"Network failure hammered");await f.AttemptGuarded();Check(f.Handler.Writes==0,"Network error wrote");
        });
        yield return ("Strict identity matching id reused once without any presence metadata",async()=>
        {
            using var f=new PresenceTests.Harness("{\"id\":\""+PresenceTests.User+"\"}");await f.Discover();
            Check(await f.Eligible()&&f.Guard(),"Matching actual ID alone must suffice");await f.Discover();Check(await f.Eligible()&&f.Handler.Reads==1,"Trusted scoped identity was not reused");
            await f.AttemptGuarded();Check(f.Handler.Writes==1,"No location/state payload blocked GROUP invite");
        });
        foreach(var change in new[]{"account","auth","group","off"})
        {
            var captured=change;
            yield return ("Strict identity final central guard invalidates after pacing: "+change,async()=>
            {
                using var f=new PresenceTests.Harness(PresenceTests.Json(PresenceTests.User,"offline"));await f.Discover();Check(await f.Eligible(),"Expected validated precondition");
                Field(f.Api,"_lastRequestTime",DateTime.Now);f.Handler.AllowWrites=false;
                var pending=f.AttemptGuarded();Check(!pending.IsCompleted,"Pacing not exercised");
                if(captured=="account")typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(f.Api,"usr_88888888-8888-8888-8888-888888888888");
                if(captured=="auth")typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(f.Api,true);
                if(captured=="group")f.Settings.GroupId="grp_88888888-8888-8888-8888-888888888888";
                if(captured=="off")f.Vm.StopAutoInvite();
                await pending;Check(!f.Guard()&&f.Handler.Writes==0,"Stale identity/consent reached write transport");
            });
        }
        yield return ("Strict identity scope requires fresh lookup after same-account reauthentication",async()=>
        {
            using var f=new PresenceTests.Harness(PresenceTests.Json(PresenceTests.User,"private"));await f.Discover();Check(await f.Eligible(),"Expected initial identity");
            typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(f.Api,true);Check(!f.Guard(),"Stale authentication identity reused");
            f.Handler.IdentityBody="{}";await f.Discover();Check(!await f.Eligible()&&f.Handler.Reads==2,"New auth scope reused old response or skipped lookup");
        });
        foreach(var origin in new[]{"other-group","unknown","client-closed"})
        foreach(var location in new[]{"offline","private"})
        {
            var source=origin;var state=location;
            yield return ($"GROUP future LOCAL discovery ignores origin ownership/presence: {origin}/{location}",async()=>
            {
                using var f=new PresenceTests.Harness(PresenceTests.Json(PresenceTests.User,state,"offline"));
                var prefix=source=="other-group"?$"[Behaviour] Joining {PresenceTests.Location}~group(grp_88888888-8888-8888-8888-888888888888)\n[Behaviour] OnJoinedRoom\n":"[Behaviour] no join\n";
                File.WriteAllText(f.Temp.Log,prefix);if(source=="client-closed")f.Running=false;
                f.Vm.ConfirmAutoInvite=(_,_)=>true;var running=f.Vm.StartAutoInviteAsync();
                var session=await PresenceTests.WaitForSessionAsync(f.Vm);
                File.AppendAllText(f.Temp.Log,$"[Behaviour] OnPlayerJoined Exact ({PresenceTests.User})\n[Behaviour] OnPlayerLeft Exact ({PresenceTests.User})\n[Behaviour] OnLeftRoom\n");
                await session.TickAsync();await session.PendingSend;Check(f.Handler.Reads==1&&f.Handler.Writes==1,"Ownership/current presence incorrectly required");
                Check((await new GroupInviteLedger(f.Temp.Db).ReadEncountersAsync()).Single().UserId==PresenceTests.User,"Departed historical record lost");
                f.Vm.StopAutoInvite();await running;
            });
        }
        foreach(var body in new[]{"{}","{\"id\":\"usr_88888888-8888-8888-8888-888888888888\"}"})
        {
            var captured=body;
            yield return ("Future LOCAL closed-client history retains but never sends unvalidated ID: "+body,async()=>
            {
                using var f=new PresenceTests.Harness(captured);File.WriteAllText(f.Temp.Log,"[Behaviour] no join\n");f.Running=false;f.Handler.AllowWrites=false;
                f.Vm.ConfirmAutoInvite=(_,_)=>true;var running=f.Vm.StartAutoInviteAsync();
                var session=await PresenceTests.WaitForSessionAsync(f.Vm);
                File.AppendAllText(f.Temp.Log,$"[Behaviour] OnPlayerJoined Exact ({PresenceTests.User})\n[Behaviour] OnPlayerLeft Exact ({PresenceTests.User})\n");
                await session.TickAsync();await session.PendingSend;await session.TickAsync();await session.PendingSend;
                Check((await new GroupInviteLedger(f.Temp.Db).ReadEncountersAsync()).Count==1&&f.Handler.Reads==1&&f.Handler.Writes==0,"Unknown future identity retried/wrote or was not retained");
                f.Vm.StopAutoInvite();await running;
            });
        }
        yield return ("Future LOCAL discovery reaches actual five-second running session without completed join",async()=>
        {
            using var f=new PresenceTests.Harness(PresenceTests.Json(PresenceTests.User,"private","offline"));File.WriteAllText(f.Temp.Log,"[Behaviour] no join\n");f.Running=false;
            f.Vm.ConfirmAutoInvite=(_,_)=>true;var began=System.Diagnostics.Stopwatch.StartNew();var running=f.Vm.StartAutoInviteAsync();
            var session=await PresenceTests.WaitForSessionAsync(f.Vm);
            Check(f.Handler.Reads==0&&f.Handler.Writes==0,"Enabling dispatched before first scheduled poll");
            File.AppendAllText(f.Temp.Log,$"[Behaviour] OnPlayerJoined Exact ({PresenceTests.User})\n[Behaviour] OnPlayerLeft Exact ({PresenceTests.User})\n");
            while(f.Handler.Writes==0&&began.Elapsed<TimeSpan.FromSeconds(15))await Task.Delay(50);
            Check(f.Handler.Reads==1&&f.Handler.Writes==1&&began.Elapsed>=TimeSpan.FromSeconds(5),"Real five-second discovery did not dispatch validated closed-client history");
            await session.PendingSend;
            f.Vm.StopAutoInvite();await running;
        });
        yield return ("Future LOCAL discovery does not authorise arbitrary raw imports",async()=>
        {
            using var f=new PresenceTests.Harness("{}");File.WriteAllText(f.Temp.Log,"[Behaviour] no join\n");
            f.Vm.ConfirmAutoInvite=(_,_)=>true;var running=f.Vm.StartAutoInviteAsync();
            await PresenceTests.WaitForSessionAsync(f.Vm);
            await new GroupInviteLedger(f.Temp.Db).SaveEncountersAsync(new(){new(){UserId=PresenceTests.User,DisplayName="Imported",Source="Raw log import"}});
            var users=await (Task<List<InstanceUser>>)f.Invoke("DiscoverAutoUsersAsync")!;
            Check(!await (Task<bool>)f.Invoke("IsAutoEligibleAsync",users.Single())!&&f.Handler.Reads==0,"Raw import entered future consent");
            f.Vm.StopAutoInvite();await running;
        });
        yield return ("LOCAL history before opt-in is retained read-only with zero network",async()=>
        {
            using var f=new PresenceTests.Harness("{}");Field(f.Vm,"_autoInviteEnabled",false);File.WriteAllText(f.Temp.Log,$"[Behaviour] OnPlayerJoined Exact ({PresenceTests.User})\n[Behaviour] OnPlayerLeft Exact ({PresenceTests.User})\n");f.Running=false;
            var users=await (Task<List<InstanceUser>>)f.Invoke("DiscoverAutoUsersAsync")!;
            Check(users.Single().UserId==PresenceTests.User&&!await (Task<bool>)f.Invoke("IsAutoEligibleAsync",users.Single())!,"No-consent discovery missing or eligible");
            Check(f.Handler.Reads==0&&f.Handler.Writes==0,"Discovery without opt-in made HTTP");
            var restarted=new GroupInviteLedger(f.Temp.Db);Check((await restarted.ReadEncountersAsync()).Count==1,"Restart lost closed-client history");
        });
        yield return ("LOCAL history incremental UTF8 partial records and unsupported envelope diagnostics",async()=>
        {
            using var temp=new Temp();File.WriteAllText(temp.Log,$"[Behaviour] OnPlayerJoined Exact ({PresenceTests.User})");
            var service=new InstanceInviterService(temp.Dir,()=>throw new Exception("History must not inspect processes"),()=>DateTime.UtcNow);
            Check((await service.ReadLocalEncounterHistoryAsync()).Count==0,"Partial record admitted");File.AppendAllText(temp.Log,"\r\n");
            Check((await service.ReadLocalEncounterHistoryAsync()).Count==1&&(await service.ReadLocalEncounterHistoryAsync()).Count==0,"Incremental record lost or replayed");
            File.AppendAllText(temp.Log,$"2026.11.08 12:00:00.123 Trace - [Behaviour] OnPlayerJoined Unknown ({PresenceTests.User})\n");
            Check((await service.ReadLocalEncounterHistoryAsync()).Count==0&&service.EncounterHistoryDiagnostic.Contains("Unsupported"),"Unknown header falsely claimed supported");
        });
        yield return ("Every local encounter survives the former 1000-row limit and closed-client restart",async()=>
        {
            using var temp=new Temp();var lines=Enumerable.Range(0,1005).Select(i=>$"[Behaviour] OnPlayerJoined User{i} (usr_{i:x8}-1111-1111-1111-111111111111)\n");File.WriteAllText(temp.Log,string.Concat(lines));
            var service=new InstanceInviterService(temp.Dir,()=>null,()=>DateTime.UtcNow);var users=await service.ReadLocalEncounterHistoryAsync();
            var ledger=new GroupInviteLedger(temp.Db);await ledger.SaveEncountersAsync(users.Select(u=>new SeenUser{UserId=u.UserId,DisplayName=u.DisplayName}).ToList());
            Check(users.Count==1005&&(await new GroupInviteLedger(temp.Db).ReadEncountersAsync()).Count==1005,"Encounter scan or restart lost historical users");
        });
        yield return ("History preview preserves the durable scan and resumes after split UTF-8 and oversized lines", async () =>
        {
            using var temp = new Temp();
            const string world = "wrld_00000001-1111-2222-3333-444444444444";
            var scanner = new InstanceInviterService(temp.Dir, () => null, () => DateTime.UtcNow);
            var ledger = new GroupInviteLedger(temp.Db);
            File.WriteAllText(temp.Log, $"[Behaviour] Joining {world}:123\n[Behaviour] Joining or Creating Room: Test\n" +
                $"[Behaviour] OnPlayerJoined Earlier ({PresenceTests.User})\n[Behaviour] OnLeftRoom\n" + new string('x', 180_000));
            Check((await scanner.ReadLocalEncounterHistoryAsync()).Single().WorldId == world, "Initial preview lost the world");
            await scanner.SyncLocalEncounterHistoryAsync(ledger);
            Check((await ledger.ReadEncountersAsync()).Single().WorldId == world, "Preview consumed the durable encounter");

            var tail = System.Text.Encoding.UTF8.GetBytes("[Behaviour] OnPlayerJoined Inside oversized line\n" +
                $"2026.11.12 13:00:00.123 Debug - [Behaviour] OnPlayerJoined René ({PresenceTests.User})\n");
            var split = Array.IndexOf(tail, (byte)0xc3) + 1;
            using (var stream = File.Open(temp.Log, FileMode.Append)) stream.Write(tail, 0, split);
            Check((await scanner.SyncLocalEncounterHistoryAsync(ledger)).Count == 0, "Incomplete or oversized record was admitted");
            using (var stream = File.Open(temp.Log, FileMode.Append)) stream.Write(tail, split, tail.Length - split);
            await scanner.SyncLocalEncounterHistoryAsync(ledger);
            var latest = (await ledger.ReadEncountersAsync()).Single();
            Check(latest.DisplayName == "René" && latest.LastSeenAt.Millisecond == 123 && latest.WorldId == null,
                "Split UTF-8, fractional time, or departure was lost");
        });
        yield return ("Full archive scan reads the beginning of a large old log and retains its users after rotation", async () =>
        {
            using var temp = new Temp();
            const string first = "usr_00000001-1111-2222-3333-444444444444";
            const string second = "usr_00000002-1111-2222-3333-444444444444";
            const string third = "usr_00000003-1111-2222-3333-444444444444";
            var archived = Path.Combine(temp.Dir, "output_log_2026-10-01.txt");
            using (var writer = new StreamWriter(archived))
            {
                writer.WriteLine($"2026.10.01 08:00:00 Debug      -  [Behaviour] OnPlayerJoined Archive Only ({third})");
                writer.WriteLine($"2026.10.01 08:30:00 Debug      -  [Behaviour] OnPlayerJoined Old Name ({first})");
                var padding = new string('x', 1023);
                for (var i = 0; i < 21 * 1024; i++) writer.WriteLine(padding);
                writer.WriteLine($"2026.10.01 09:45:00 Debug      -  [Behaviour] OnPlayerJoined Same Name ({second})");
            }
            File.SetLastWriteTimeUtc(archived, DateTime.UtcNow.AddDays(-30));
            for (var i = 0; i < 20; i++) File.WriteAllText(Path.Combine(temp.Dir, $"output_log_new_{i}.txt"), "");
            File.WriteAllText(temp.Log, $"2026.11.01 10:15:00 Debug - [Behaviour] OnPlayerJoined Same Name ({first})\r\n");
            var ledger = new GroupInviteLedger(temp.Db);
            var scanner = new InstanceInviterService(temp.Dir, () => null, () => DateTime.UtcNow);
            await scanner.SyncLocalEncounterHistoryAsync(ledger);
            var retained = await ledger.ReadEncountersAsync();
            Check(retained.Count == 3 && retained.Count(row => row.DisplayName == "Same Name") == 2, "Exact identities were lost or an older name replaced a newer encounter");
            Check(retained.Single(row => row.UserId == first).LastSeenAt == new DateTime(2026, 11, 1, 10, 15, 0), "Last seen was taken from the scan date");
            Check(retained.Single(row => row.UserId == second).LastSeenAt == new DateTime(2026, 10, 1, 9, 45, 0), "The older large archive was skipped");
            Check(retained.Any(row => row.UserId == third), "The first record in the large archive was skipped");
            File.Delete(archived);
            await scanner.SyncLocalEncounterHistoryAsync(ledger);
            Check((await new GroupInviteLedger(temp.Db).ReadEncountersAsync()).Count == 3, "The first record in the archive or previously retained records disappeared");
        });
    }
}
