using System.Net;
using System.Net.Http;
using System.Reflection;
using VRCGroupTools.Services;

static class AccountPinTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All()
    {
        yield return ("Account ID alone changes without auth-generation change are rejected", async () =>
        {
            using var handler = new DenyHandler();
            var api = new VRChatApiService(new HttpClient(handler) { BaseAddress = new Uri("https://offline.invalid/") });
            typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(api, true);
            typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(api, "account-A");
            var generation = api.InvitationSession;
            var instances = StubProxy.Create<IInstanceInviterService>((_, _) => Fixture.Location("123"));
            var workflow = CurrentInstanceInviteWorkflow.Create(api, instances, (_, _) =>
            { typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(api, "account-B"); return true; });
            var report = await workflow.RunAsync(new[] { "recipient" }, CancellationToken.None);
            ApiTests.Assert(api.InvitationSession == generation && handler.Calls == 0 && report.Outcomes.Single().Status == InstanceInviteStatus.NotSent, "Account ID was not independently pinned");
        });
        yield return ("Authentication change during inter-recipient pacing preserves prior sent and blocks remaining", async () =>
        {
            (string AccountId, long Session)? auth = ("account-A", 1);
            var sends = 0;
            var workflow = new CurrentInstanceInviteWorkflow(() => Fixture.Location("123"),
                (_, _, _, _) => { sends++; return Task.FromResult(true); }, (_, _) => true,
                _ => { auth = ("account-A", 2); return Task.CompletedTask; }, () => auth);
            var report = await workflow.RunAsync(new[] { "first", "remaining" }, CancellationToken.None);
            ApiTests.Assert(sends == 1 && report.Outcomes[0].Status == InstanceInviteStatus.Sent && report.Outcomes[1].Status == InstanceInviteStatus.NotSent, "Per-recipient authentication validation missing");
        });
        foreach (bool differentAccount in new[] { false, true })
        foreach (var stage in new[] { "confirmation", "HTTP pacing" })
        {
            var different = differentAccount; var captured = stage;
            yield return ($"Acting {(different ? "different account" : "same-account reauthentication")} during {stage} blocks HTTP", () => Reject(different, captured));
        }
    }
    static async Task Reject(bool different, string stage)
    {
        using var handler = new DenyHandler();
        var api = new VRChatApiService(new HttpClient(handler) { BaseAddress = new Uri("https://offline.invalid/") });
        void Set(string name, object value) => typeof(VRChatApiService).GetProperty(name)!.SetValue(api, value);
        Set("IsLoggedIn", true);
        Set("CurrentUserId", "usr_11111111-1111-1111-1111-111111111111");
        void ChangeAuth()
        {
            Set("IsLoggedIn", false);
            if (different) Set("CurrentUserId", "usr_33333333-3333-3333-3333-333333333333");
            Set("IsLoggedIn", true);
        }
        var before = api.InvitationSession;
        var instances = StubProxy.Create<IInstanceInviterService>((_, _) => Fixture.Location("123"));
        var workflow = CurrentInstanceInviteWorkflow.Create(api, instances, (_, _) => { if (stage == "confirmation") ChangeAuth(); return true; });
        if (stage == "HTTP pacing") typeof(VRChatApiService).GetField("_lastRequestTime", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(api, DateTime.Now);
        var task = workflow.RunAsync(new[] { "usr_22222222-2222-2222-2222-222222222222", "usr_44444444-4444-4444-4444-444444444444" }, CancellationToken.None);
        if (stage == "HTTP pacing")
        {
            ApiTests.Assert(!task.IsCompleted && handler.Calls == 0, "Test did not reach real API pacing before dispatch");
            ChangeAuth();
        }
        var report = await task;
        ApiTests.Assert(api.InvitationSession != before, "Fixture did not advance actual auth generation");
        ApiTests.Assert(handler.Calls == 0 && report.Outcomes.All(o => o.Status == InstanceInviteStatus.NotSent),
            $"Changed authentication reached HTTP={handler.Calls}; outcomes={string.Join(",", report.Outcomes.Select(o => o.Status))}");
    }
    sealed class DenyHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { Calls++; throw new InvalidOperationException("Network denied: changed auth reached HTTP"); }
    }
}
