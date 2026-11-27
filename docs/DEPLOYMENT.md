# 发布程序与游戏版本

程序和游戏内容分开维护。同一套程序可以运行不同玩法版本；每个版本都完整保存自己的 SQLite、地图、素材、世界配置、NPC/任务/掉落脚本、管理密钥及备份。

英雄恢复计时使用玩家自定义存档v3（原基础格式117不变），启动读取旧v2前自动完整备份SQLite；旧存活英雄保持生命，旧死亡英雄没有时间戳，升级时开始一次60秒等待。新存档仍保存原账户、角色、英雄、物品与编号。新程序开始保存后，旧程序不能单独回滚读取v3；恢复旧程序须在停服后恢复对应完整SQLite/world备份，已有新资产变化时先保留当前库并评估恢复差异，不能直接覆盖。

GM登录确认是版本脚本变更：通过受保护管理文本API及文件版本号更新`Envir/SystemScripts/00Default/Login.txt`，保留其余登录处理与r001内的目的地/ISADMIN检查。由于原NamelistReset包含子页面，提示使用原`DELAYGOTO 1 @AdminMapPrompt`在登录处理后执行；不要把提示接到嵌入子页面尾部。脚本范例和隔离验证在`tests/Compatibility/GmLogin.txt`、`hero-recovery.py`，正常重启后生效。

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

物品、怪物、地图、NPC 和任务定义由各版本的 SQLite 保存；脚本与配置在该版本的 world 内。客户端不读取或携带版本数据库，地图及场景资源仍按需从服务端下载和校验缓存。

## 发布

在项目根目录执行：

```bash
./publish.sh
```

构建机需要 .NET SDK、Python 3、rsync，包含客户端时另需 Godot 4.7.2 .NET 应用。默认输出到 build，也可执行 `./publish.sh /absolute/path/to/build`。发布始终在独立源码副本编译，不覆盖运行中的开发程序集。所有程序先在暂存目录生成，成功后替换所选程序目录；构建失败保留已有发布，替换异常会恢复旧目录。再次发布保留服务端、管理端已有的 appsettings.json，且不复制、覆盖或清空 data。

根目录 `VERSION` 是三端共用的程序版本，`Directory.Build.props` 将其写入程序集；每次发布将末位加一，全部构建和目录替换成功后才保存新版本，失败不递增。发布锁避免同一源码目录同时递增。每个实际发布的程序目录附带 `VERSION`，服务端和管理端支持 `start.sh --version`，`/health` 的 `programVersion` 同步返回程序版本；管理台顶栏与客户端标题栏显示各自实际程序版本。此版本与玩法目录名称、资源内容哈希及协议编号相互独立。

仅发布服务器程序（包含配套陪玩组件，不生成或修改客户端包、不带游戏数据）：

```bash
./publish.sh build/linux-x64 --server-only --runtime linux-x64
python3 tests/Deployment/publish.py
```

支持 `linux-x64`、`linux-arm64`、`osx-arm64`、`osx-x64`；含客户端的发布仍限 macOS。仅发布两端后，现有客户端包保持原版本；下次构建客户端读取新的共用版本，不能把未重建的旧客户端称作新包。

开发启动和发布构建从已导入版本（默认 `data/versions/default`，可用 `MIR2_DATA_ROOT` 指定）提取登录、选角、建角、通用窗口和基础 HUD 使用的帧，以及登录音乐、选角音乐和按钮音效，随客户端程序集携带。首屏不等待资源服务，重连保留本地皮肤。源码仓库不提交生成的美术包；更换这些内置美术需要重新构建客户端，选角数据仍需要连接服务器。

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

管理密钥文件与 `MIR2_ADMIN_KEY` 均不限制非空密钥长度，单字符或短密钥也可使用；空白环境变量沿用文件配置，空白文件拒绝启动。读取已有密钥不会重置或改写它，未提供密钥时仍生成64位十六进制随机值。文件600/目录700权限、固定时间比较、登录限流、防伪/Cookie及版本校验保持不变；公网使用短密钥时更容易被猜中。

