"""Offline release runner: builds/tests only, never starts production App or publishes."""
from pathlib import Path
import filecmp,json,os,subprocess,sys,tempfile,time
from xml.etree import ElementTree
ROOT=Path(__file__).resolve().parent.parent
os.chdir(ROOT)
SDK='dotnet'
VERSION=ElementTree.parse(ROOT/'src/VRCGroupTools.csproj').findtext('.//Version')
OUT=ROOT/'installer/Output'/f'{VERSION}-verification'; OUT.mkdir(parents=True,exist_ok=True)
SUITES=['GroupInviteTests','InstanceInviteTests','GroupMetadataRegression','PublicationTests','SchedulerWebhookTests','AutoInviteTests','UpdateTests']
mode=sys.argv[1] if len(sys.argv)>1 else 'build-tests'
records=[]
env=os.environ.copy()
env.setdefault('TMPDIR',tempfile.gettempdir())
if os.name=='nt':
    # The legacy replacement harness starts Windows PowerShell 5.1, even when the caller uses pwsh.
    # os.environ is case-insensitive on Windows, but its copy() has upper-cased keys (SYSTEMROOT, PSMODULEPATH).
    native_modules=Path(os.environ.get('SystemRoot') or r'C:\Windows')/'System32/WindowsPowerShell/v1.0/Modules'
    existing=os.environ.get('PSModulePath','')
    for key in [k for k in env if k.upper()=='PSMODULEPATH']: del env[key]
    env['PSModulePath']=str(native_modules)+(os.pathsep+existing if existing else '')
def run(name,args):
    start=time.monotonic(); p=subprocess.run(args,cwd=ROOT,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace')
    (OUT/(name+'.log')).write_text(p.stdout,encoding='utf-8')
    record={'name':name,'command':args,'exit_code':p.returncode,'seconds':round(time.monotonic()-start,2),'pass_lines':sum(x.startswith(('PASS ','PASS:')) for x in p.stdout.splitlines()),'summary':[x for x in p.stdout.splitlines() if ' passed' in x or 'FAIL' in x or 'error ' in x][-15:]}
    records.append(record); (OUT/(mode+'-results.json')).write_text(json.dumps(records,indent=2),encoding='utf-8');print(json.dumps(record),flush=True)
    if p.returncode: print(p.stdout[-4500:]);sys.exit(p.returncode)
if mode=='build-tests':
    for suite in SUITES: run('initial-'+suite,[SDK,'run','--project','tests/'+suite,'-c','Release'])
elif mode in ('frozen-tests','release-tests'):
    zip_path=ROOT/'installer/Output'/f'VRCGroupTools-v{VERSION}-win-x64.zip'
    if not zip_path.is_file() or not zip_path.with_suffix('.zip.sha256').is_file():
        raise SystemExit(f'Package the portable ZIP and checksum for {VERSION} before running {mode}.')
    assembly=ROOT/'src/bin/Release/net8.0-windows/win-x64/VRCGroupTools.dll'
    for suite in SUITES:
        if mode=='release-tests':
            run('build-'+suite,[SDK,'build','tests/'+suite,'-c','Release','-p:BuildProjectReferences=false'])
        if suite!='GroupMetadataRegression':
            test_assembly=ROOT/'tests'/suite/'bin/Release/net8.0-windows/win-x64/VRCGroupTools.dll'
            if not filecmp.cmp(test_assembly,assembly,shallow=False):
                raise SystemExit(f'{suite} uses a different application assembly from the release build.')
        args=[SDK,'run','--no-build','--project','tests/'+suite,'-c','Release']
        if suite=='UpdateTests':args+=['--',str(zip_path),VERSION]
        run('frozen-'+suite,args)
else:raise SystemExit('Unknown mode')
