using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace VRCGroupTools.Services;

public static class UpdateReplacement
{
    private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";
    public static ProcessStartInfo StartInfo(string script) => new()
    {
        FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
        UseShellExecute = false, CreateNoWindow = false, WindowStyle = ProcessWindowStyle.Normal
    };
    public static string Script(string source, string target, int parentId, string log, string sha256, bool showErrors = true)
    {
        if(!Regex.IsMatch(sha256,@"\A[a-fA-F0-9]{64}\z") || parentId<0)throw new ArgumentException("Invalid replacement parameters");
        // Encoded PowerShell and single-quoted literals preserve Unicode/spaces/metacharacters without cmd expansion.
        return $$"""
$ErrorActionPreference = 'Stop'
$source = {{Quote(source)}}
$target = {{Quote(target)}}
$log = {{Quote(log)}}
$expected = {{Quote(sha256)}}
$backup = $target + '.update-backup-' + [Guid]::NewGuid().ToString('N')
$next = $target + '.update-' + [Guid]::NewGuid().ToString('N')
$replaced = $false
try {
    if ({{parentId}} -gt 0) {
        $parent = Get-Process -Id {{parentId}} -ErrorAction SilentlyContinue
        if ($null -ne $parent -and !$parent.WaitForExit(60000)) { throw 'Application did not exit within 60 seconds; update cancelled.' }
    }
    if ((Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash -ne $expected) { throw 'Staged executable SHA256 changed.' }
    [IO.File]::Copy($source, $next, $false)
    if ((Get-FileHash -LiteralPath $next -Algorithm SHA256).Hash -ne $expected) { throw 'Replacement copy SHA256 mismatch.' }
    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        try { [IO.File]::Replace($next, $target, $backup); $replaced = $true; break }
        catch { if ($attempt -eq 19) { throw }; Start-Sleep -Milliseconds 500 }
    }
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $expected) { throw 'Installed copy SHA256 mismatch.' }
    Start-Process -FilePath $target -WorkingDirectory ([IO.Path]::GetDirectoryName($target)) -ErrorAction Stop | Out-Null
    ('Updated and relaunched. Previous executable retained at ' + $backup) | Out-File -LiteralPath $log -Encoding utf8
    exit 0
} catch {
    $failure = $_.Exception.Message
    if ($replaced -and [IO.File]::Exists($backup)) {
        try { [IO.File]::Replace($backup, $target, ($target + '.failed-' + [Guid]::NewGuid().ToString('N'))); $failure += ' Previous executable restored.' }
        catch { $failure += ' Rollback failed: ' + $_.Exception.Message + '. Preserve backup at ' + $backup }
    }
    ('Update failed: ' + $failure) | Out-File -LiteralPath $log -Encoding utf8
    {{(showErrors ? "Add-Type -AssemblyName PresentationFramework; [System.Windows.MessageBox]::Show(('Update failed. See log: ' + $log + [Environment]::NewLine + $failure), 'VRC Group Tools update') | Out-Null" : "# Error UI disabled only by isolated test harness")}}
    exit 1
} finally {
    if ([IO.File]::Exists($next)) { [IO.File]::Delete($next) }
}
""";
    }
}