根目录 start-server.sh、start-admin.sh、start-client.sh 是 Debug/Development 开发入口，先构建并安全替换所属实例，默认使用 data/versions/default；管理仅本机开发免密。publish.sh 和发布程序仍为 Release，管理密钥登录不变。

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

陪玩执行组件随服务端发布在 `server/companion/`，由宿主启动；不要混用不同构建的 `Mir2.Shared.dll`。发布脚本确保两者协议指纹一致。陪玩人格、昵称池、模型/密钥、日程和用量随版本SQLite复制，改为新目录后总开关默认关闭；使用方法见 [陪玩管理](COMPANIONS_OPERATIONS.md)。

## Linux 服务器布局与运行

服务器部署采用以下布局，`data` 本身就是所选玩法版本根目录，不再多套一层 `data/versions/default`：

```text
/devdata/finalmir2/
  server/                    Linux 自包含服务端及 companion/
  admin/                     Linux 自包含管理端
  data/
    assets/                  用户手动上传 maps、libraries、sounds、import-report.json
    world/                   对应版本的 Configs、Envir 等
    runtime/                 对应版本的 mir2.sqlite3、管理密钥及备份
    import/                  仅首次原始导入时需要
```

两端 `appsettings.json` 的 `Mir2.VersionDirectory` 都设为 `../data`，管理端 `Mir2.ServerUrl` 为 `http://127.0.0.1:5080`。程序自带 .NET 8，不在服务器安装 SDK；仍依赖系统 glibc、ICU、OpenSSL 等原生运行库，应以目标机实际启动验证兼容性。

`scripts/deployment/finalmir2-{server,admin}.service` 是上述固定布局的 systemd 单元，使用独立低权限用户 `finalmir2`，程序目录只读，只有 `data` 可写。两端依赖 `/devdata` 挂载；数据盘通过文件系统 UUID 写入 `/etc/fstab`。已有数据盘须先检查并保留数据，格式化必须另外明确授权。

首次部署若未上传 SQLite、资源导入完成标记和 world/Configs，游戏单元保持等待，避免创建空世界或反复失败重启。管理台可先登录，但游戏操作须等待游戏服务就绪。请将完整、配套的版本数据手动放进 `data/`；只上传美术素材不足以启动已有世界。迁移运行中的版本，必须先正常保存停服再复制 SQLite 和配套 world/runtime；不要直接上传仍在写入的数据库。

上传完成后在服务器执行：

```bash
chown -R finalmir2:finalmir2 /devdata/finalmir2/data
chmod 700 /devdata/finalmir2/data/runtime
systemctl restart finalmir2-server finalmir2-admin
systemctl status finalmir2-server finalmir2-admin
curl -fsS http://127.0.0.1:5080/health
```

管理程序默认监听 `127.0.0.1:5081`。本次 Linux 服务模板按授权设置 `MIR2_ADMIN_BIND_ADDRESS=0.0.0.0`，可直接通过 `http://服务器地址:5081` 访问；仅接受 localhost/合法 IP 的 Host，不接受任意域名。密钥登录、Cookie、防伪和版本校验保持生效。公网 HTTP 会明文传输登录密钥，建议另配 HTTPS 或限制可信来源；也可将绑定恢复回环并使用 SSH 隧道。以服务器地址代替下例占位符：

```bash
ssh -N -L 15081:127.0.0.1:5081 root@服务器地址
# 浏览器打开 http://127.0.0.1:15081
ssh root@服务器地址 'cat /devdata/finalmir2/data/runtime/admin.key'
```

游戏监听 TCP 7000，客户端资源地址为 `http://服务器地址:5080`；云安全组需放行这两个端口，管理台按需放行 5081（建议限制可信来源）。不要为了公网管理而关闭现有 Host、登录或版本保护。

更新时先在 `.incoming-版本号` 等独立目录上传并核对两端 `--version`、Shared 一致性，再正常停止 `finalmir2-admin`、`finalmir2-server`。确认退出并完整备份 SQLite/world/runtime，保留原 `appsettings.json` 后再替换程序目录，最后启动并检查健康、日志与实际 TCP。旧目录留作回退；若新程序已经升级并保存存档，回退必须连同配套数据备份处理，不能只换旧程序。
