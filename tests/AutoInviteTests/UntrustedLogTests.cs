using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using VRCGroupTools.Services;
using VRCGroupTools.ViewModels;

static class UntrustedLogTests
{
    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("Recognized timestamped/bare player events preserve joins and departures; spoofed Left is inert", () =>
        {
            using var temp = new Temp();
            const string user = "usr_22222222-2222-2222-2222-222222222222";
            File.WriteAllText(temp.Log, "[Behaviour] Joining wrld_11111111-1111-1111-1111-111111111111:123\n[Behaviour] OnJoinedRoom\n" +
                $"2026.11.07 12:00:00 Log        -  [Behaviour] OnPlayerJoined Exact ({user})\n[Behaviour] OnPlayerJoined Name Only\n");
            var start = DateTime.UtcNow.AddMinutes(-1);
            var service = new InstanceInviterService(temp.Dir, () => start, () => DateTime.UtcNow);
            ApiCheck(service.GetLocalUsers().Count == 2, "Recognized timestamped or bare event lost");
            File.AppendAllText(temp.Log, $"2026.11.07 12:00:01 Log - [UdonBehaviour] OnPlayerLeft Exact ({user})\n[Behaviour] message: OnPlayerLeft Name Only\n");
            ApiCheck(service.GetLocalUsers().Count == 2, "Untrusted Left removed a real occupant");
            File.AppendAllText(temp.Log, $"2026.11.07 12:00:02 Log - [Behaviour] OnPlayerLeft Exact ({user})\n[Behaviour] OnPlayerLeft Name Only\n");
            ApiCheck(service.GetLocalUsers().Count == 0, "Recognized departures no longer work");
            return Task.CompletedTask;
        });
        foreach (var envelope in new[] {
            "[UdonBehaviour] OnPlayerJoined",
            "[OtherLogger] OnPlayerJoined",
            "[Behaviour] Udon says OnPlayerJoined",
            "[UdonBehaviour] [Behaviour] OnPlayerJoined",
            "[Behaviour] message: [Behaviour] OnPlayerJoined",
            "OnPlayerJoined",
            "[Behaviour] OnPlayerJoined",
            "[UdonBehaviour] message\n[Behaviour] OnPlayerJoined",
            "[UdonBehaviour] message\r\n[Behaviour] OnPlayerJoined",
            "[UdonBehaviour] message\n2026.11.07 12:00:00 Log - [Behaviour] OnPlayerJoined" })
        {
            var captured = envelope;
            yield return ("Untrusted player envelope cannot auto-invite before HTTP: " + captured, () => Reject(captured));
        }
    }
    static async Task Reject(string envelope)
    {
        using var temp = new Temp();
        const string group = "grp_11111111-1111-1111-1111-111111111111", account = "usr_33333333-3333-3333-3333-333333333333", user = "usr_99999999-9999-9999-9999-999999999999";
        File.WriteAllText(temp.Log, "[Behaviour] Joining wrld_11111111-1111-1111-1111-111111111111:123\n[Behaviour] OnJoinedRoom\n" +
            $"2026.11.07 12:00:00 Log        -  {envelope} Injected ({user})\n");
        var start = DateTime.UtcNow.AddMinutes(-1);
        var instance = new InstanceInviterService(temp.Dir, () => start, () => DateTime.UtcNow);
        var ledger = new GroupInviteLedger(temp.Db);
        await ledger.SaveMembershipAsync(new(group, new(), DateTime.UtcNow, true));
        using var handler = new DenyHandler();
        var api = new VRChatApiService(new HttpClient(handler) { BaseAddress = new Uri("https://offline.invalid/") }, ledger);
        typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(api, true);
        typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(api, account);
        var settings = new AppSettings { GroupId = group, InstanceInviterOnly18Plus = false };
        var settingsService = Proxy.Make<ISettingsService>((m, _) => m.Name switch { "get_Settings" => settings, "get_CurrentGroupId" => group, _ => null });
        using var vm = new InstanceInviterViewModel(api, settingsService, instance, Proxy.Make<ICacheService>((_, _) => null), ledger);
        typeof(InstanceInviterViewModel).GetField("_autoInviteEnabled", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, true);
        object? Invoke(string name, params object[] args) => typeof(InstanceInviterViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args);
        var context = (AutoGroupInviteContext)Invoke("ReadAutoContext")!;
        var eligible = 0;
        var session = new AutoGroupInviteSession(context, () => (AutoGroupInviteContext?)Invoke("ReadAutoContext"),
            () => (Task<List<InstanceUser>>)Invoke("DiscoverAutoUsersAsync")!,
            async u => { var ok = await (Task<bool>)Invoke("IsAutoEligibleAsync", u)!; if (ok) eligible++; return ok; },
            u => (bool)Invoke("StillAutoEligible", u)!,
            async (id, guard, token) => { var sent = await api.SendAutomaticGroupInviteAsync(group, id, guard, token); return new(sent, sent ? GroupInviteState.Sent : GroupInviteState.Failed, "test"); }, message => Console.WriteLine("UNTRUSTED TEST " + message));
        await session.TickAsync(); await session.PendingSend; session.Stop();
        foreach (var hint in instance.GetLocalUsers())
        {
            ApiCheck(!(bool)Invoke("StillAutoEligible", hint)!, "Final guard accepted uncorroborated hint");
            try { await api.SendAutomaticGroupInviteAsync(group, hint.UserId, () => (bool)Invoke("StillAutoEligible", hint)!, CancellationToken.None); }
            catch (GroupInviteSuppressedException) { }
        }
        ApiCheck(handler.Calls == 0 && eligible == 0,
            $"Uncorroborated envelope reached eligibility/write HTTP: eligible={eligible}, HTTP={handler.Calls}, occupants={instance.GetLocalUsers().Count}");
    }
    static void ApiCheck(bool ok, string message) { if (!ok) throw new Exception(message); }
    sealed class DenyHandler : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken c)
        { if (r.Method == HttpMethod.Get) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
          Calls++; throw new InvalidOperationException("Network denied: untrusted input reached write HTTP"); }
    }
}
