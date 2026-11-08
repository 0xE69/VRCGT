"""Build the portable archive consumed by installed v1.1.18+ clients. Never runs the application."""
from pathlib import Path
import hashlib
import re
import zipfile

root = Path(__file__).resolve().parent.parent
match = re.search(r'<Version>(.*?)</Version>', (root / 'src/VRCGroupTools.csproj').read_text())
assert match is not None, 'Project version missing'
version = match.group(1)
exe = root / 'src/bin/Publish/VRCGroupTools.exe'
output = root / 'installer/Output'
output.mkdir(exist_ok=True)
archive = output / f'VRCGroupTools-v{version}-win-x64.zip'
exe_sha = hashlib.sha256(exe.read_bytes()).hexdigest()
with zipfile.ZipFile(archive, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as package:
    package.write(exe, 'VRCGroupTools.exe')
    package.writestr('SHA256SUMS.txt', f'{exe_sha}  VRCGroupTools.exe\n')
with zipfile.ZipFile(archive) as package:
    assert package.namelist() == ['VRCGroupTools.exe', 'SHA256SUMS.txt']
    assert hashlib.sha256(package.read('VRCGroupTools.exe')).hexdigest() == exe_sha
archive_sha = hashlib.sha256(archive.read_bytes()).hexdigest()
sidecar = archive.with_suffix(archive.suffix + '.sha256')
sidecar.write_text(f'{archive_sha}  {archive.name}\n', encoding='ascii')
print(f'{archive} | {archive.stat().st_size} bytes | SHA256 {archive_sha}')
print(f'{sidecar}\nRoot EXE SHA256 {exe_sha}')
