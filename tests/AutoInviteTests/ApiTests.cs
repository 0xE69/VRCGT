using System.Net;
using System.IO;
using System.Net.Http;
using System.Reflection;
using VRCGroupTools.Services;

static class ApiTests
{
    public static IEnumerable<(string,Func<Task>)> All()
    {
        yield return ("Automatic guard rechecks off/context after central API pacing before HTTP",async()=>{
            using var f=new Temp();var ledger=new GroupInviteLedger(f.Db);var handler=new Transport((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));var api=Api(handler,ledger);
            typeof(VRChatApiService).GetField("_lastRequestTime",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(api,DateTime.Now);
            var valid=true;var task=api.SendAutomaticGroupInviteAsync("g","u",()=>valid,CancellationToken.None);valid=false;await Suppressed(task,GroupInviteState.NotSent);Check(handler.Calls==0&&await ledger.GetStateAsync("g","u")==GroupInviteState.NotSent,"Off during pacing still sent");});
        yield return ("Automatic and manual overlapping claims share exact durable ledger",async()=>{
            using var f=new Temp();var ledger=new GroupInviteLedger(f.Db);var pending=new TaskCompletionSource<HttpResponseMessage>();var handler=new Transport((_,_)=>pending.Task);var api=Api(handler,ledger);
            var first=api.SendAutomaticGroupInviteAsync("g","u",()=>true,CancellationToken.None);await Suppressed(api.SendGroupInviteAsync("g","u"),GroupInviteState.InFlight);pending.SetResult(new(HttpStatusCode.OK));await first;
            var reloaded=Api(handler,new GroupInviteLedger(f.Db));await Suppressed(reloaded.SendAutomaticGroupInviteAsync("g","u",()=>true,CancellationToken.None),GroupInviteState.Sent);Check(handler.Calls==1,"Cross-path/restart duplicate HTTP");});
        yield return ("Automatic uncertain/cancelled dispatched request remains Unknown across reload",async()=>{
            using var f=new Temp();var ledger=new GroupInviteLedger(f.Db);var handler=new Transport((_,_)=>throw new IOException("fixture timeout"));var api=Api(handler,ledger);
            await Suppressed(api.SendAutomaticGroupInviteAsync("g","u",()=>true,CancellationToken.None),GroupInviteState.Unknown);await Suppressed(Api(handler,new GroupInviteLedger(f.Db)).SendGroupInviteAsync("g","u"),GroupInviteState.Unknown);Check(handler.Calls==1,"Unknown retry reached HTTP");});
        yield return ("Automatic known rejection persists Failed and cannot hammer every poll/restart",async()=>{
            using var f=new Temp();var ledger=new GroupInviteLedger(f.Db);var handler=new Transport((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)));var api=Api(handler,ledger);
            Check(!await api.SendAutomaticGroupInviteAsync("g","u",()=>true,CancellationToken.None),"Rejected request marked success");await Suppressed(Api(handler,new GroupInviteLedger(f.Db)).SendAutomaticGroupInviteAsync("g","u",()=>true,CancellationToken.None),GroupInviteState.Failed);Check(handler.Calls==1,"Failed auto retry reached HTTP");});
        yield return ("Cancellation during in-flight request prevents further sends and preserves uncertainty",async()=>{
            using var f=new Temp();var ledger=new GroupInviteLedger(f.Db);var began=new TaskCompletionSource();var handler=new Transport(async(_,token)=>{began.SetResult();await Task.Delay(10000,token);return new(HttpStatusCode.OK);});var api=Api(handler,ledger);using var c=new CancellationTokenSource();
            var request=api.SendAutomaticGroupInviteAsync("g","u",()=>true,c.Token);await began.Task;c.Cancel();await Suppressed(request,GroupInviteState.Unknown);Check(handler.Calls==1,"Wrong cancellation outcome");});
        yield return ("429 Retry-After is retained at shared group HTTP boundary",async()=>{
            using var f=new Temp();var handler=new Transport((_,_)=>{var r=new HttpResponseMessage(HttpStatusCode.TooManyRequests);r.Headers.RetryAfter=new(TimeSpan.FromMinutes(2));return Task.FromResult(r);});var api=Api(handler,new(f.Db));
            await api.SendAutomaticGroupInviteAsync("g","u",()=>true,CancellationToken.None);
            var prop=typeof(VRChatApiService).GetProperty("GroupInviteRetryAfterUtc");Check(prop!=null&&((DateTimeOffset)prop.GetValue(api)!)>DateTimeOffset.UtcNow.AddSeconds(110),"Retry-After lost; re-enable can bypass server backoff");});
    }
    static VRChatApiService Api(HttpMessageHandler handler,GroupInviteLedger ledger){var api=new VRChatApiService(new HttpClient(handler){BaseAddress=new Uri("https://offline.invalid/")},ledger);typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(api,true);return api;}
    static async Task Suppressed(Task<bool> task,GroupInviteState state){try{await task;}catch(GroupInviteSuppressedException e){Check(e.State==state,$"Expected {state}, got {e.State}");return;}throw new Exception("Request not suppressed");}
    static void Check(bool ok,string why){if(!ok)throw new Exception(why);}
    sealed class Transport(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send):HttpMessageHandler {public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken c){Calls++;return send(r,c);}}
}
