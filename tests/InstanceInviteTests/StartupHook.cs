// Optional artifact probe. Runs before Main only when explicitly selected through
// DOTNET_STARTUP_HOOKS and VRCGT_ARTIFACT_SMOKE=1. It must not reference application types.
public static class StartupHook
{
    public static void Initialize()
    {
        if (Environment.GetEnvironmentVariable("VRCGT_ARTIFACT_SMOKE") != "1") return;
        Console.WriteLine($"ARTIFACT_SMOKE_PASS runtime={Environment.Version} architecture={System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}; application Main was not entered");
        Environment.Exit(0);
    }
}
