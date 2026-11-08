using System.Text.Json;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools.Services;

public sealed partial class GroupInviteLedger
{
    // Discovery only, never persistent authorisation. Reuses the shared local DB, not contacts.
    public const int EncounterLimit = 1000;
    public async Task SaveEncountersAsync(List<SeenUser> users)
    {
        using var connection = await OpenAsync();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS GroupInviteEncounters (Identity TEXT PRIMARY KEY, Record TEXT NOT NULL, UpdatedUtc TEXT NOT NULL)";
        await command.ExecuteNonQueryAsync();
        foreach (var user in users.Take(EncounterLimit))
        {
            // Unresolved names remain visible for review but can never authorise a send.
            var key = ApprovalPublisher.Id(user.UserId, "usr") ? user.UserId : "name:" + user.DisplayName;
            command.CommandText = "INSERT INTO GroupInviteEncounters VALUES ($i,$r,$t) ON CONFLICT(Identity) DO UPDATE SET Record=$r,UpdatedUtc=$t";
            command.Parameters.Clear(); command.Parameters.AddWithValue("$i", key);
            command.Parameters.AddWithValue("$r", JsonSerializer.Serialize(user)); command.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        }
        command.Parameters.Clear();
        command.CommandText = $"DELETE FROM GroupInviteEncounters WHERE Identity NOT IN (SELECT Identity FROM GroupInviteEncounters ORDER BY UpdatedUtc DESC,Identity LIMIT {EncounterLimit})";
        await command.ExecuteNonQueryAsync(); transaction.Commit();
    }
    public async Task<List<SeenUser>> ReadEncountersAsync()
    {
        using var connection = await OpenAsync(); using var command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE IF NOT EXISTS GroupInviteEncounters (Identity TEXT PRIMARY KEY, Record TEXT NOT NULL, UpdatedUtc TEXT NOT NULL); SELECT Record FROM GroupInviteEncounters ORDER BY UpdatedUtc DESC,Identity LIMIT {EncounterLimit}";
        using var reader = await command.ExecuteReaderAsync(); var users = new List<SeenUser>();
        while (await reader.ReadAsync())
        {
            var user = JsonSerializer.Deserialize<SeenUser>(reader.GetString(0)); if (user != null) users.Add(user);
        }
        return users;
    }
}
