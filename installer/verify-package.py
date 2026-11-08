"""Inspect packaged bits without starting app/installer or reading user data.
Bundle layout: dotnet/runtime v8.0.11 Microsoft.NET.HostModel Bundle/Manifest.cs,
Bundle/FileEntry.cs and AppHost/HostWriter.cs (Microsoft primary source).
Run from repo root: python installer/verify-package.py
"""
from pathlib import Path
import hashlib
import io
import json
import re
import struct
import subprocess
import zlib
import zipfile

ROOT = Path(__file__).resolve().parent.parent
EXE = ROOT / "src/bin/Publish/VRCGroupTools.exe"
version_match = re.search(r"<Version>(.*?)</Version>", (ROOT / "src/VRCGroupTools.csproj").read_text())
assert version_match is not None, "Project version missing"
VERSION = version_match.group(1)
INSTALLER = ROOT / f"installer/Output/VRCGroupTools_Setup_{VERSION}.exe"
ZIP = ROOT / f"installer/Output/VRCGroupTools-v{VERSION}-win-x64.zip"
ZIP_CHECKSUM = ZIP.with_suffix(".zip.sha256")


def sha(data):
    return hashlib.sha256(data).hexdigest()


def read_string(stream):
    size = 0
    for shift in range(0, 35, 7):
        byte = stream.read(1)[0]
        size |= (byte & 127) << shift
        if not byte & 128:
            return stream.read(size).decode("utf-8")
    raise ValueError("Invalid bundle string")


