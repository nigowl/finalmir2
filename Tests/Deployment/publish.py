"""用替身构建器检查发布事务，不触碰运行中的程序或版本数据。"""
import os
import pathlib
import shutil
import subprocess
import tempfile

repo = pathlib.Path(__file__).resolve().parents[2]
with tempfile.TemporaryDirectory(prefix="mir2-publish-") as temporary:
    root = pathlib.Path(temporary) / "source"
    root.mkdir()
    for name in ("publish.sh", "LICENSE", "LICENSE-GPL-3.0.txt", "THIRD-PARTY-NOTICES.md"):
        shutil.copy2(repo / name, root / name)
    version = root / "VERSION"
    version.write_text("1.0.0\n")
    tools = pathlib.Path(temporary) / "tools"
    tools.mkdir()
    dotnet = tools / "dotnet"
    dotnet.write_text("""#!/usr/bin/env python3
import os, pathlib, sys
arguments = sys.argv[1:]
assert arguments[0] in ('publish', 'build')
project = pathlib.Path(arguments[1])
assert '.publish.' in str(project) and '/source/src/' in str(project)
assert pathlib.Path.cwd() == project.parents[2]
if os.environ.get('FAIL_BUILD') and project.stem == 'Mir2.Admin': sys.exit(7)
output = pathlib.Path(arguments[arguments.index('-o') + 1]) if arguments[0] == 'publish' else project.parent / '.godot/mono/temp/bin/Debug'
output.mkdir(parents=True, exist_ok=True)
value = next(value.split('=', 1)[1] for value in arguments if value.startswith('-p:Version='))
assert (project.parents[2] / 'VERSION').read_text().strip() == value
(output / 'Mir2.Shared.dll').write_text(value)
(output / project.stem).write_text(value)
if arguments[0] == 'build': (output / 'Client.Godot.deps.json').write_text('{}')
""")
    dotnet.chmod(0o755)
    move = tools / "mv"
    move.write_text("""#!/usr/bin/env python3
import os, sys
source, destination = sys.argv[1:]
failure = os.environ.get('FAIL_INSTALL')
if failure == 'admin' and source.endswith('/admin') and '/.publish.' in source: sys.exit(8)
if failure == 'version' and destination.endswith('/VERSION'): sys.exit(9)
os.execv('/bin/mv', ['mv', source, destination])
""")
    move.chmod(0o755)
    output = root / "build/linux-x64"
    for role in ("server", "admin", "client"):
        (output / role).mkdir(parents=True)
        (output / role / "existing").write_text(role)
    for role in ("server", "admin"):
        (output / role / "appsettings.json").write_text('{"local":true}')
    environment = os.environ | {"PATH": str(tools) + os.pathsep + os.environ["PATH"]}

    def publish(arguments=None, **variables):
        return subprocess.run([str(root / "publish.sh"), str(output)] + (arguments if arguments is not None else ["--server-only", "--runtime", "linux-x64"]),
                              env=environment | variables, text=True, capture_output=True)

    def snapshot():
        return {str(path.relative_to(output)): path.read_bytes() for path in output.rglob("*") if path.is_file()}

    before = snapshot()
    result = publish(FAIL_BUILD="1")
    assert result.returncode != 0 and snapshot() == before, result.stdout + result.stderr
    assert version.read_text().strip() == "1.0.0"
    assert not (root / "build/.publish.lock").exists()
    for failure in ("admin", "version"):
        result = publish(FAIL_INSTALL=failure)
        assert result.returncode != 0 and snapshot() == before, result.stdout + result.stderr
        assert version.read_text().strip() == "1.0.0"
    for expected in ("1.0.1", "1.0.2"):
        result = publish()
        assert result.returncode == 0, result.stdout + result.stderr
        assert version.read_text().strip() == expected
        for role in ("server", "admin"):
            assert (output / role / "VERSION").read_text().strip() == expected
            assert (output / role / "Mir2.Shared.dll").read_text() == expected
            assert (output / role / "appsettings.json").read_text() == '{"local":true}'
        assert (output / "server/companion/Mir2.Shared.dll").read_text() == expected
        assert (output / "client/existing").read_text() == "client"
        assert not (root / "build/.publish.lock").exists()
        assert not list(output.glob(".publish.*"))
    if os.uname().sysname == 'Darwin':
        app = pathlib.Path(temporary) / 'Godot.app/Contents'
        (app / 'MacOS').mkdir(parents=True)
        (app / 'Info.plist').write_text('test')
        godot = app / 'MacOS/Godot'
        godot.write_text("#!/usr/bin/env python3\nimport pathlib,sys\nif '--version' in sys.argv: print('4.7.2.stable.mono')\nelse: pathlib.Path(sys.argv[-1]).write_bytes(b'pack')\n")
        godot.chmod(0o755)
        result = publish([], GODOT_BIN=str(godot))
        assert result.returncode == 0, result.stdout + result.stderr
        assert version.read_text().strip() == '1.0.3'
        assert (output / 'client/VERSION').read_text().strip() == '1.0.3'
        assert (output / 'client/.godot/mono/temp/bin/Debug/Mir2.Shared.dll').read_text() == '1.0.3'
        assert not (output / 'client/existing').exists()
    lock = root / "build/.publish.lock"
    lock.mkdir()
    assert publish().returncode != 0
    lock.rmdir()
    before = snapshot()
    for invalid in ("bad", "1.0.65534", "1.0.0\n2.0.0"):
        version.write_text(invalid)
        assert publish().returncode != 0 and snapshot() == before
    print("PASS 发布递增、配套 Shared、构建隔离、配置/客户端保留、失败回滚、版本校验和并发保护。")
