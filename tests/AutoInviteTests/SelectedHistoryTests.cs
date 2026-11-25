using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using VRCGroupTools.Services;

static class SelectedHistoryTests
{
    private const string Group = "grp_11111111-1111-1111-1111-111111111111";
    private const string Other = "usr_88888888-8888-8888-8888-888888888888";
    private const string Future = "usr_77777777-7777-7777-7777-777777777777";

    public static IEnumerable<(string, Func<Task>)> All()
    {
        foreach (var (body, status, canSend) in new[]
        {
            ("null", HttpStatusCode.OK, true),
            ($"{{\"userId\":\"{PresenceTests.User}\",\"membershipStatus\":\"banned\"}}", HttpStatusCode.OK, false),
            ("{}", HttpStatusCode.OK, false),
            ("{}", HttpStatusCode.Forbidden, false),
        })
        {
            yield return ($"Selected historical user checks incomplete membership before manual invite ({body}, {status})", async () =>
            {
                using var f = new PresenceTests.Harness(body);
                f.Vm.StopAutoInvite();
                f.Running = false;
                var ledger = new GroupInviteLedger(f.Temp.Db);
                await ledger.SaveMembershipAsync(new(Group, new(), DateTime.UtcNow, false));
                await f.Vm.LoadCommand.ExecuteAsync(null);
                var row = f.Vm.FilteredInstanceUsers.Single();
                PresenceTests.Check(row.CanInvite && f.Handler.Reads == 0 && f.Handler.Writes == 0, "Review must allow a pending identity with no transfer");
                row.IsSelected = true;
                await f.Vm.RefreshCommand.ExecuteAsync(null);
                PresenceTests.Check(f.Vm.FilteredInstanceUsers.Single() == row && row.IsSelected && f.Vm.HasSelectedInstanceUsers, "Refresh lost the checked recipient");
                f.Vm.ConfirmHistoryInvites = (group, users) => group == Group && users.Single().Contains(PresenceTests.User);
                f.Handler.ReadStatus = status;
                f.Handler.AllowWrites = canSend;
                await f.Vm.InviteInstanceUsersCommand.ExecuteAsync(null);
                PresenceTests.Check(f.Handler.Reads == 1 && f.Handler.Writes == (canSend ? 1 : 0), "Membership preflight or manual dispatch mismatch");
                if (canSend)
                {
                    PresenceTests.Check(!row.CanInvite && await ledger.GetStateAsync(Group, PresenceTests.User) == GroupInviteState.Sent, "Sent recipient remained selectable");
                    await f.Vm.InviteInstanceUsersCommand.ExecuteAsync(null);
                    PresenceTests.Check(f.Handler.Writes == 1, "Duplicate manual invite escaped the ledger");
                }
            });
        }
        foreach (var (outcome, response, state) in new (string, HttpStatusCode?, GroupInviteState)[]
        {
            ("Sent", HttpStatusCode.OK, GroupInviteState.Sent),
            ("Rejected", HttpStatusCode.Forbidden, GroupInviteState.Failed),
            ("Unknown", HttpStatusCode.InternalServerError, GroupInviteState.Unknown),
            ("Unknown", null, GroupInviteState.Unknown),
            ("Skipped", null, GroupInviteState.None),
            ("AlreadySent", null, GroupInviteState.Sent)
        })
        {
            yield return ($"Manual Select All console reports {outcome} ({response?.ToString() ?? "no HTTP response"})",
                () => CheckManualConsoleAsync(outcome, response, state));
        }
        yield return ("Group Inviter loads local history with no group, account, or running game", async () =>
        {
            using var f = new PresenceTests.Harness("null");
            f.Vm.StopAutoInvite();
            f.Running = false;
            f.Settings.GroupId = "";
            typeof(VRChatApiService).GetProperty("IsLoggedIn")!.SetValue(f.Api, false);
            await f.Vm.LoadCommand.ExecuteAsync(null);
            var row = f.Vm.FilteredInstanceUsers.Single();
            PresenceTests.Check(row.User.UserId == PresenceTests.User && !row.CanInvite && f.Handler.Reads == 0 && f.Handler.Writes == 0,
                "Browsing historical logs depended on a current group, login, or current instance");
        });
        yield return ("Manual confirmation cannot carry a checked encounter across groups", async () =>
        {
            using var f = new PresenceTests.Harness("null");
            f.Vm.StopAutoInvite();
            await f.Vm.LoadCommand.ExecuteAsync(null);
            f.Vm.FilteredInstanceUsers.Single().IsSelected = true;
            f.Vm.ConfirmHistoryInvites = (_, _) => { f.Settings.GroupId = "grp_22222222-2222-2222-2222-222222222222"; return true; };
            f.Handler.AllowWrites = false;
            await f.Vm.InviteInstanceUsersCommand.ExecuteAsync(null);
            PresenceTests.Check(f.Handler.Reads == 0 && f.Handler.Writes == 0, "Changed group reached the transport");
        });
        yield return ("Checked-only Auto Invite excludes unchecked and future users from every instance", async () =>
        {
            using var f = new PresenceTests.Harness(PresenceTests.Json(PresenceTests.User, "offline"));
            f.Vm.StopAutoInvite();
            f.Running = false;
            File.AppendAllText(f.Temp.Log, $"[Behaviour] OnPlayerJoined Other ({Other})\n");
            await f.Vm.LoadCommand.ExecuteAsync(null);
            f.Vm.FilteredInstanceUsers.Single(row => row.User.UserId == PresenceTests.User).IsSelected = true;
            var confirms = 0;
            f.Vm.ConfirmAutoInvite = (_, review) =>
            {
                PresenceTests.Check(review.Contains(PresenceTests.User) && !review.Contains(Other) && review.Contains("1 selected exact IDs") && review.Contains("encounters: False"), "Confirmation broadened selected scope");
                confirms++;
                return true;
            };
            f.Vm.AutoInviteEnabled = true;
            var session = await PresenceTests.WaitForSessionAsync(f.Vm);
            File.AppendAllText(f.Temp.Log, $"[Behaviour] OnPlayerJoined New ({Future})\n");
            await session.TickAsync();
            await session.PendingSend;
            var ledger = new GroupInviteLedger(f.Temp.Db);
            PresenceTests.Check(confirms == 1 && f.Handler.Writes == 1 && await ledger.GetStateAsync(Group, PresenceTests.User) == GroupInviteState.Sent, "Checked recipient did not receive the invite");
            var added = f.Vm.FilteredInstanceUsers.Single(row => row.User.UserId == Future);
            PresenceTests.Check(!await (Task<bool>)f.Invoke("IsAutoEligibleAsync", added.User)! &&
                await ledger.GetStateAsync(Group, Other) == GroupInviteState.None &&
                await ledger.GetStateAsync(Group, Future) == GroupInviteState.None, "Unchecked or future recipient was admitted to the queue");
            PresenceTests.Check(f.Vm.AutoInviteEnabled, "Marking a sent recipient deselected cancelled other queued selections");
            f.Vm.FilteredInstanceUsers.Single(row => row.User.UserId == Other).IsSelected = true;
            PresenceTests.Check(!session.IsRunning && !f.Vm.AutoInviteEnabled, "Editing the selected recipients left the old queue active");
        });
    }

