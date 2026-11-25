"""Build and package the project's current version without publishing it."""
from pathlib import Path
import hashlib,json,os,shutil,subprocess,sys
from xml.etree import ElementTree
ROOT=Path(__file__).resolve().parent.parent;os.chdir(ROOT)
VERSION=ElementTree.parse(ROOT/'src/VRCGroupTools.csproj').findtext('.//Version')
OUT=ROOT/'installer/Output'/f'{VERSION}-verification';OUT.mkdir(parents=True,exist_ok=True)
SDK='dotnet'
SUITES=['GroupInviteTests','InstanceInviteTests','GroupMetadataRegression','PublicationTests','SchedulerWebhookTests','AutoInviteTests','UpdateTests']

def find_inno():
    if os.environ.get('INNO_ISCC'):
        return os.environ['INNO_ISCC']
    installed=shutil.which('ISCC.exe')
    if installed:
        return installed
    directories=[ROOT/'installer/Output/tooling/InnoSetup']
    for base in ('ProgramFiles(x86)', 'ProgramFiles', 'LOCALAPPDATA'):
        if base not in os.environ:
            continue
        parent=Path(os.environ[base])
        if base=='LOCALAPPDATA':
            parent=parent/'Programs'
        directories.extend(parent/f'Inno Setup {major}' for major in (6, 7))
    for directory in directories:
        compiler=directory/'ISCC.exe'
        if compiler.is_file():
            return str(compiler)
    raise SystemExit('Install Inno Setup, add ISCC.exe to PATH, or set INNO_ISCC to the compiler executable.')
def run(name,args):
    p=subprocess.run(args,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace')
    (OUT/(name+'.log')).write_text(p.stdout,encoding='utf-8');print(name,p.returncode,flush=True)
    if p.returncode:print(p.stdout[-5000:]);raise SystemExit(p.returncode)
def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()
run('final-build',[SDK,'build','src/VRCGroupTools.csproj','-c','Release'])
for suite in SUITES:run('build-'+suite,[SDK,'build','tests/'+suite,'-c','Release','--no-dependencies'])
run('publish',[SDK,'publish','src/VRCGroupTools.csproj','--no-build','-c','Release','-r','win-x64','--self-contained','true','-p:PublishSingleFile=true','-o','src/bin/Publish'])
run('inno',[find_inno(),'installer/setup.iss'])
run('portable-package',[sys.executable,'installer/package-update.py'])
run('verify-package',[sys.executable,'installer/verify-package.py'])
shutil.copyfile(ROOT/'installer/Output/package-manifest.json',OUT/'package-manifest.json')
assembly=ROOT/'src/bin/Release/net8.0-windows/win-x64/VRCGroupTools.dll'
copies=[ROOT/'tests'/suite/'bin/Release/net8.0-windows/win-x64/VRCGroupTools.dll' for suite in SUITES if suite!='GroupMetadataRegression']
assert all(p.is_file() and sha(p)==sha(assembly) for p in copies), 'A release test project differs from the packaged application.'
(OUT/'tested-assembly-copies.json').write_text(json.dumps({'release_assembly_sha256':sha(assembly),'copies':[{'path':str(p.relative_to(ROOT)),'sha256':sha(p)} for p in copies],'note':'GroupMetadataRegression links source and has no application DLL copy.'},indent=2),encoding='utf-8')
paths=subprocess.check_output(['git','ls-files','--cached','--others','--exclude-standard','-z']).decode('utf-8').split('\0')
inputs=[{'path':p,'bytes':(ROOT/p).stat().st_size,'sha256':sha(ROOT/p)} for p in sorted(set(paths)) if p and (ROOT/p).is_file()]
revision=subprocess.check_output(['git','rev-parse','HEAD'],text=True).strip()
dirty=bool(subprocess.check_output(['git','status','--porcelain','--untracked-files=all']))
manifest={'version':VERSION,'head':revision,'dirty_source':dirty,'note':'All tracked and nonignored source inputs at freeze. The files list includes changes since HEAD if dirty_source is true.','files':inputs}
(OUT/'source-inputs.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
(OUT/'source-inputs.json.sha256').write_text(sha(OUT/'source-inputs.json')+'  source-inputs.json\n',encoding='ascii')
prior_assets=OUT/'prior-assets.json'
if prior_assets.is_file():
    for old in json.loads(prior_assets.read_text()):
        assert sha(ROOT/old['path'])==old['sha256'],'Prior asset changed: '+old['path']
print(json.dumps({'frozen':True,'nonignored_inputs':len(inputs),'source_manifest_sha256':sha(OUT/'source-inputs.json'),'release_assembly_sha256':sha(assembly),'tested_copies':len(copies)}),flush=True)
