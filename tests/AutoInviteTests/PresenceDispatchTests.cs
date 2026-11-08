using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Reflection;
using VRCGroupTools.Services;

static class PresenceDispatchTests
{
    const string Second = "usr_88888888-8888-8888-8888-888888888888";
    static void Check(bool ok, string message) => PresenceTests.Check(ok, message);
    public static IEnumerable<(string, Func<Task>)> All()
    {
        yield return ("Actual presence HTTP starts remain 13 seconds apart after delayed shared limiter and distinct candidate", async () =>
        {
            using var f = new Transport();
            var context = new AutoGroupInviteContext("g", "a", 1, "client", "world", "instance");
            var clock = DateTimeOffset.UtcNow;
            var corroborator = new AutomaticPresenceCorroborator(context, () => clock);
            // Force a real shared-limiter wait, then admit another candidate at the contractual boundary.
            typeof(VRChatApiService).GetField("_lastRequestTime", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(f.Api, DateTime.Now.AddMilliseconds(400));
            var began = Stopwatch.GetTimestamp();
            await corroborator.ResolveAsync(context, PresenceTests.User, f.Api, () => true);
            var remaining = TimeSpan.FromSeconds(13) - Stopwatch.GetElapsedTime(began);
            if (remaining > TimeSpan.Zero) await Task.Delay(remaining);
            clock = clock.AddSeconds(13);
            await corroborator.ResolveAsync(context, Second, f.Api, () => true);
            Check(f.Starts.Count == 2, "Distinct candidate did not reach read transport");
            var interval = Stopwatch.GetElapsedTime(f.Starts[0], f.Starts[1]);
            Console.WriteLine($"Actual presence HTTP interval: {interval.TotalSeconds:F6}s");
            Check(interval >= TimeSpan.FromSeconds(13), "Shared limiter compressed actual outbound starts below 13 seconds");
        });
        yield return ("Concurrent presence reads serialize starts without holding gate for network response", async () =>
        {
            using var f = new Transport { HoldFirst = true };
            var first = f.Api.GetUserPresenceAsync(PresenceTests.User, () => true, CancellationToken.None);
            await f.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second = f.Api.GetUserPresenceAsync(Second, () => true, CancellationToken.None);
            await second.WaitAsync(TimeSpan.FromSeconds(16));
            Check(!first.IsCompleted && f.Starts.Count == 2, "Gate held across HTTP response or concurrent dispatch lost");
            Check(Stopwatch.GetElapsedTime(f.Starts[0], f.Starts[1]) >= TimeSpan.FromSeconds(13), "Concurrent starts bypassed pacing");
            f.Release.TrySetResult(true); await first;
        });
        yield return ("Cancellation while queued or inside shared limiter does not strand dispatch gate", async () =>
        {
            using var f = new Transport();
            typeof(VRChatApiService).GetField("_lastRequestTime", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(f.Api, DateTime.Now.AddMilliseconds(400));
            using var ownerCancel = new CancellationTokenSource();
            using var queuedCancel = new CancellationTokenSource();
            var owner = f.Api.GetUserPresenceAsync(PresenceTests.User, () => true, ownerCancel.Token);
            var queued = f.Api.GetUserPresenceAsync(Second, () => true, queuedCancel.Token);
            queuedCancel.Cancel(); await queued.WaitAsync(TimeSpan.FromSeconds(2));
            ownerCancel.Cancel(); await owner.WaitAsync(TimeSpan.FromSeconds(2));
            Check(f.Starts.Count == 0, "Cancelled queued/shared-limiter request dispatched");
            await f.Api.GetUserPresenceAsync(Second, () => true, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));
            Check(f.Starts.Count == 1, "Cancellation stranded the dispatch gate");
        });
        foreach (var change in new[] { "cancel", "guard", "login", "account" })
        {
            var captured = change;
            yield return ("Presence pacing revalidates immediately at dispatch: " + captured, async () =>
            {
                using var f = new Transport();
                await f.Api.GetUserPresenceAsync(PresenceTests.User, () => true, CancellationToken.None);
                using var cancel = new CancellationTokenSource();
                var valid = true;
                var second = f.Api.GetUserPresenceAsync(Second, () => valid, cancel.Token);
                Check(!second.IsCompleted, "Second lookup did not wait at dispatch boundary");
                if (captured == "cancel") cancel.Cancel();
                if (captured == "guard") { await Task.Delay(200); valid = false; } // revoke after shared limiter, while monotonic pacing is active
                if (captured == "login") typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(f.Api, true);
                if (captured == "account") typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(f.Api, Second);
                // Cancellation must release any owned gate promptly; guard/context changes must still be checked after waits.
                if (captured == "cancel") await second.WaitAsync(TimeSpan.FromSeconds(2));
                else await second.WaitAsync(TimeSpan.FromSeconds(16));
                Check(f.Starts.Count == 1 && await second == null, "Revoked presence read reached outbound HTTP");
                using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
                await f.Api.GetUserPresenceAsync(Second, () => true, cancelled.Token).WaitAsync(TimeSpan.FromSeconds(2));
            });
        }
    }
    sealed class Transport : HttpMessageHandler
    {
        public readonly List<long> Starts = new();
        public readonly TaskCompletionSource<bool> FirstStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource<bool> Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool HoldFirst;
        public VRChatApiService Api;
        public Transport()
        {
            Api = new(new HttpClient(this) { BaseAddress = new Uri("https://offline.invalid/") });
            typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(Api, true);
            typeof(VRChatApiService).GetProperty("CurrentUserId")!.SetValue(Api, PresenceTests.User);
        }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Check(request.Method == HttpMethod.Get, "Write forbidden");
            Starts.Add(Stopwatch.GetTimestamp()); FirstStarted.TrySetResult(true);
            if (HoldFirst && Starts.Count == 1) await Release.Task.WaitAsync(token);
            return new(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
        protected override void Dispose(bool disposing) { Release.TrySetResult(true); base.Dispose(disposing); }
    }
}
