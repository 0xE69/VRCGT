using System.Net;
using System.Net.Http;
using System.Reflection;
using VRCGroupTools.Services;

static class ApiTests
{
    public static async Task GuardPreventsHttpRequest()
    {
        var ctor = typeof(VRChatApiService).GetConstructor(new[] { typeof(HttpClient) });
        Assert(ctor != null, "Guarded invite transport seam is missing");
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid/") };
        var api = (VRChatApiService)ctor!.Invoke(new object[] { client });
        var method = typeof(VRChatApiService).GetMethod("InviteUserToInstanceGuardedAsync");
        Assert(method != null, "Final pre-request destination guard is missing");
        Exception? failure = null;
        try { await (Task<bool>)method!.Invoke(api, new object[] { "usr_fixture", "world", "123", (Func<bool>)(() => false) })!; }
        catch (Exception ex) { failure = ex; }
        Assert(handler.Requests == 0, "Guard rejected destination but HTTP was sent");
        Assert(failure?.GetType().Name == "InstanceInviteNotSentException", "Must distinguish unsent from uncertain requests");
    }
    public static async Task ProductionWorkflowChecksAuthAndFinalDestination()
    {
        bool loggedIn = false;
        var destination = Fixture.Location("123");
        int sends = 0, confirmations = 0;
        var api = StubProxy.Create<IVRChatApiService>((method, args) => method.Name switch
        {
            "get_IsLoggedIn" => loggedIn,
            "get_CurrentUserId" => "usr_11111111-1111-1111-1111-111111111111",
            "get_InvitationSession" => 1L,
            "InviteUserToInstanceGuardedAsync" => Guarded(args!),
            _ => throw new InvalidOperationException("Unexpected API call: " + method.Name)
        });
        Task<bool> Guarded(object?[] args)
        {
            sends++;
            destination = Fixture.Location("456");
            if (!((Func<bool>)args[3]!)()) throw new InstanceInviteNotSentException();
            return Task.FromResult(true);
        }
        var session = StubProxy.Create<IInstanceInviterService>((method, args) => destination);
        var create = typeof(CurrentInstanceInviteWorkflow).GetMethod("Create");
        Assert(create != null, "Production workflow factory is missing");
        var workflow = (CurrentInstanceInviteWorkflow)create!.Invoke(null, new object[] { api, session, (Func<CurrentInstanceInfo, int, bool>)((_, _) => { confirmations++; return true; }) })!;
        await workflow.RunAsync(new[] { "a" }, CancellationToken.None);
        Assert(confirmations == 0 && sends == 0, "Logged-out sessions must not prompt or send");
        loggedIn = true;
        var report = await workflow.RunAsync(new[] { "a" }, CancellationToken.None);
        Assert(sends == 1 && confirmations == 1 && report.Outcomes[0].Status == InstanceInviteStatus.NotSent, "Production transport must recheck the current destination");
    }

    public static async Task PayloadAndFailureSemantics()
    {
        using var handler = new RecordingHandler();
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://fixture.invalid/") };
        var api = new VRChatApiService(client);
        var location = Fixture.Location("123~private(usr_fixture)~nonce(secret)");
        handler.Respond = async request =>
        {
            Assert(request.Method == HttpMethod.Post && request.RequestUri!.AbsolutePath == "/invite/usr_fixture", "Incorrect invite endpoint");
            var body = await request.Content!.ReadAsStringAsync();
            using var json = System.Text.Json.JsonDocument.Parse(body);
            Assert(json.RootElement.GetProperty("instanceId").GetString() == location.FullLocation, "Private instance modifiers must reach the API unchanged");
            return new HttpResponseMessage(HttpStatusCode.OK);
        };
        Assert(await api.InviteUserToInstanceGuardedAsync("usr_fixture", location.WorldId, location.InstanceId, () => true), "Successful response must be reported as sent");
        handler.Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("secret private location") });
        Assert(!await api.InviteUserToInstanceGuardedAsync("usr_fixture", location.WorldId, location.InstanceId, () => true), "Rejected response cannot be reported as sent");
        handler.Respond = _ => throw new HttpRequestException("lost response");
        bool unknown = false;
        try { await api.InviteUserToInstanceGuardedAsync("usr_fixture", location.WorldId, location.InstanceId, () => true); }
        catch (HttpRequestException) { unknown = true; }
        Assert(unknown, "Transport exceptions must remain distinguishable from rejected responses");
    }

    public static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}

sealed class RecordingHandler : HttpMessageHandler
{
    public int Requests;
    public Func<HttpRequestMessage, Task<HttpResponseMessage>> Respond = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return Respond(request);
    }
}
