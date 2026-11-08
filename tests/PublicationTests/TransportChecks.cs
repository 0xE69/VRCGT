using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;

static class TransportChecks
{
 public static async Task<int> Run(string root)
 {
  int count=0;
  void Check(bool ok,string why) {if(!ok)throw new Exception(why);}
  async Task Test(string name,Func<Task> test){await test();count++;Console.WriteLine("PASS "+name);}
  string g="grp_11111111-1111-1111-1111-111111111111",a="usr_11111111-1111-1111-1111-111111111111";
  PublicationDraft News()=>new("news",a,g,"news","Title","Body","group",true);
  string Db()=>Path.Combine(root,Guid.NewGuid()+".db");
  VRChatApiService Api(FakeHttp h){var api=new VRChatApiService(new HttpClient(h){BaseAddress=new("https://offline.invalid/")},new GroupInviteLedger(Db()));typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(api,true);typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(api,a);return api;}
  var image=Path.Combine(root,"approved.png");
  var pixels=new byte[16*16*4]; for(int i=0;i<pixels.Length;i+=4){pixels[i]=255;pixels[i+3]=255;}
  var bitmap=BitmapSource.Create(16,16,96,96,PixelFormats.Bgra32,null,pixels,64);bitmap.Freeze();
  var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(image))encoder.Save(output);
  await Test("actual news adapter uses one POST then exact ID from group readback",async()=>{var h=new FakeHttp(a,g);var api=Api(h);var report=await new ApprovalPublisher(api,Db()).PublishAsync(News,_=>Task.FromResult(true));Check(report.Publication.State=="Confirmed"&&h.Writes==1&&h.Readbacks==1,"news readback failed: "+report);Check(h.Last!["sendNotification"]!.Value<bool>()&&h.Last["roleIds"] is JArray roles&&roles.Count==0,"payload defaults");});
  await Test("readback mismatch retains ID and never re-creates",async()=>{var h=new FakeHttp(a,g){Mode="mismatch"};var api=Api(h);var p=new ApprovalPublisher(api,Db());var report=await p.PublishAsync(News,_=>Task.FromResult(true));await p.PublishAsync(News,_=>Task.FromResult(true));Check(report.Publication.State=="SentUnverified"&&report.Publication.TargetId!=null&&h.Writes==1,"mismatch retried/lost id");});
  await Test("timeout 5xx redirect 429 malformed write responses never replay",async()=>{foreach(var mode in new[]{"timeout","500","redirect","429","malformed"}){var h=new FakeHttp(a,g){Mode=mode};var api=Api(h);var p=new ApprovalPublisher(api,Db());await p.PublishAsync(News,_=>Task.FromResult(true));await p.PublishAsync(News,_=>Task.FromResult(true));Check(h.Writes==1,mode+" replayed");}});
  await Test("news/calendar permissions independently fail closed",async()=>{var h=new FakeHttp(a,g){Owner=false,Permission="group-calendar-manage"};var api=Api(h);var preview=ApprovalPublisher.Preview(News());Check(!await api.CanPublishAsync(preview),"calendar permits news");h.Permission="group-announcement-manage";Check(await api.CanPublishAsync(preview),"news denied");h.Permission="";Check(!await api.CanPublishAsync(preview),"missing permits news");});
  await Test("guard after pacing sees logout during permission HTTP and sends nothing",async()=>{var h=new FakeHttp(a,g);var api=Api(h);int calls=0;h.OnGroup=()=>{if(++calls==3)api.Logout();};var report=await new ApprovalPublisher(api,Db()).PublishAsync(News,_=>Task.FromResult(true));Check(h.Writes==0&&report.Publication.State=="NotSent","logout sent");});
  await Test("PNG exact upload route tag owner MD5 readback and partial success",async()=>{var h=new FakeHttp(a,g){Mode="postfail"};var api=Api(h);var d=News() with{ImagePath=image};var report=await new ApprovalPublisher(api,Db()).PublishAsync(()=>d,_=>Task.FromResult(true));Check(h.Uploads==1&&h.Writes==1&&report.Upload.State=="Confirmed"&&report.Publication.State=="Rejected","partial lost: "+report);Check(h.Multipart!.Contains("gallery")&&h.Multipart.Contains("image/png"),"upload tag/mime missing");});
  await Test("unknown upload prevents post and is durably blocked",async()=>{var h=new FakeHttp(a,g){Mode="uploadtimeout"};var api=Api(h);var d=News() with{ImagePath=image};var db=Db();var report=await new ApprovalPublisher(api,db).PublishAsync(()=>d,_=>Task.FromResult(true));await new ApprovalPublisher(api,db).PublishAsync(()=>d,_=>Task.FromResult(true));Check(h.Uploads==1&&h.Writes==0&&report.Upload.State=="Unknown","unknown upload repeated");});
  await Test("changed PNG invalidates approval before any upload",async()=>{var h=new FakeHttp(a,g);var api=Api(h);var changed=Path.Combine(root,"changed.png");File.Copy(image,changed);var d=News() with{ImagePath=changed};await new ApprovalPublisher(api,Db()).PublishAsync(()=>d,_=>{File.AppendAllText(changed,"changed");return Task.FromResult(true);});Check(h.Uploads==0&&h.Writes==0,"changed image sent");});
  await Test("invalid payload identity audience and image types rejected offline",()=>{foreach(var d in new[]{News() with{GroupId="grp_bad"},News() with{AccountId="usr_bad"},News() with{Audience="private"},News() with{Title=new string('x',65)},News() with{ImagePath=Path.Combine(root,"absent.png")}}){try{ApprovalPublisher.Preview(d);throw new Exception("invalid accepted");}catch(ArgumentException){}}return Task.CompletedTask;});
  await Test("WPF VM event command maps timezone and actual calendar exact GET",async()=>{var h=new FakeHttp(a,g);var api=Api(h);var eventService=new CalendarEventService(api,DispatchProxy.Create<IDiscordWebhookService,NoExternal>(),Path.Combine(root,Guid.NewGuid().ToString()),()=>DateTime.UtcNow);var vm=new CalendarEventViewModel(eventService,api,()=>g,()=>new(){g}){PublisherOverride=new ApprovalPublisher(api,Db()),ConfirmPublicationOverride=_=>Task.FromResult(true)};vm.Draft=new(){Name="Title",Description="Body",GroupId=g,TimeZoneId="Eastern Standard Time",StartTime=new(2027,1,15,12,0,0),EndTime=new(2027,1,15,13,0,0),Category="Film & Media",Visibility="Group"};vm.DraftPlatformWindows=true;await vm.PublishEventCommand.ExecuteAsync(null);Check(h.Writes==1&&h.Readbacks==1&&h.Last!["startsAt"]!.Value<DateTime>().Hour==17&&h.Last["category"]!.Value<string>()=="film_media"&&vm.Draft.ExternalId==FakeHttp.Cal,"event integration failed: "+vm.StatusMessage);});
  await Test("scheduled draft review is local then approval command publishes exact selected group",async()=>{var h=new FakeHttp(a,g);var api=Api(h);var folder=Path.Combine(root,Guid.NewGuid().ToString());var service=new CalendarEventService(api,DispatchProxy.Create<IDiscordWebhookService,NoExternal>(),folder,()=>DateTime.UtcNow);var selected=g;var vm=new CalendarEventViewModel(service,api,()=>selected,()=>new(){g}){PublisherOverride=new ApprovalPublisher(api,Db()),ConfirmPublicationOverride=_=>Task.FromResult(true)};vm.ReviewScheduledPostCommand.Execute(new ScheduledPostDraft("rule:event",g,"Title","Body",null,DateTime.UtcNow));Check(h.Writes==0,"review sent");selected="grp_22222222-2222-2222-2222-222222222222";await vm.PublishNewsCommand.ExecuteAsync(null);Check(h.Writes==0,"wrong selected group sent");selected=g;await vm.PublishNewsCommand.ExecuteAsync(null);Check(h.Writes==1&&vm.StatusMessage.Contains("Confirmed"),"news UI not integrated: "+vm.StatusMessage);});
await Test("news can be saved locally without publication",()=>{Check(typeof(CalendarEventViewModel).GetProperty("SaveNewsDraftCommand")!=null,"missing local news save");return Task.CompletedTask;});
  await Test("manual editor news updates require exact-target approval support",()=>{Check(typeof(PublicationDraft).GetProperty("ExistingPostId")!=null,"manual update approval target missing");return Task.CompletedTask;});
  await Test("manual news PUT exact-target readback and independently approved later edit",async()=>{var h=new FakeHttp(a,g);var api=Api(h);var d=News() with{Action="news-update",Key=FakeHttp.Post,ExistingPostId=FakeHttp.Post};var p=new ApprovalPublisher(api,Db());var first=await p.PublishAsync(()=>d,_=>Task.FromResult(true));Check(first.Publication.State=="Confirmed","update failed: "+first);d=d with{Body="Edited again"};var second=await p.PublishAsync(()=>d,_=>Task.FromResult(true));Check(second.Publication.State=="Confirmed"&&h.Writes==2,"fresh verified edit blocked");});
  await Test("news local save persists payload without any API calls",async()=>{var h=new FakeHttp(a,g);var api=Api(h);var folder=Path.Combine(root,Guid.NewGuid().ToString());var service=new CalendarEventService(api,DispatchProxy.Create<IDiscordWebhookService,NoExternal>(),folder,()=>DateTime.UtcNow);var vm=new CalendarEventViewModel(service,api,()=>g,()=>new(){g}){NewsTitle="Local title",NewsBody="Unfinished body",NewsAudience="public",NewsNotify=true};await vm.SaveNewsDraftCommand.ExecuteAsync(null);var loaded=await new CalendarEventService(api,DispatchProxy.Create<IDiscordWebhookService,NoExternal>(),folder,()=>DateTime.UtcNow).GetPendingPostsAsync();Check(loaded.Count==1&&loaded[0].Title=="Local title"&&loaded[0].Notify&&loaded[0].Audience=="public"&&h.Writes==0,"local save failed");});
  await Test("durable publication history is readable without reapproving or sending",async()=>{var h=new FakeHttp(a,g){Mode="timeout"};var api=Api(h);var db=Db();var p=new ApprovalPublisher(api,db);await p.PublishAsync(News,_=>Task.FromResult(true));var restarted=new ApprovalPublisher(api,db);var history=await restarted.ReadHistoryAsync(g);Check(history.Count==1&&history[0].State=="Unknown"&&h.Writes==1,"unknown history missing or read sent");Check((await restarted.ReadHistoryAsync("grp_22222222-2222-2222-2222-222222222222")).Count==0,"cross-group history leaked");});
  await Test("calendar editor preserves chosen day and clocks and rejects invalid input",async()=>{var h=new FakeHttp(a,g);var api=Api(h);var folder=Path.Combine(root,Guid.NewGuid().ToString());var service=new CalendarEventService(api,DispatchProxy.Create<IDiscordWebhookService,NoExternal>(),folder,()=>DateTime.UtcNow);var vm=new CalendarEventViewModel(service,api,()=>g,()=>new(){g}){PublisherOverride=new ApprovalPublisher(api,Db()),ConfirmPublicationOverride=_=>Task.FromResult(true)};vm.Draft=new(){Name="Date clock test",Description="body",GroupId=g,TimeZoneId="Eastern Standard Time"};vm.StartDate=new(2027,1,15);vm.EndDate=new(2027,1,15);vm.StartClock="18:25";vm.EndClock="19:05";await vm.SaveEventCommand.ExecuteAsync(null);var saved=(await service.GetEventsAsync()).Single();Check(saved.StartTime==new DateTime(2027,1,15,18,25,0)&&saved.EndTime==new DateTime(2027,1,15,19,5,0)&&h.Writes==0,"clock lost chosen day or save sent");vm.EditEventCommand.Execute(saved);Check(vm.StartClock=="18:25"&&vm.StartDate==new DateTime(2027,1,15),"reopened draft lost clocks/date");vm.StartClock="25:99";await vm.PublishEventCommand.ExecuteAsync(null);Check(h.Writes==0&&vm.StatusMessage.Contains("HH:mm"),"invalid clock silently used old time");});
  return count;
 }
}
public class NoExternal:DispatchProxy {protected override object? Invoke(MethodInfo? method,object?[]? args)=>throw new Exception("Unexpected external call");}
sealed class FakeHttp(string actor,string group):HttpMessageHandler
{
 public const string Cal="cal_11111111-1111-1111-1111-111111111111",Post="not_11111111-1111-1111-1111-111111111111",FileId="file_11111111-1111-1111-1111-111111111111";
 public string Mode="ok",Permission="group-announcement-manage";public bool Owner=true;public int Writes,Uploads,Readbacks;public JObject? Last;public string? Multipart;public Action? OnGroup;private string? md5;
 static HttpResponseMessage Json(object body)=>new(HttpStatusCode.OK){Content=new StringContent(JsonConvert.SerializeObject(body))};
 protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct)
 {
  var path=r.RequestUri!.AbsolutePath.TrimStart('/');
  if(r.Method==HttpMethod.Post || r.Method==HttpMethod.Put)
  {
   if(path=="file/image") {Uploads++;Multipart=await r.Content!.ReadAsStringAsync(ct);if(Mode=="uploadtimeout")throw new HttpRequestException();var multipart=(MultipartFormDataContent)r.Content;var file=multipart.First(p=>p.Headers.ContentType?.MediaType=="image/png");md5=Convert.ToBase64String(MD5.HashData(await file.ReadAsByteArrayAsync(ct)));return Json(new{id=FileId});}
   Writes++;Last=JObject.Parse(await r.Content!.ReadAsStringAsync(ct));
   if(Mode=="timeout")throw new HttpRequestException();if(Mode=="500")return new(HttpStatusCode.InternalServerError);if(Mode=="redirect")return new(HttpStatusCode.TemporaryRedirect);if(Mode=="429")return new(HttpStatusCode.TooManyRequests);if(Mode=="postfail")return new(HttpStatusCode.Forbidden);if(Mode=="malformed")return new(HttpStatusCode.OK){Content=new StringContent("invalid")};
   if(path!=$"groups/{group}/posts"&&path!=$"groups/{group}/posts/{Post}"&&path!=$"calendar/{group}/event")throw new Exception("Wrong write endpoint");return Json(new{id=path.StartsWith("calendar")?Cal:Post});
  }
  if(path==$"groups/{group}"){OnGroup?.Invoke();return Json(new{id=group,ownerId=Owner?actor:"other"});}
  if(path==$"groups/{group}/members/{actor}")return Json(new{userId=actor,roleIds=new[]{"role"}});
  if(path==$"groups/{group}/roles")return Json(new[]{new{id="role",permissions=new[]{Permission}}});
  if(path=="file/"+FileId)return Json(new{id=FileId,ownerId=actor,versions=new[]{new{file=new{md5,status="complete"}}}});
  if(path==$"groups/{group}/posts"||path==$"calendar/{group}/{Cal}")
  {Readbacks++;var target=(JObject)Last!.DeepClone();target["id"]=path.StartsWith("calendar")?Cal:Post;target[path.StartsWith("calendar")?"ownerId":"groupId"]=group;if(Mode=="mismatch")target["text"]="WRONG";return path.StartsWith("calendar")?Json(target):Json(new{posts=new[]{target},total=1});}
  throw new Exception("Unexpected read endpoint "+path);
 }
}
