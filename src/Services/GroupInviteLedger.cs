using Microsoft.Data.Sqlite;
using System.IO;

namespace VRCGroupTools.Services;

public enum GroupInviteState { None, InFlight, Sent, Unknown, Failed, NotSent }
public sealed record GroupInviteRecord(string GroupId, string UserId, GroupInviteState State, string UpdatedUtc);
public sealed class GroupInviteSuppressedException : Exception
{
    public GroupInviteState State { get; }
    public GroupInviteSuppressedException(GroupInviteState state) : base($"Group invite: {state}. Local duplicate protection; no automatic retry. Unknown may have been sent; InFlight may still be pending or interrupted. Check VRChat before taking further action.") { State = state; }
}

/// <summary>Group-wide local ledger, intentionally shared across accounts: changing sender must not re-invite a recipient.
/// Atomic SQLite claims survive crashes. An orphaned InFlight is deliberately never retried automatically.</summary>
public sealed class GroupInviteLedger
{
    private readonly string _connectionString;
    public GroupInviteLedger(string? databasePath = null)
    {
        databasePath ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VRCGroupTools", "vrcgrouptools.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, DefaultTimeout = 30 }.ToString();
    }
    private async Task<SqliteConnection> OpenAsync()
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS GroupInviteLedger (GroupId TEXT NOT NULL, UserId TEXT NOT NULL, State TEXT NOT NULL, UpdatedUtc TEXT NOT NULL, PRIMARY KEY(GroupId,UserId))";
        await command.ExecuteNonQueryAsync();
        return connection;
    }
    public Task<bool> TryClaimAsync(string groupId, string userId) => ClaimAsync(groupId, userId, true);
    public Task<bool> TryClaimAutomaticAsync(string groupId, string userId) => ClaimAsync(groupId, userId, false);
    private async Task<bool> ClaimAsync(string groupId, string userId, bool manual)
    {
        using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO GroupInviteLedger VALUES ($g,$u,'InFlight',$t) ON CONFLICT(GroupId,UserId) DO UPDATE SET State='InFlight',UpdatedUtc=$t WHERE GroupInviteLedger.State " + (manual ? "IN ('Failed','NotSent')" : "= 'NotSent'");
        Parameters(command, groupId, userId);
        return await command.ExecuteNonQueryAsync() == 1;
    }
    public async Task CompleteAsync(string groupId, string userId, GroupInviteState state)
    {
        if (state is not (GroupInviteState.Sent or GroupInviteState.Unknown or GroupInviteState.Failed or GroupInviteState.NotSent)) throw new ArgumentException("Invalid completion state");
        using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE GroupInviteLedger SET State=$s,UpdatedUtc=$t WHERE GroupId=$g AND UserId=$u AND State='InFlight'";
        Parameters(command, groupId, userId);
        command.Parameters.AddWithValue("$s", state.ToString());
        await command.ExecuteNonQueryAsync();
    }
    public async Task<List<GroupInviteRecord>> ReadAsync(string groupId)
    {
        using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT UserId,State,UpdatedUtc FROM GroupInviteLedger WHERE GroupId=$g ORDER BY UpdatedUtc DESC";
        command.Parameters.AddWithValue("$g", groupId);
        using var reader = await command.ExecuteReaderAsync();
        var records = new List<GroupInviteRecord>();
        while (await reader.ReadAsync()) records.Add(new(groupId, reader.GetString(0), Enum.Parse<GroupInviteState>(reader.GetString(1)), reader.GetString(2)));
        return records;
    }
    public async Task<GroupInviteState> GetStateAsync(string groupId, string userId)
    {
        using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT State FROM GroupInviteLedger WHERE GroupId=$g AND UserId=$u";
        command.Parameters.AddWithValue("$g", groupId);
        command.Parameters.AddWithValue("$u", userId);
        var state = await command.ExecuteScalarAsync() as string;
        return state == null ? GroupInviteState.None : Enum.Parse<GroupInviteState>(state);
    }
    public async Task SaveMembershipAsync(GroupMembershipSnapshot snapshot)
    {
        using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS GroupMembershipSnapshots (GroupId TEXT PRIMARY KEY, Snapshot TEXT NOT NULL); INSERT INTO GroupMembershipSnapshots VALUES ($g,$s) ON CONFLICT(GroupId) DO UPDATE SET Snapshot=$s";
        command.Parameters.AddWithValue("$g", snapshot.GroupId);
        command.Parameters.AddWithValue("$s", System.Text.Json.JsonSerializer.Serialize(snapshot));
        await command.ExecuteNonQueryAsync();
    }
    public async Task<GroupMembershipSnapshot?> ReadMembershipAsync(string groupId)
    {
        using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS GroupMembershipSnapshots (GroupId TEXT PRIMARY KEY, Snapshot TEXT NOT NULL); SELECT Snapshot FROM GroupMembershipSnapshots WHERE GroupId=$g";
        command.Parameters.AddWithValue("$g", groupId);
        var json = await command.ExecuteScalarAsync() as string;
        return json == null ? null : System.Text.Json.JsonSerializer.Deserialize<GroupMembershipSnapshot>(json);
    }
    private static void Parameters(SqliteCommand command, string groupId, string userId)
    {
        command.Parameters.AddWithValue("$g", groupId);
        command.Parameters.AddWithValue("$u", userId);
        command.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
    }
}
