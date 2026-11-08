using System.IO;
using VRCGroupTools.Services;

// Report-derived marker ORDER, not a captured full log. Only marker names/counts and
// timing/size metadata were supplied. All payloads (including URL/name) are synthetic.
// Envelope follows the existing timestamped Behaviour fixtures; no unknown fields invented.
static class LiveMarkerTests
{
    public static IEnumerable<(string Name, Func<Task> Run)> All()
    {
        yield return ("Reported 1.1.21 live marker sequence completes without OnJoinedRoom", () =>
        {
            using var f = new DetectionLog();
            File.WriteAllText(f.File, Sequence(f));
            var service = f.Service();
            Check(service.GetCurrentInstance()?.FullLocation == f.Location, "Reported completed join was discarded (name-only second stage / missing legacy completion)");
            Check(CurrentInstanceSessionReader.Read(f.Dir, f.Start, f.Now)?.FullLocation == f.Location, "Snapshot did not accept report-derived completion");
            return Task.CompletedTask;
        });
        yield return ("Reported marker sequence preserves pending location across incremental polls", () =>
        {
            using var f = new DetectionLog(); var reader = new IncrementalInstanceSessionReader();
            File.WriteAllText(f.File, Line(f, "Entering Room: Synthetic fixture") + Line(f, "Joining " + f.Location));
            Check(reader.Read(f.Dir, f.Start, f.Now) == null, "Pending join completed early");
            File.AppendAllText(f.File, Line(f, "Joining or Creating Room: Synthetic fixture"));
            Check(reader.Read(f.Dir, f.Start, f.Now) == null, "Second stage completed early");
            File.AppendAllText(f.File, Line(f, "Successfully joined room"));
            Check(reader.Read(f.Dir, f.Start, f.Now)?.FullLocation == f.Location, "Second stage discarded the pending address");
            return Task.CompletedTask;
        });
        foreach (var boundary in new[] { "Entering Room: Another fixture", "Joining malformed", "OnLeftRoom", "OnDisconnected" })
            yield return ("Report completion cannot resurrect pending location after " + boundary.Split(' ')[0], () =>
            {
                using var f = new DetectionLog();
                File.WriteAllText(f.File, Sequence(f) + Line(f, "Joining " + f.Location) + Line(f, boundary) + Line(f, "Successfully joined room"));
                CheckBothUnknown(f); return Task.CompletedTask;
            });
        foreach (var suffix in new[] { "Entering Room: Next", "Joining or Creating Room: Next", "Joining malformed", "Joining wrld_22222222-2222-2222-2222-222222222222:456" })
            yield return ("Latest incomplete report transition never falls back: " + suffix.Split(' ')[0], () =>
            {
                using var f = new DetectionLog(); File.WriteAllText(f.File, Sequence(f) + Line(f, suffix));
                CheckBothUnknown(f); return Task.CompletedTask;
            });
        yield return ("Generic completion and name-only stage cannot establish a location", () =>
        {
            using var f = new DetectionLog(); File.WriteAllText(f.File, Line(f, "Joining or Creating Room: Synthetic fixture") + Line(f, "Successfully joined room"));
            CheckBothUnknown(f); return Task.CompletedTask;
        });
        yield return ("Marker substrings inside a world name cannot create transitions", () =>
        {
            using var f = new DetectionLog();
            File.WriteAllText(f.File, Line(f, "Entering Room: Synthetic OnLeftRoom Successfully joined room Joining " + f.Location));
            CheckBothUnknown(f);
            File.WriteAllText(f.File, Line(f, "Joining " + f.Location) + Line(f, "Joining or Creating Room: OnDisconnected OnJoinedRoom Successfully joined room"));
            CheckBothUnknown(f);
            File.AppendAllText(f.File, Line(f, "Successfully joined room"));
            Check(f.Service().GetCurrentInstance()?.FullLocation == f.Location, "World-name marker substrings invalidated a real pending join");
            return Task.CompletedTask;
        });
        yield return ("Report completion after truncation or process change cannot reuse pending state", () =>
        {
            using var f = new DetectionLog(); var reader = new IncrementalInstanceSessionReader();
            File.WriteAllText(f.File, Line(f, "Joining " + f.Location));
            Check(reader.Read(f.Dir, f.Start, f.Now) == null, "Pending joined early");
            File.WriteAllText(f.File, Line(f, "Successfully joined room"));
            Check(reader.Read(f.Dir, f.Start, f.Now) == null, "Truncation kept pending location");
            File.WriteAllText(f.File, Line(f, "Joining " + f.Location));
            Check(reader.Read(f.Dir, f.Start, f.Now) == null, "Pending joined early");
            File.WriteAllText(f.File, Line(f, "Successfully joined room"));
            Check(reader.Read(f.Dir, f.Start.AddSeconds(1), f.Now) == null, "Process change kept pending location");
            return Task.CompletedTask;
        });
    }
    static string Sequence(DetectionLog f) => Line(f, "OnLeftRoom") + Line(f, "OnDisconnected")
        + Line(f, "Entering Room: Synthetic fixture") + Line(f, "Joining " + f.Location)
        + Line(f, "Joining or Creating Room: Synthetic fixture") + Line(f, "Successfully joined room")
        + Line(f, "OnPlayerJoined Synthetic A") + Line(f, "OnPlayerJoined Synthetic B") + Line(f, "OnPlayerJoined Synthetic C");
    static string Line(DetectionLog f, string payload) => $"{f.Now.ToLocalTime():yyyy.MM.dd HH:mm:ss} Log        -  [Behaviour] {payload}\n";
    static void CheckBothUnknown(DetectionLog f)
    {
        Check(f.Service().GetCurrentInstance() == null, "Incremental reader reused a historical or invalid location");
        Check(CurrentInstanceSessionReader.Read(f.Dir, f.Start, f.Now) == null, "Snapshot reader reused a historical or invalid location");
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
}
