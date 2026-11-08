using VRCGroupTools.Services;
using System.IO;

var tests = new List<(string Name, Func<Task> Run)>
{
    ("Only explicitly selected recipients are invited after confirmation", async () =>
    {
        var f = new Fixture();
        await f.Workflow.RunAsync(new[] { "selected" }, CancellationToken.None);
        Check(f.Sent.SequenceEqual(new[] { "selected" }), "Must send only the explicit recipient snapshot");
        Check(f.Confirmations == 1 && f.ConfirmedCount == 1, "Must confirm recipient count and destination");
    }),
    ("Instance change while confirming aborts all sends", async () =>
    {
        var f = new Fixture();
        f.OnConfirm = () => f.Destination = Fixture.Location("456");
        await f.Workflow.RunAsync(new[] { "a", "b" }, CancellationToken.None);
        Check(f.Sent.Count == 0, "Destination changed during confirmation but invites were sent");
    }),
    ("Each recipient has an accurate outcome when transport fails", async () =>
    {
        var f = new Fixture();
        f.Send = user => user switch
        {
            "rejected" => Task.FromResult(false),
            "uncertain" => throw new IOException("private transport details"),
            _ => Task.FromResult(true)
        };
        var task = f.Workflow.RunAsync(new[] { "ok", "rejected", "uncertain", "later" }, CancellationToken.None);
        await task;
        var report = task.GetType().GetProperty("Result")?.GetValue(task);
        Check(report != null, "Per-recipient report is missing");
        var outcomes = (System.Collections.IEnumerable)report!.GetType().GetProperty("Outcomes")!.GetValue(report)!;
        Check(outcomes.Cast<object>().Select(o => o.GetType().GetProperty("Status")!.GetValue(o)!.ToString()).SequenceEqual(new[] { "Sent", "Failed", "Unknown", "Sent" }), "Outcomes must distinguish rejected, unknown and sent");
    }),
    ("Cancellation during pacing leaves remaining recipients unsent", async () =>
    {
        var f = new Fixture();
        using var cancel = new CancellationTokenSource();
        f.Pace = token => { cancel.Cancel(); return Task.FromCanceled(token); };
        var report = await f.Workflow.RunAsync(new[] { "a", "b" }, cancel.Token);
        Check(f.Sent.SequenceEqual(new[] { "a" }), "Cancellation must stop after the in-flight invite");
        Check(report.Outcomes[0].Status == InstanceInviteStatus.Sent && report.Outcomes[1].Status == InstanceInviteStatus.NotSent, "Cancellation lost accurate outcomes");
    }),
    ("A second click cannot start an overlapping batch", async () =>
    {
        var f = new Fixture();
        var pending = new TaskCompletionSource<bool>();
        f.Send = _ => pending.Task;
        var first = f.Workflow.RunAsync(new[] { "a" }, CancellationToken.None);
        var second = f.Workflow.RunAsync(new[] { "a" }, CancellationToken.None);
        Check(f.Sent.Count == 1 && f.Confirmations == 1, "Overlapping click sent duplicate invite");
        pending.SetResult(true);
        await Task.WhenAll(first, second);
    }),
    ("An empty selection never prompts or sends", async () =>
    {
        var f = new Fixture();
        await f.Workflow.RunAsync(Array.Empty<string>(), CancellationToken.None);
        Check(f.Confirmations == 0 && f.Sent.Count == 0, "Empty selection must be a no-op");
    }),
    ("Current session reader returns the completed destination without logging secrets", () =>
    {
        var type = typeof(CurrentInstanceInfo).Assembly.GetType("VRCGroupTools.Services.CurrentInstanceSessionReader");
        Check(type != null, "A session-validated log reader is missing");
        using var log = new LogFixture();
        using var output = new StringWriter();
        var original = Console.Out;
        CurrentInstanceInfo? result;
        try
        {
            Console.SetOut(output);
            result = (CurrentInstanceInfo?)type!.GetMethod("Read")!.Invoke(null, new object?[] { log.Directory, log.Now.AddMinutes(-1), log.Now });
        }
        finally { Console.SetOut(original); }
        Check(result?.FullLocation == log.Location, "Must parse the completed instance including private modifiers");
        Check(!output.ToString().Contains("nonce"), "Private location must not be logged");
        return Task.CompletedTask;
    }),
    ("Offline or stale logs cannot establish a current session", () =>
    {
        using var log = new LogFixture();
        Check(CurrentInstanceSessionReader.Read(log.Directory, null, log.Now) == null, "Offline VRChat must reject logs");
        Check(CurrentInstanceSessionReader.Read(log.Directory, log.Now.AddMinutes(1), log.Now.AddMinutes(1)) == null, "Previous process session must be rejected");
        File.SetLastWriteTimeUtc(log.FilePath, log.Now.AddMinutes(-10));
        Check(CurrentInstanceSessionReader.Read(log.Directory, log.Now.AddHours(-1), log.Now) == null, "Stale log must be rejected");
        return Task.CompletedTask;
    }),
    ("Transitions disconnects and malformed latest joins fail closed", () =>
    {
        using var log = new LogFixture();
        foreach (var boundary in new[] { "Joining wrld_broken", "Joining or Creating Room: pending", "OnLeftRoom", "OnDisconnected", "Joining " + Fixture.Location("456").FullLocation })
        {
            File.WriteAllText(log.FilePath, $"[Behaviour] Joining {log.Location}\n[Behaviour] OnJoinedRoom\n[Behaviour] {boundary}\n");
            Check(CurrentInstanceSessionReader.Read(log.Directory, log.Now.AddMinutes(-1), DateTime.UtcNow) == null, "Must not reuse old instance after " + boundary);
        }
        return Task.CompletedTask;
    }),
    ("Instance service uses session validation rather than historical logs", () =>
    {
        using var log = new LogFixture();
        var constructor = typeof(InstanceInviterService).GetConstructor(new[] { typeof(string), typeof(Func<DateTime?>), typeof(Func<DateTime>) });
        Check(constructor != null, "Instance service has no validated session source");
        var service = (InstanceInviterService)constructor!.Invoke(new object[] { log.Directory, (Func<DateTime?>)(() => null), (Func<DateTime>)(() => log.Now) });
        Check(service.GetCurrentInstance() == null, "Offline logs must not reach workflow");
        return Task.CompletedTask;
    }),
    ("Confirmation cannot mutate and retarget the pinned destination", async () =>
    {
        var f = new Fixture();
        f.OnConfirm = () => f.Destination!.InstanceId = "456";
        await f.Workflow.RunAsync(new[] { "a" }, CancellationToken.None);
        Check(f.Sent.Count == 0, "Mutable destination was retargeted after confirmation");
    }),
    ("Destination is checked at the HTTP boundary", ApiTests.GuardPreventsHttpRequest),
    ("HTTP guard refusal aborts the remaining batch as unsent", async () =>
    {
        var f = new Fixture();
        f.Send = _ => throw new InstanceInviteNotSentException();
        var report = await f.Workflow.RunAsync(new[] { "a", "b" }, CancellationToken.None);
        Check(f.Sent.Count == 1 && report.Outcomes.All(o => o.Status == InstanceInviteStatus.NotSent), "Guard refusal must not be unknown or allow later sends");
    }),
    ("Duplicate recipient IDs are sent only once", async () =>
    {
        var f = new Fixture();
        var report = await f.Workflow.RunAsync(new[] { "a", "a" }, CancellationToken.None);
        Check(f.Sent.Count == 1 && report.Outcomes.Count == 1 && f.ConfirmedCount == 1, "Duplicate friends must not cause duplicate invitations");
    }),
    ("Lost session reader preserves completed outcomes and aborts remaining recipients", async () =>
    {
        var f = new Fixture();
        f.Send = _ => { f.Read = () => throw new IOException("private log path"); return Task.FromResult(true); };
        var report = await f.Workflow.RunAsync(new[] { "a", "b" }, CancellationToken.None);
        Check(report.Outcomes[0].Status == InstanceInviteStatus.Sent && report.Outcomes[1].Status == InstanceInviteStatus.NotSent, "Lost session must retain completed outcomes");
    }),
    ("Pre-cancelled batches never prompt for confirmation", async () =>
    {
        var f = new Fixture();
        var report = await f.Workflow.RunAsync(new[] { "a" }, new CancellationToken(true));
        Check(f.Confirmations == 0 && f.Sent.Count == 0 && report.Outcomes.Single().Status == InstanceInviteStatus.NotSent, "Cancelled batch still prompted or sent");
    }),
    ("HTTP payload preserves instance modifiers and transport failure semantics", ApiTests.PayloadAndFailureSemantics),
    ("Missing destination never prompts or sends", async () =>
    {
        var f = new Fixture { Destination = null };
        var report = await f.Workflow.RunAsync(new[] { "a" }, CancellationToken.None);
        Check(f.Confirmations == 0 && f.Sent.Count == 0 && report.Outcomes.Single().Status == InstanceInviteStatus.NotSent, "Missing instance must fail closed");
    }),
    ("Declined confirmation never sends", async () =>
    {
        var f = new Fixture { Confirm = false };
        var report = await f.Workflow.RunAsync(new[] { "a" }, CancellationToken.None);
        Check(f.Confirmations == 1 && f.Sent.Count == 0 && report.Outcomes.Single().Status == InstanceInviteStatus.NotSent, "Declined confirmation sent an invite");
    }),
    ("Instance change during a send aborts the next recipient", async () =>
    {
        var f = new Fixture();
        f.Send = _ => { f.Destination = Fixture.Location("456"); return Task.FromResult(true); };
        var report = await f.Workflow.RunAsync(new[] { "a", "b" }, CancellationToken.None);
        Check(f.Sent.SequenceEqual(new[] { "a" }) && report.Outcomes[1].Status == InstanceInviteStatus.NotSent, "Instance change must stop remaining sends");
    }),
    ("Friend Manager invites only selected friends without a configured group", ViewModelTests.SelectedFriendsWithoutGroup),
    ("Production workflow requires auth and rechecks at dispatch", ApiTests.ProductionWorkflowChecksAuthAndFinalDestination),
    ("Busy instance batch blocks other Friend Manager actions", ViewModelTests.BusyBatchBlocksActions),
    ("Friend Manager shows per-friend results without retrying uncertain sends", ViewModelTests.PerFriendOutcomes),
    ("Compiled Friend Manager exposes both destinations and cancel through real DI", UiTests.WiredViewAndDependencyInjection),
};
tests.AddRange(AccountPinTests.All());
tests.AddRange(DetectionTests.All());
tests.AddRange(LiveMarkerTests.All());
int failed = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Count - failed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class Fixture
{
    public CurrentInstanceInfo? Destination = Location("123");
    public List<string> Sent = new();
    public int Confirmations, ConfirmedCount;
    public Action? OnConfirm;
    public bool Confirm = true;
    public Func<string, Task<bool>> Send = _ => Task.FromResult(true);
    public Func<CancellationToken, Task> Pace = _ => Task.CompletedTask;
    public Func<CurrentInstanceInfo?>? Read;
    public CurrentInstanceInviteWorkflow Workflow { get; }
    public Fixture()
    {
        Workflow = new(() => Read == null ? Destination : Read(),
            (user, world, instance, token) => { Sent.Add(user); return Send(user); },
            (destination, count) => { Confirmations++; ConfirmedCount = count; OnConfirm?.Invoke(); return Confirm; },
            token => Pace(token));
    }
    public static CurrentInstanceInfo Location(string instance) => new()
    {
        WorldId = "wrld_00000000-0000-0000-0000-000000000001", InstanceId = instance,
        FullLocation = "wrld_00000000-0000-0000-0000-000000000001:" + instance
    };
}

