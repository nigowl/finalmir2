# 发布程序与游戏版本

程序和游戏内容分开维护。同一套程序可以运行不同玩法版本；每个版本都完整保存自己的 SQLite、地图、素材、世界配置、NPC/任务/掉落脚本、管理密钥及备份。

```text
finalmir2/
  build/                     publish.sh 生成，不纳入 Git
    server/                  服务端程序、appsettings.json、start.sh
    admin/                   Blazor 管理端、appsettings.json、start.sh
    client/                  Godot 宿主、编译程序集、资源包、start.sh
  data/
    versions/
      default/                  当前已有版本
        assets/              maps、libraries、sounds、资源清单
        world/               Configs、Envir、Localization 等
        runtime/             mir2.sqlite3、admin.key、备份、暂存、进程记录
        import/              首次导入资料及历史归档
      复古/                  复制 default 目录后按需修改
      变态/                  另一个独立版本
```

物品、怪物、地图、NPC 和任务定义由各版本的 SQLite 保存；脚本与配置在该版本的 world 内。客户端只连接服务端并按需缓存素材，不读取或携带版本数据库。

## 发布

在项目根目录执行：

```bash
./publish.sh
```

构建机需要 .NET SDK、Python 3 和 Godot 4.7.2 .NET 应用。默认输出到 build，也可执行 `./publish.sh /absolute/path/to/build`。所有程序先在暂存目录生成，成功后替换三端目录；构建失败保留已有发布，替换异常会恢复旧目录。再次发布保留服务端、管理端已有的 appsettings.json，且不复制、覆盖或清空 data。

发布目录内的程序仍在运行时，脚本会拒绝覆盖。请先正常停止服务端并等待存档完成，再重新发布。

服务端和管理端包含对应 macOS 架构的 .NET 运行时，运行时不需要 SDK。客户端当前随包携带本机的 Godot .NET 宿主和编译程序集，运行机器仍需 .NET 8 运行时；无需安装 SDK 或另外安装 Godot。此方式不依赖外网下载导出模板，体积较大；尚未制作精简导出、开发者签名或公证包。当前在 Apple Silicon macOS 上验证。

## 选择版本

分别编辑：

- build/server/appsettings.json
- build/admin/appsettings.json

默认内容：

```json
{
  "Mir2": {
    "VersionDirectory": "../../data/versions/default",
    "ServerUrl": "http://127.0.0.1:5080"
  }
}
```

VersionDirectory 是完整版本目录，支持绝对路径；相对路径始终以对应程序目录为基准，与终端当前目录无关。服务端和管理端必须指向同一版本。ServerUrl 仅供管理端连接资源/管理 API 使用。

```bash
./build/server/start.sh
./build/admin/start.sh
./build/client/start.sh
```

三个入口分别运行在三个终端；Ctrl+C 正常停止，服务端退出前保存。发布入口直接运行已经发布的程序，不自动编译或替换旧实例。默认端口仍是游戏 TCP 7000、资源/管理 API 5080、管理 UI 5081。

管理密钥在所选版本的 runtime/admin.key 内。管理界面显示版本名称；管理端请求携带版本目录身份，服务端会拒绝目录不匹配的请求，即使两个复制版本仍使用相同的管理密钥。

根目录原有 start-server.sh、start-admin.sh、start-client.sh 保留为开发入口，仍先构建并安全替换所属实例，默认同样使用 data/versions/default。

## 增加玩法版本

先正常停止源版本的服务端，确认进程已退出，再复制整个版本目录。目标目录应尚不存在：

```bash
cp -R data/versions/default data/versions/复古
```

把服务端、管理端 appsettings.json 的 VersionDirectory 都改为 `../../data/versions/复古`，然后启动。此后通过该管理端修改怪物、物品、地图、NPC、配置和脚本，写入的都是复古目录。

复制会同时复制账户、角色、行会存档及管理密钥，不会自动清档。不要只复制运行中的 SQLite 主文件，也不要用指向其他版本的可写目录符号链接来代替完整复制。初始化完成的版本正常运行只依赖 assets、world、runtime；保留 import 方便追溯导入来源。

## 同时运行多个版本

给每个实例指定独立端口，并用现有环境变量覆盖版本目录：

```bash
MIR2_DATA_ROOT="$PWD/data/versions/复古" MIR2_GAME_PORT=17000 MIR2_RESOURCE_PORT=15080 ./build/server/start.sh
MIR2_DATA_ROOT="$PWD/data/versions/复古" MIR2_SERVER=http://127.0.0.1:15080 MIR2_ADMIN_PORT=15081 ./build/admin/start.sh
MIR2_SERVER=http://127.0.0.1:15080 ./build/client/start.sh
```

MIR2_DATA_ROOT 优先于 appsettings.json，含义是“所选版本目录”，不是外层 data 或 versions。MIR2_RUNTIME_ROOT 仍兼容原有隔离测试，但常规部署应不设置它，让 SQLite、密钥、备份和暂存全部留在对应版本的 runtime 内。不要给不同版本配置相同运行目录。

部署时复制 build 与所需的 data/versions 目录即可，不需要 src 或 Mir2.sln。如果改变二者的相对位置，更新 appsettings.json 的版本路径。

## 验证

```bash
./publish.sh
python3 tests/Deployment/smoke.py
```

部署测试将三端复制到项目外的临时目录，在不把 dotnet 或 Godot 加入 PATH 的环境运行发布程序。测试通过完整目录复制生成两个版本，检查并行运行、账户/怪物/地图/NPC 脚本隔离、管理版本标签和错配拒绝、Blazor 静态资源、Godot 完整游玩及存档重启、SQLite 完整性，以及重复发布保留配置。素材库在测试 fixture 中只读复用，正式版本数据不参与测试；失败保留隔离目录供排查。
