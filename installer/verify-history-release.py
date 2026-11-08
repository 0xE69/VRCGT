"""Offline release runner: builds/tests only, never starts production App or publishes."""
from pathlib import Path
import hashlib,json,os,subprocess,sys,time
ROOT=Path(__file__).resolve().parent.parent
os.chdir(ROOT)
SDK='C:/Users/mail/AppData/Local/hermes/cache/tools/dotnet8/dotnet.exe'
OUT=ROOT/'installer/Output/1.1.23-verification'; OUT.mkdir(parents=True,exist_ok=True)
SUITES=['GroupInviteTests','InstanceInviteTests','GroupMetadataRegression','PublicationTests','SchedulerWebhookTests','AutoInviteTests','UpdateTests']
mode=sys.argv[1] if len(sys.argv)>1 else 'build-tests'
records=[]
def run(name,args):
    start=time.monotonic(); p=subprocess.run(args,cwd=ROOT,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace')
    (OUT/(name+'.log')).write_text(p.stdout,encoding='utf-8')
    record={'name':name,'command':args,'exit_code':p.returncode,'seconds':round(time.monotonic()-start,2),'pass_lines':sum(x.startswith('PASS ') for x in p.stdout.splitlines()),'summary':[x for x in p.stdout.splitlines() if ' passed' in x or 'FAIL' in x or 'error ' in x][-15:]}
    records.append(record); (OUT/(mode+'-results.json')).write_text(json.dumps(records,indent=2),encoding='utf-8');print(json.dumps(record),flush=True)
    if p.returncode: print(p.stdout[-4500:]);sys.exit(p.returncode)
if mode=='build-tests':
    for suite in SUITES: run('initial-'+suite,[SDK,'run','--project','tests/'+suite,'-c','Release'])
elif mode=='frozen-tests':
    zip_path=str(ROOT/'installer/Output/VRCGroupTools-v1.1.23-win-x64.zip')
    for suite in SUITES:
        args=[SDK,'run','--no-build','--project','tests/'+suite,'-c','Release']
        if suite=='UpdateTests':args+=['--',zip_path,'1.1.23']
        run('frozen-'+suite,args)
else:raise SystemExit('Unknown mode')