    private static async Task CheckManualConsoleAsync(string outcome, HttpStatusCode? response, GroupInviteState expectedState)
    {
        const string responseSecret = "private-fixture-response-body";
        const string diagnosticSecret = "private-fixture-http-exception";
        const string forgedLine = "FORGED-CONSOLE-ENTRY";
        var id = PresenceTests.User;
        var membership = outcome == "Skipped" ? $"{{\"userId\":\"{id}\",\"membershipStatus\":\"banned\"}}" : "null";
        using var f = new PresenceTests.Harness(membership);
        f.Vm.StopAutoInvite();
        f.Running = false;
        var ledger = new GroupInviteLedger(f.Temp.Db);
        await ledger.SaveMembershipAsync(new(Group, new(), DateTime.UtcNow, false));
        await ledger.SaveEncountersAsync(new() { new() { UserId = id, DisplayName = $"Fixture\r\n{forgedLine}", LastSeenAt = DateTime.Now } });
        File.AppendAllText(f.Temp.Log, "[Behaviour] OnPlayerJoined Unresolved name\n");
        await f.Vm.LoadCommand.ExecuteAsync(null);

        using var capture = new StringWriter();
        string requestBody = "", requestSecret = "", beforeResponse = "";
        f.Handler.WriteResponse = async (request, token) =>
        {
            beforeResponse = capture.ToString();
            requestBody = await request.Content!.ReadAsStringAsync(token);
            requestSecret = request.RequestUri!.Query.Split('=', 2)[1];
            if (response == null) throw new HttpRequestException(diagnosticSecret);
            return new HttpResponseMessage(response.Value) { Content = new StringContent(responseSecret) };
        };
        var confirmations = 0;
        f.Vm.ConfirmHistoryInvites = (group, recipients) =>
        {
            confirmations++;
            PresenceTests.Check(group == Group && recipients.Count == 1 && recipients[0].Contains(id),
                "Select All included an unresolved recipient or the wrong group");
            if (outcome == "AlreadySent")
            {
                // Another window completes the same invite while the operator reviews the batch.
                ledger.TryClaimAsync(Group, id).GetAwaiter().GetResult();
                ledger.CompleteAsync(Group, id, GroupInviteState.Sent).GetAwaiter().GetResult();
            }
            return true;
        };

        var original = Console.Out;
        try
        {
            Console.SetOut(capture);
            f.Vm.SelectAllInstanceUsersCommand.Execute(null);
            PresenceTests.Check(f.Vm.InstanceUserSelectedCount == 1, "Select All must select only exact eligible IDs");
            await f.Vm.InviteInstanceUsersCommand.ExecuteAsync(null);
        }
        finally { Console.SetOut(original); }

        var log = capture.ToString();
        var target = $"[1/1] {id}";
        var dispatchExpected = outcome is "Sent" or "Rejected" or "Unknown";
        PresenceTests.Check(confirmations == 1 && f.Handler.Reads == 1 && f.Handler.Writes == (dispatchExpected ? 1 : 0),
            "Membership preflight and HTTP writes disagree with the displayed outcome");
        PresenceTests.Check(await ledger.GetStateAsync(Group, id) == expectedState, "Console result disagrees with durable invite state");
        var start = log.IndexOf("[GROUP-INVITE] Manual batch started", StringComparison.Ordinal);
        var checking = log.IndexOf($"{target} checking membership", StringComparison.Ordinal);
        var result = log.IndexOf($"{target} result={outcome}", StringComparison.Ordinal);
        var end = log.IndexOf("[GROUP-INVITE] Manual batch complete", StringComparison.Ordinal);
        PresenceTests.Check(start >= 0 && checking > start && result > checking && end > result,
            "Missing or out-of-order batch start, membership check, recipient result or summary");
        var summary = log[end..];
        PresenceTests.Check(summary.Contains($"sent={(outcome == "Sent" ? 1 : 0)} ") &&
            summary.Contains($"rejected={(outcome == "Rejected" ? 1 : 0)} ") &&
            summary.Contains($"unknown={(outcome == "Unknown" ? 1 : 0)} ") &&
            summary.Contains($"skipped={(outcome is "Skipped" or "AlreadySent" ? 1 : 0)} ") && summary.Contains("remaining=0"),
            "Console summary counted an outcome incorrectly");
        PresenceTests.Check(outcome == "Sent" || !log.Contains("result=Sent"), "Console falsely reported a successful invitation");
        var apiStatus = $"Invite for {id} returned HTTP";
        if (dispatchExpected)
        {
            PresenceTests.Check(beforeResponse.Contains($"{target} requesting group invitation") && !beforeResponse.Contains("result=")
                && !beforeResponse.Contains("Manual batch complete"), "Real-time progress was missing or announced an outcome before the POST completed");
            using var body = JsonDocument.Parse(requestBody);
            PresenceTests.Check(body.RootElement.GetProperty("userId").GetString() == id, "Transport received a different recipient");
            var expectedApi = response.HasValue ? $"{apiStatus} {(int)response.Value} ({response.Value})" : $"Invite for {id}: Unknown (HttpRequestException)";
            PresenceTests.Check(log.Contains(expectedApi), "Missing sanitized transport result");
            PresenceTests.Check(!log.Contains(requestBody) && !log.Contains(requestSecret), "Console exposed a request body or credential");
        }
        else
        {
            PresenceTests.Check(!log.Contains(apiStatus), "Console reported a POST response for a suppressed or skipped recipient");
        }
        PresenceTests.Check(!log.Contains("offline.invalid") && !log.Contains("apiKey=", StringComparison.OrdinalIgnoreCase) &&
            !log.Contains(responseSecret) && !log.Contains(diagnosticSecret) &&
            !log.Contains("\n" + forgedLine) && !log.Contains("\r" + forgedLine),
            "Console exposed a URL, query credential, raw transport text or a forged entry");
    }

}
