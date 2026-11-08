using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace VRCGroupTools.Services;

public static class UpdatePackage
{
    public const long MaxBytes = 512L * 1024 * 1024;
    public static string Hash(string path) { using var file=File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
    public static async Task CopyBoundedAsync(Stream source, Stream target, long limit, CancellationToken token = default)
    {
        var buffer=new byte[81920];long total=0;int read;
        while((read=await source.ReadAsync(buffer,token))>0) { total+=read;if(total>limit)throw new InvalidDataException("Update exceeds size limit");await target.WriteAsync(buffer.AsMemory(0,read),token); }
    }
    public static string Stage(string zipPath, string directory, string? expectedZipSha256 = null)
    {
        if(new FileInfo(zipPath).Length>MaxBytes)throw new InvalidDataException("Update ZIP exceeds size limit");
        if(expectedZipSha256!=null && (!Regex.IsMatch(expectedZipSha256,@"\A[a-fA-F0-9]{64}\z") || !Hash(zipPath).Equals(expectedZipSha256,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Update ZIP SHA256 mismatch");
        using var zip=ZipFile.OpenRead(zipPath);
        if(zip.Entries.Count>64)throw new InvalidDataException("Too many update entries");
        long total=0;var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var entry in zip.Entries)
        {
            var name=entry.FullName.Replace('\\','/');
            if(name.StartsWith('/') || name.Contains(':') || name.Split('/').Any(p=>p is ".." or ".") || name.Contains('\0') || !names.Add(name) || ((entry.ExternalAttributes>>16)&0xF000)==0xA000)throw new InvalidDataException("Unsafe or duplicate update entry");
            total=checked(total+entry.Length);if(total>MaxBytes)throw new InvalidDataException("Expanded update exceeds size limit");
            if(name.EndsWith(".exe",StringComparison.OrdinalIgnoreCase)&&name!="VRCGroupTools.exe")throw new InvalidDataException("Unexpected executable in update");
        }
        var executable=zip.GetEntry("VRCGroupTools.exe")??throw new InvalidDataException("Expected root VRCGroupTools.exe missing");
        if(executable.Length<2)throw new InvalidDataException("Invalid executable");
        string? expectedExe=null;var manifest=zip.GetEntry("SHA256SUMS.txt");
        if(manifest!=null)
        {
            if(manifest.Length>4096)throw new InvalidDataException("Oversized checksum manifest");
            using var text=new StreamReader(manifest.Open());var value=text.ReadToEnd();
            var match=Regex.Match(value,@"\A([a-fA-F0-9]{64})  VRCGroupTools\.exe\r?\n?\z");
            if(!match.Success)throw new InvalidDataException("Invalid executable checksum manifest");expectedExe=match.Groups[1].Value;
        }
        Directory.CreateDirectory(directory);var path=Path.Combine(directory,"VRCGroupTools.exe");
        try
        {
            using(var input=executable.Open())using(var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write)) input.CopyTo(output);
            using(var file=File.OpenRead(path)){if(file.ReadByte()!='M'||file.ReadByte()!='Z')throw new InvalidDataException("Not a Windows executable");}
            if(expectedExe!=null&&!Hash(path).Equals(expectedExe,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Executable SHA256 mismatch");
            return path;
        }
        catch { if(File.Exists(path))File.Delete(path);throw; }
    }
}
