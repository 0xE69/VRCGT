using System.IO;

sealed class LogFixture : IDisposable
{
    public string Directory { get; } = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
    public DateTime Now { get; } = DateTime.UtcNow;
    public string Location { get; } = Fixture.Location("123~private(usr_test)~nonce(secret)").FullLocation;
    public string FilePath => Path.Combine(Directory, "output_log_fixture.txt");
    public LogFixture()
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, $"[Behaviour] Joining {Location}\n[Behaviour] OnJoinedRoom\n");
    }
    public void Dispose() => System.IO.Directory.Delete(Directory, true);
}
