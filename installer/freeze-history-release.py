"""Produce a local offline 1.1.23 candidate. No commits, app startup or publication."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess,sys
ROOT=Path(__file__).resolve().parent.parent;os.chdir(ROOT)
OUT=ROOT/'installer/Output/1.1.23-verification';OUT.mkdir(parents=True,exist_ok=True)
SDK='C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe'
SUITES=['GroupInviteTests','InstanceInviteTests','GroupMetadataRegression','PublicationTests','SchedulerWebhookTests','AutoInviteTests','UpdateTests']
def run(name,args):
    p=subprocess.run(args,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace')
    (OUT/(name+'.log')).write_text(p.stdout,encoding='utf-8');print(name,p.returncode,flush=True)
    if p.returncode:print(p.stdout[-5000:]);raise SystemExit(p.returncode)
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
run('final-build',[SDK,'build','src/VRCGroupTools.csproj','-c','Release'])
for suite in SUITES:run('build-'+suite,[SDK,'build','tests/'+suite,'-c','Release','--no-dependencies'])
run('publish',[SDK,'publish','src/VRCGroupTools.csproj','--no-build','-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-o','src/bin/Publish'])
run('inno',[str(ROOT/'installer/Output/tooling/InnoSetup/ISCC.exe'),'installer/setup.iss'])
run('portable-package',[sys.executable,'installer/package-update.py'])
run('verify-package',[sys.executable,'installer/verify-package.py'])
shutil.copyfile(ROOT/'installer/Output/package-manifest.json',OUT/'package-manifest.json')
assembly=ROOT/'src/bin/Release/net8.0-windows/win-x64/VRCGroupTools.dll'
copies=[p for p in (ROOT/'tests').rglob('VRCGroupTools.dll') if 'bin' in p.parts]
assert len(copies)==6 and all(sha(p)==sha(assembly) for p in copies),[(str(p),sha(p)) for p in copies]
(OUT/'tested-assembly-copies.json').write_text(json.dumps({'release_assembly_sha256':sha(assembly),'copies':[{'path':str(p.relative_to(ROOT)),'sha256':sha(p)} for p in copies],'note':'GroupMetadataRegression links source and has no application DLL copy.'},indent=2),encoding='utf-8')
paths=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z']).decode('utf-8').split('\0')
inputs=[{'path':p,'bytes':(ROOT/p).stat().st_size,'sha256':sha(ROOT/p)} for p in sorted(set(paths)) if p and (ROOT/p).is_file()]
revision=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
manifest={'version':'1.1.23','head':revision,'dirty_source':True,'note':'All nonignored files at freeze, including every source/test/docs/script input. Ignored build/tool/package/evidence outputs excluded. Dirty tree is NOT represented by old HEAD; informational suffix retains build-time revision. No rebuild after this freeze.','files':inputs}
(OUT/'source-inputs.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
(OUT/'source-inputs.json.sha256').write_text(sha(OUT/'source-inputs.json')+'  source-inputs.json\n',encoding='ascii')
for old in json.loads((OUT/'prior-assets.json').read_text()):assert sha(ROOT/old['path'])==old['sha256'],'Prior asset changed: '+old['path']
print(json.dumps({'frozen':True,'nonignored_inputs':len(inputs),'source_manifest_sha256':sha(OUT/'source-inputs.json'),'release_assembly_sha256':sha(assembly),'tested_copies':len(copies)}),flush=True)
