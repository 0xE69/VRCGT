using System.IO;
using System.Net.Http;
using VRCGroupTools.Services;
var root = Path.Combine(Environment.CurrentDirectory, "tests", "PublicationTests", "fixtures-" + Guid.NewGuid()); Directory.CreateDirectory(root);
int passed = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); }
async Task Test(string name, Func<Task> action) { await action(); passed++; Console.WriteLine("PASS " + name); }
var g = "grp_11111111-1111-1111-1111-111111111111"; var a = "usr_11111111-1111-1111-1111-111111111111";
PublicationDraft Draft() => new("draft-1", a, g, "news", "Exact title", "Exact text", "group", false);
string Db() => Path.Combine(root, Guid.NewGuid() + ".db");
await Test("cancel means zero writes", async () => { var t = new Fake(a,g); var p = new ApprovalPublisher(t,Db()); var r = await p.PublishAsync(Draft, _ => Task.FromResult(false)); Check(t.Creates == 0 && r.Publication.State == "NotSent", "cancel sent"); });
await Test("edit during asynchronous confirmation invalidates approval", async () => { var t = new Fake(a,g); var d=Draft(); var p = new ApprovalPublisher(t,Db()); await p.PublishAsync(()=>d, _=> { d=d with { Body="edit" }; return Task.FromResult(true); }); Check(t.Creates == 0,"edited sent"); });
await Test("account group session and permission change prevent write", async () => { foreach(var change in new[]{"account","group","session","permission"}) { var t = new Fake(a,g); var p = new ApprovalPublisher(t,Db()); await p.PublishAsync(Draft,_=> { if(change=="permission") t.Permission=false; else t.State = change switch { "account"=>t.State with {AccountId="other"}, "group"=>t.State with {GroupId="other"}, _=>t.State with {Session=2} }; return Task.FromResult(true); }); Check(t.Creates==0,change+" sent"); } });
await Test("expired approval never writes", async () => { var t=new Fake(a,g); var now=DateTime.UtcNow; var p=new ApprovalPublisher(t,Db(),()=>now); await p.PublishAsync(Draft,_=>{now=now.AddMinutes(6);return Task.FromResult(true);}); Check(t.Creates==0,"expired sent"); });
await Test("restart and concurrent claims block uncertain writes", async () => { var t=new Fake(a,g) { Mode="timeout" }; var db=Db(); var p=new ApprovalPublisher(t,db); await Task.WhenAll(Enumerable.Range(0,8).Select(_=>p.PublishAsync(Draft, _=>Task.FromResult(true)))); await new ApprovalPublisher(t,db).PublishAsync(Draft,_=>Task.FromResult(true)); Check(t.Creates==1,"duplicate unknown"); });
await Test("news and event approvals independent and payload validated", async () => { var t=new Fake(a,g); var p=new ApprovalPublisher(t,Db()); var news=await p.PublishAsync(Draft,_=>Task.FromResult(true)); Check(t.Creates==1 && news.Publication.State=="Confirmed","news failed"); var d=Draft() with {Action="event"}; try { ApprovalPublisher.Preview(d); throw new Exception("event without date accepted"); } catch(ArgumentException) {} Check(t.Creates==1,"news also sent event"); });
await Test("unofficial spec upload and event exact target readback adapter exists", ()=> { Check(typeof(VRChatApiService).GetInterfaces().Contains(typeof(IApprovalPublicationTransport)), "API adapter missing: no real UI-to-HTTP publishing path"); return Task.CompletedTask; });
await Test("calendar integrated explicit event and scheduled news preview commands", () => { var type=typeof(VRCGroupTools.ViewModels.CalendarEventViewModel); Check(type.GetProperty("PublishEventCommand")!=null && type.GetProperty("ReviewScheduledPostCommand")!=null, "No integrated approval UI commands"); return Task.CompletedTask; });
passed += await TransportChecks.Run(root);
Console.WriteLine($"RESULT {passed} passed");
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(root,true);

sealed class Fake(string account,string group):IApprovalPublicationTransport
{
 public PublicationContext State = new(account,group,true,1); public bool Permission=true; public int Creates,Uploads; public string Mode="ok";
 public PublicationContext Context(string group)=>State;
 public Task<bool> CanPublishAsync(PublicationPreview p)=>Task.FromResult(Permission);
 public async Task<PublicationEffect> UploadApprovedAsync(PublicationPreview p,Func<Task<bool>> guard) { if(!await guard())return new("NotSent"); Uploads++;return new("Confirmed","file_11111111-1111-1111-1111-111111111111"); }
 public async Task<PublicationEffect> CreateApprovedAsync(PublicationPreview p,string? imageId,Func<Task<bool>> guard) { if(!await guard())return new("NotSent"); Interlocked.Increment(ref Creates); if(Mode=="timeout")throw new HttpRequestException(); return new("Confirmed","not_11111111-1111-1111-1111-111111111111"); }
}