def verify():
    checks = {
        "App": (ROOT / "src/App.xaml.cs", r'Version => "([^"]+)"'),
        "Project": (ROOT / "src/VRCGroupTools.csproj", r"<Version>(.*?)</Version>"),
        "Assembly": (ROOT / "src/VRCGroupTools.csproj", r"<AssemblyVersion>(.*?)</AssemblyVersion>"),
        "File": (ROOT / "src/VRCGroupTools.csproj", r"<FileVersion>(.*?)</FileVersion>"),
        "Installer": (ROOT / "installer/setup.iss", r'MyAppVersion "([^"]+)"'),
        "Changelog": (ROOT / "CHANGELOG.md", r"## \[([^]]+)\]"),
    }
    versions = {}
    for key, (path, pattern) in checks.items():
        match = re.search(pattern, path.read_text(encoding="utf-8-sig"))
        assert match is not None, f"Missing version: {key}"
        versions[key] = match.group(1)
    assert all(value == (VERSION + ".0" if key in ("Assembly", "File") else VERSION) for key, value in versions.items()), versions
    data = EXE.read_bytes()
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    assert data[:2] == b"MZ" and data[pe:pe + 4] == b"PE\0\0"
    assert struct.unpack_from("<H", data, pe + 4)[0] == 0x8664, "Not win-x64 apphost"
    signature = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")
    position = data.index(signature)
    header_offset = struct.unpack_from("<q", data, position - 8)[0]
    assert 0 < header_offset < len(data), "Not a bundled executable"
    stream = io.BytesIO(data)
    stream.seek(header_offset)
    major, minor, count = struct.unpack("<III", stream.read(12))
    bundle_id = read_string(stream)
    assert major >= 6
    deps_offset, deps_size, config_offset, config_size, flags = struct.unpack("<qqqqQ", stream.read(40))
    entries = {}
    for _ in range(count):
        offset, size, compressed_size, kind = struct.unpack("<qqqB", stream.read(25))
        name = read_string(stream)
        stored_size = compressed_size or size
        assert name not in entries and offset >= 0 and offset + stored_size <= header_offset, name
        entries[name] = {"offset": offset, "size": size, "compressed_size": compressed_size, "kind": kind}
    required = ["VRCGroupTools.dll", "System.Private.CoreLib.dll", "e_sqlite3.dll", "wpfgfx_cor3.dll", "PresentationFramework.dll"]
    # Windows singlefilehost statically links CoreCLR/JIT/hostpolicy rather than listing those DLLs in the manifest.
    optional = pe + 24
    section_count = struct.unpack_from("<H", data, pe + 6)[0]
    optional_size = struct.unpack_from("<H", data, pe + 20)[0]
    sections = [struct.unpack_from("<IIII", data, optional + optional_size + index * 40 + 8) for index in range(section_count)]

    def rva_position(rva):
        for virtual_size, address, raw_size, raw_offset in sections:
            if address <= rva < address + max(virtual_size, raw_size):
                return raw_offset + rva - address
        raise ValueError("PE RVA outside sections")

    export = rva_position(struct.unpack_from("<I", data, optional + 112)[0])
    name_count = struct.unpack_from("<I", data, export + 24)[0]
    name_table = rva_position(struct.unpack_from("<I", data, export + 32)[0])
    native_exports = []
    for index in range(name_count):
        start = rva_position(struct.unpack_from("<I", data, name_table + index * 4)[0])
        native_exports.append(data[start:data.index(b"\x00", start)].decode("ascii"))
    assert {"DotNetRuntimeInfo", "g_CLREngineMetrics", "CLRJitAttachState"} <= set(native_exports), "Static runtime/JIT exports missing"
    assert all(name in entries for name in required), {name: name in entries for name in required}
    entry = entries["VRCGroupTools.dll"]
    raw = data[entry["offset"]:entry["offset"] + (entry["compressed_size"] or entry["size"])]
    assembly = zlib.decompress(raw, -15) if entry["compressed_size"] else raw
    built = ROOT / "src/bin/Release/net8.0-windows/win-x64/VRCGroupTools.dll"
    assert sha(assembly) == sha(built.read_bytes()), "Published assembly differs from tested Release assembly"
    config = json.loads(data[config_offset:config_offset + config_size])
    assert config["runtimeOptions"].get("includedFrameworks"), "Self-contained framework metadata missing"
    assert INSTALLER.is_file() and INSTALLER.stat().st_size > 1_000_000
    metadata_command = "$ErrorActionPreference='Stop'; @('" + str(EXE).replace("'", "''") + "','" + str(INSTALLER).replace("'", "''") + "') | ForEach-Object { $v=(Get-Item $_).VersionInfo; [PSCustomObject]@{Path=$_;FileVersion=$v.FileVersion;ProductVersion=$v.ProductVersion} } | ConvertTo-Json -Compress"
    metadata = json.loads(subprocess.check_output(["powershell.exe", "-NoProfile", "-Command", metadata_command], text=True))
    assert metadata[0]["FileVersion"] == VERSION + ".0"
    assert metadata[0]["ProductVersion"].startswith(VERSION)
    assert metadata[1]["ProductVersion"].strip() == VERSION
    with zipfile.ZipFile(ZIP) as package:
        assert package.namelist() == ["VRCGroupTools.exe", "SHA256SUMS.txt"], "Legacy updater requires one exact root application EXE, never installer/extra executables"
        assert sha(package.read("VRCGroupTools.exe")) == sha(data), "Portable ZIP differs from tested published executable"
        assert package.read("SHA256SUMS.txt").decode("ascii") == f"{sha(data)}  VRCGroupTools.exe\n"
    assert ZIP_CHECKSUM.read_text(encoding="ascii") == f"{sha(ZIP.read_bytes())}  {ZIP.name}\n"
    report = {"version": VERSION, "versions": versions, "metadata": metadata, "bundle_format": f"{major}.{minor}", "bundle_id": bundle_id,
              "bundle_entry_count": count, "native_runtime_exports": native_exports, "required_runtime_contents": required, "release_assembly_sha256": sha(assembly), "self_contained_frameworks": config["runtimeOptions"]["includedFrameworks"],
              "artifacts": [{"path": str(path), "bytes": path.stat().st_size, "sha256": sha(path.read_bytes())} for path in (EXE, INSTALLER, ZIP, ZIP_CHECKSUM)],
              "acceptance": "Static package inspection only; no application or installer execution, no live service acceptance."}
    output = ROOT / "installer/Output/package-manifest.json"
    output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    verify()
