using System.IO;
using VRCGroupTools.Services;

static class DetectionTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All()
    {
        yield return ("Current-process completed join survives five-megabyte tail loss on first refresh", () =>
        {
            using var f = new DetectionLog();
            File.AppendAllText(f.File, string.Concat(Enumerable.Repeat("ordinary world output without session events\n", 125_000)));
            // Busy-room fixture: timestamped Behaviour player envelope is supported by pinned VRCX
            // LogWatcher 30e00095b817117660180db96865e158e07d5c0a, ParseLogOnPlayerJoinedOrLeft.
            File.AppendAllText(f.File, string.Concat(Enumerable.Range(1, 30).Select(i =>
                $"{f.Now.ToLocalTime():yyyy.MM.dd HH:mm:ss} Log        -  [Behaviour] OnPlayerJoined Fixture{i} (usr_22222222-2222-2222-2222-{i:000000000000})\n")));
            System.IO.File.SetLastWriteTimeUtc(f.File, f.Now);
            var service = f.Service();
            Check(service.GetCurrentInstance()?.FullLocation == f.Location, "First refresh lost a completed current-process join outside the last 5MB");
            Check(service.GetLocalUsers().Count == 30, "Fresh busy-room activity must not lose the baseline or its 30 visible fixture users");
            Check(CurrentInstanceSessionReader.Read(f.Dir, f.Start, f.Now)?.FullLocation == f.Location, "Snapshot consumer disagrees with service");
            File.AppendAllText(f.File, "[Behaviour] OnDisconnected\n");
            Check(service.GetCurrentInstance() == null, "Large log reused location after disconnect");
            return Task.CompletedTask;
        });
        yield return ("Quiet current-process log is unknown with an actionable privacy-safe reason, not offline", () =>
        {
            using var f = new DetectionLog();
            File.SetLastWriteTimeUtc(f.File, f.Now.AddMinutes(-3));
            var service = f.Service();
            Check(service.GetCurrentInstance() == null, "Quiet logs must not bypass freshness or establish historical presence");
            CheckReason(service, "LogNotFresh");
            var settings = new AppSettings();
            using var vm = new VRCGroupTools.ViewModels.InstanceInviterViewModel(
                StubProxy.Create<IVRChatApiService>(),
                StubProxy.Create<ISettingsService>((m, _) => m.Name == "get_Settings" ? settings : null), service,
                StubProxy.Create<ICacheService>(), new GroupInviteLedger(Path.Combine(f.Dir, "fixture.db")));
            typeof(VRCGroupTools.ViewModels.InstanceInviterViewModel).GetMethod("UpdateCurrentInstance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(vm, null);
            Check(vm.StatusMessage != "Not in an instance", "Refresh falsely declares the user offline for a quiet log");
            Check(vm.CurrentInstanceDisplay?.Contains("LogNotFresh") == true, "UI must expose the safe diagnostic code");
            Check(!vm.CurrentInstanceDisplay!.Contains(f.Location) && !vm.CurrentInstanceDisplay.Contains(f.Dir), "Diagnostic leaked location or local user path");
            return Task.CompletedTask;
        });
        yield return ("Process access failure is distinguishable from no running client and contains no exception details", () =>
        {
            using var f = new DetectionLog();
            var service = new InstanceInviterService(f.Dir, () => throw new UnauthorizedAccessException("PRIVATE_FIXTURE"), () => f.Now);
            Check(service.GetCurrentInstance() == null, "Unreadable process must fail closed");
            CheckReason(service, "ProcessInspectionFailed");
            Check(!Diagnostic(service).ToString()!.Contains("PRIVATE_FIXTURE"), "Exception details leaked");
            return Task.CompletedTask;
        });
        foreach (var (marker, code) in new[] {
            ("OnLeftRoom", "LeftOrDisconnected"), ("OnDisconnected", "LeftOrDisconnected"),
            ("Joining or Creating Room: pending", "JoinIncomplete"),
            ("Joining wrld_broken", "LocationUnrecognized"),
            ("Joining wrld_11111111-1111-1111-1111-111111111111:456", "JoinIncomplete") })
        {
            yield return ("Large-log latest boundary stays closed: " + code + " / " + marker.Split(' ')[0], () =>
            {
                using var f = new DetectionLog();
                File.AppendAllText(f.File, "[Behaviour] " + marker + "\n" + string.Concat(Enumerable.Repeat("ordinary output\n", 350_000)));
                File.SetLastWriteTimeUtc(f.File, f.Now);
                var service = f.Service();
                Check(service.GetCurrentInstance() == null, "Historical completed room used past a later boundary");
                Check(CurrentInstanceSessionReader.Read(f.Dir, f.Start, f.Now) == null, "Snapshot used historical room");
                CheckReason(service, code);
                return Task.CompletedTask;
            });
        }
        yield return ("Oversized appended chunk cannot bypass the per-read scan budget", () =>
        {
            using var f = new DetectionLog();var service = f.Service();
            Check(service.GetCurrentInstance() != null, "Positive control missing");
            using (var stream = new FileStream(f.File, FileMode.Open, FileAccess.Write)) stream.SetLength(65_000_000);
            File.SetLastWriteTimeUtc(f.File, f.Now);
            Check(service.GetCurrentInstance() == null, "An oversized appended chunk bypassed bounded scanning");CheckReason(service, "LogScanLimit");
            return Task.CompletedTask;
        });
        yield return ("Bounded scan never falls back to a historical tail", () =>
        {
            using var f = new DetectionLog();
            using (var stream = new FileStream(f.File, FileMode.Open, FileAccess.Write)) stream.SetLength(64_000_001);
            File.SetLastWriteTimeUtc(f.File, f.Now);
            var service = f.Service();
            Check(service.GetCurrentInstance() == null, "Oversized baseline must be unavailable");
            CheckReason(service, "LogScanLimit");
            Check(CurrentInstanceSessionReader.Read(f.Dir, f.Start, f.Now) == null, "Snapshot scan bypassed size bound");
            return Task.CompletedTask;
        });
        yield return ("Process change, rotation and truncation never retain a completed historical room", () =>
        {
            using var f = new DetectionLog();
            DateTime? start = f.Start;
            var service = new InstanceInviterService(f.Dir, () => start, () => f.Now);
            Check(service.GetCurrentInstance() != null, "Positive control missing");
            start = f.Now.AddSeconds(10);
            Check(service.GetCurrentInstance() == null, "Future/new process reused completed room");
            CheckReason(service, "ClockMismatch");
            start = f.Start;
            Check(service.GetCurrentInstance() != null, "Process restore positive missing");
            File.WriteAllText(f.File, "ordinary output\n");File.SetLastWriteTimeUtc(f.File, f.Now);
            Check(service.GetCurrentInstance() == null, "Truncation retained historical room");
            CheckReason(service, "JoinNotObserved");
            File.Delete(f.File);
            Check(service.GetCurrentInstance() == null, "Deleted file retained room");CheckReason(service, "LogMissing");
            File.WriteAllText(f.File, "[Behaviour] OnJoinedRoom\n");File.SetLastWriteTimeUtc(f.File, f.Now);
            Check(service.GetCurrentInstance() == null, "Rotation with orphan completion reused historical room");
            start = null;
            Check(service.GetCurrentInstance() == null, "Missing client retained room");CheckReason(service, "ProcessUnavailable");
            return Task.CompletedTask;
        });
        yield return ("Quiet rejection requires a newly established consent identity after fresh activity", () =>
        {
            using var f = new DetectionLog();var service = f.Service();
            var first = service.GetCurrentInstance();Check(first != null, "Positive control missing");
            File.SetLastWriteTimeUtc(f.File, f.Now.AddMinutes(-3));
            Check(service.GetCurrentInstance() == null, "Quiet log bypassed freshness");CheckReason(service, "LogNotFresh");
            File.SetLastWriteTimeUtc(f.File, f.Now);
            var next = service.GetCurrentInstance();
            Check(next != null && first!.SessionKey != next.SessionKey, "Unavailable interval retained automatic consent session");
            return Task.CompletedTask;
        });
        yield return ("Missing folder and old-session log are distinct and never fall back", () =>
        {
            using var f = new DetectionLog();
            var missing = new InstanceInviterService(Path.Combine(f.Dir, "absent"), () => f.Start, () => f.Now);
            Check(missing.GetCurrentInstance() == null, "Missing directory detected a room");CheckReason(missing, "LogDirectoryMissing");
            var service = new InstanceInviterService(f.Dir, () => f.Now.AddSeconds(-1), () => f.Now);
            File.SetCreationTimeUtc(f.File, f.Now.AddHours(-1));
            Check(service.GetCurrentInstance() == null, "Prior process log was used");CheckReason(service, "LogSessionMismatch");
            return Task.CompletedTask;
        });
    }
    static object Diagnostic(InstanceInviterService service)
    {
        var property = typeof(InstanceInviterService).GetProperty("DetectionDiagnostic");
        Check(property != null, "Missing actionable detection diagnostic; all nulls are collapsed into not in an instance");
        return property!.GetValue(service)!;
    }
    static void CheckReason(InstanceInviterService service, string expected)
    {
        var diagnostic = Diagnostic(service);
        Check(diagnostic.GetType().GetProperty("Code")!.GetValue(diagnostic)!.ToString() == expected, "Unexpected detection reason; expected " + expected);
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}

sealed class DetectionLog : IDisposable
{
    public string Dir = Path.Combine(Environment.GetEnvironmentVariable("TMPDIR") ?? Path.GetTempPath(), "vrcgt-detection-" + Guid.NewGuid());
    public string File => Path.Combine(Dir, "output_log_fixture.txt");
    public readonly DateTime Now = DateTime.UtcNow;
    public DateTime Start => Now.AddMinutes(-30);
    public string Location => "wrld_11111111-1111-1111-1111-111111111111:123~nonce(PRIVATE_FIXTURE)";
    public DetectionLog()
    {
        Directory.CreateDirectory(Dir);
        System.IO.File.WriteAllText(File, $"[Behaviour] Joining {Location}\n[Behaviour] OnJoinedRoom\n");
        System.IO.File.SetLastWriteTimeUtc(File, Now);
    }
    public InstanceInviterService Service() => new(Dir, () => Start, () => Now);
    public void Dispose() => Directory.Delete(Dir, true);
}
