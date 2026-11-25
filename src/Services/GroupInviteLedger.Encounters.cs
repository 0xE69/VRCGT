using System.Text.Json;
using Microsoft.Data.Sqlite;
using VRCGroupTools.ViewModels;

namespace VRCGroupTools.Services;

public sealed partial class GroupInviteLedger
{
    private const string CreateEncounters = "CREATE TABLE IF NOT EXISTS GroupInviteEncounters (Identity TEXT PRIMARY KEY, Record TEXT NOT NULL, UpdatedUtc TEXT NOT NULL)";

    public Task SaveEncountersAsync(List<SeenUser> users) => MergeEncountersAsync(users, includeProfile: true);
    internal Task SaveLocalDiscoveriesAsync(List<SeenUser> users) => MergeEncountersAsync(users, includeProfile: false);

    private async Task MergeEncountersAsync(List<SeenUser> users, bool includeProfile)
    {
        if (users.Count == 0) return;
        using var connection = await OpenAsync();
        using var transaction = connection.BeginTransaction();
        using var read = connection.CreateCommand();
        read.Transaction = transaction;
        read.CommandText = CreateEncounters;
        await read.ExecuteNonQueryAsync();
        read.CommandText = "SELECT Record FROM GroupInviteEncounters WHERE Identity = $id";
        var readId = read.Parameters.Add("$id", SqliteType.Text);

        using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = """
            INSERT INTO GroupInviteEncounters(Identity,Record,UpdatedUtc) VALUES ($id,$record,$utc)
            ON CONFLICT(Identity) DO UPDATE SET Record=excluded.Record, UpdatedUtc=excluded.UpdatedUtc
            """;
        var writeId = write.Parameters.Add("$id", SqliteType.Text);
        var writeRecord = write.Parameters.Add("$record", SqliteType.Text);
        write.Parameters.AddWithValue("$utc", DateTime.UtcNow.ToString("O"));
        foreach (var user in users)
        {
            var id = ApprovalPublisher.Id(user.UserId, "usr") ? user.UserId : "name:" + user.DisplayName;
            readId.Value = writeId.Value = id;
            var previous = await read.ExecuteScalarAsync() as string;
            var existing = previous == null ? null : JsonSerializer.Deserialize<SeenUser>(previous);
            var record = user;
            if (existing != null)
            {
                // Importing older logs or rediscovering an exact ID must preserve the newest encounter and any known profile.
                if (user.LastSeenAt >= existing.LastSeenAt)
                {
                    existing.DisplayName = user.DisplayName;
                    existing.LastSeenAt = user.LastSeenAt;
                    existing.WorldId = user.WorldId;
                    existing.Source = user.Source;
                }
                if (includeProfile)
                {
                    existing.ProfilePicUrl = user.ProfilePicUrl;
                    existing.IsAgeVerified = user.IsAgeVerified;
                    existing.Tags = user.Tags;
                }
                record = existing;
            }
            var json = JsonSerializer.Serialize(record);
            if (json == previous) continue;
            writeRecord.Value = json;
            await write.ExecuteNonQueryAsync();
        }
        transaction.Commit();
    }

    public async Task<List<SeenUser>> ReadEncountersAsync()
    {
        using var connection = await OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = CreateEncounters + "; SELECT Record FROM GroupInviteEncounters ORDER BY Identity";
        using var reader = await command.ExecuteReaderAsync();
        var users = new List<SeenUser>();
        while (await reader.ReadAsync())
        {
            var user = JsonSerializer.Deserialize<SeenUser>(reader.GetString(0));
            if (user != null) users.Add(user);
        }
        return users.OrderByDescending(u => u.LastSeenAt).ToList();
    }
}
