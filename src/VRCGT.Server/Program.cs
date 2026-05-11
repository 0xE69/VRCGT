// ============================================================
// VRCGT v2 — Standalone server entry point
// ============================================================
// Use this to run the server directly (development / CI):
//   dotnet run --project src/VRCGT.Server [--port <n>]
//
// For normal user-facing execution, VRCGT.Launcher starts this server
// in-process (same process, via ServerAppFactory.Create()) and opens
// the browser once /health responds.
//
// All WebApplication configuration lives in ServerAppFactory.cs.
//
// Port resolution order:
//   1. --port <n> command-line argument
//   2. VRCGT_PORT environment variable
//   3. AppConstants.DefaultPort (57432)
// ============================================================

using VRCGT.Core;
using VRCGT.Server;

var portStr = args.SkipWhile(a => a != "--port").Skip(1).FirstOrDefault()
              ?? Environment.GetEnvironmentVariable("VRCGT_PORT")
              ?? AppConstants.DefaultPort.ToString();

if (!int.TryParse(portStr, out var port) || port is < 1 or > 65535)
{
    Console.Error.WriteLine($"Invalid port value: '{portStr}'");
    return 1;
}

var app = await ServerAppFactory.CreateAsync(port, args);
await app.RunAsync();
return 0;

