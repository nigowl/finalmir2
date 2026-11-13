"""隔离验证管理端 Debug 热更新、自动重启及受管启动切换。"""
import json,os,pathlib,socket,subprocess,tempfile,time,urllib.request,uuid,shutil,signal

repo=pathlib.Path(__file__).resolve().parents[2]
root=pathlib.Path(tempfile.mkdtemp(prefix='mir2-admin-watch-')).resolve()
name='WatchProbe_'+uuid.uuid4().hex
route='/watch-probe-'+uuid.uuid4().hex
component=repo/'src/Admin/Components/Pages'/(name+'.razor')
source=repo/'src/Admin/Services'/(name+'.cs')
css=repo/'src/Admin/wwwroot'/(name+'.css')
with socket.socket() as sock:sock.bind(('127.0.0.1',0));port=sock.getsockname()[1]
address=f'http://127.0.0.1:{port}'
env=os.environ|{'MIR2_DATA_ROOT':str(root/'data'),'MIR2_RUNTIME_ROOT':str(root/'data/runtime'),'MIR2_ADMIN_PORT':str(port),'MIR2_SERVER':'http://127.0.0.1:1'}
processes=[]
def start(watch):
    log=open(root/f'watch-{len(processes)}.log','w')
    p=subprocess.Popen([str(repo/'start-admin.sh')]+(['--watch'] if watch else []),cwd=repo,env=env,start_new_session=True,stdin=subprocess.DEVNULL,stdout=log,stderr=subprocess.STDOUT)
    processes.append((p,log));return p

def wait_ready(p):
    end=time.monotonic()+90
    while time.monotonic()<end:
        if p.poll() is not None:raise AssertionError((root/'watch-0.log').read_text())
        try:
            if json.load(urllib.request.urlopen(address+'/health',timeout=1))['ready']:return
        except (OSError,ValueError):pass
        time.sleep(.2)
    raise AssertionError('管理端启动超时')

try:
    component.write_text(f'@page "{route}"\n@attribute [Authorize]\n<link rel="stylesheet" href="/{name}.css" />\n<h1 class="watch-probe">热更新前</h1>\n<p>@(new Mir2.Admin.Services.{name}().Value)</p>\n<MudTabs Rounded="true" ApplyEffectsToContainer="true"><MudTabPanel Text="布局一"><MudButton Size="Size.Small" Variant="Variant.Filled" Color="Color.Primary">主要操作</MudButton><MudButton Size="Size.Small" Variant="Variant.Outlined" Color="Color.Default">辅助操作</MudButton></MudTabPanel><MudTabPanel Text="布局二"><p>内容</p></MudTabPanel></MudTabs>\n' )
    source.write_text(f'namespace Mir2.Admin.Services; public class {name} {{ public string Value => "重启前"; }}\n')
    css.write_text('.watch-probe{color:rgb(110,120,130)}\n')
    watch=start(True);wait_ready(watch)
    subprocess.run(['node',str(repo/'tests/Management/watch.cjs'),str(root),address,route,str(component),str(source),str(css),name],cwd=repo,env=env,check=True,timeout=180)
    normal=start(False);watch.wait(75);wait_ready(normal)
    assert watch.returncode in (0,130,-signal.SIGINT),(root/'watch-0.log').read_text()
    assert '热更新后' in urllib.request.urlopen(address+route).read().decode()
    print('PASS 同一隔离实例从 watch 切换普通管理端，旧 watcher 正常退出且没有重新拉起占用端口。')
finally:
    for p,log in processes:
        if p.poll() is None:
            assert os.getpgid(p.pid)==p.pid
            os.killpg(p.pid,signal.SIGINT)
            try:p.wait(20)
            except subprocess.TimeoutExpired:
                os.killpg(p.pid,signal.SIGKILL);p.wait(5)
        log.close()
    for p in [component,source,css]:p.unlink(missing_ok=True)
    if os.environ.get('MIR2_TEST_KEEP')=='1':print(root)
    else:shutil.rmtree(root)
