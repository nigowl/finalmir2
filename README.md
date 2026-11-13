# Mir2 Native

macOS 上运行的传奇 Mir2：原 C# 游戏引擎、SQLite 存档、Godot 原生微端，以及 Blazor + MudBlazor 开发管理台。游戏客户端只保留微端，素材从服务端按需下载和校验缓存。

## 发布与部署

```bash
./publish.sh
./build/server/start.sh
./build/admin/start.sh
./build/client/start.sh
```

三端程序分别发布到 build/server、build/admin、build/client。服务端和管理端在各自的 appsettings.json 中配置 Mir2.VersionDirectory，默认 `../../data/versions/default`；相对路径以程序目录为准，MIR2_DATA_ROOT 可覆盖。重复发布保留这两份配置，运行中的发布程序需先正常停止。

每个版本目录包含独立的 SQLite、地图/素材、怪物/NPC/物品定义、世界配置与脚本、管理密钥、备份及导入资料。正常停服后复制 `data/versions/default` 为 `data/versions/复古`，再让服务端和管理端指向复古目录，即可独立修改和运行；复制会保留账户与角色存档。管理台显示版本名称并拒绝目录错配请求。部署只需程序和所需版本目录，不需要源码。

服务端、管理端为自包含程序；客户端随包携带 Godot .NET 宿主，运行机器仍需 .NET 8 运行时。详细配置、复制和多版本并行运行见 [发布部署说明](docs/DEPLOYMENT.md)。

## 目录

```text
src/
  Server/         游戏宿主；监控、资源、开发和运营管理模块
  Client/         Godot .NET 原生微端
  Admin/          Blazor Server + MudBlazor 管理界面
  Game/           原游戏引擎、数据库模型、规则和本地化
  Shared/         游戏协议、公共模型、管理契约和路径规则
  Resources/      共用地图解析、原始动画帧定义
  Client.Core/    不依赖 Godot 的连接和游戏状态
  Companion/      服务端监管的独立原 TCP 陪玩组件
tests/            兼容性、物品安全、管理和启动脚本测试
scripts/          三端共用的启动/进程管理逻辑
docs/             架构、运行和历史参考资料
data/versions/    各游戏版本的数据目录，不纳入版本控制
build/            发布后的 server、admin、client 三端程序
```

旧 WinForms/SlimDX 客户端、Windows 管理器、补丁网站和原编辑器工程已退出当前目录和解决方案；需要查阅原实现时使用 Git 历史。已有导入数据和运行存档保留，历史本地构建产物归档在 `data/versions/default/import/legacy-artifacts/`。

## 开发启动

需要 .NET 8 SDK/运行时、Godot **4.7.2 .NET（mono）**；NuGet 依赖在首次构建时恢复。管理台使用 MudBlazor 8.15.0。

在三个终端分别运行：

```bash
./start-server.sh
./start-client.sh
./start-admin.sh
```

脚本可以从其他工作目录调用，会先构建，再检查同项目、同运行目录的旧实例，发送 TERM 正常保存退出后重启；60 秒仍未退出才强制终止。通过 PID、进程身份及路径校验避免误杀其他项目。服务端另外锁定整个数据目录，避免两个引擎同时修改一套资源。

- 游戏 TCP：`127.0.0.1:7000`
- 资源和管理 API：`http://127.0.0.1:5080`
- 管理台：`http://127.0.0.1:5081`

三个 start-*.sh 开发入口统一使用 Debug/Development，管理台仅本机免密；Release 仍使用 `data/versions/default/runtime/admin.key` 中自动生成的密钥登录。页面使用 Cookie/防伪令牌，服务端接口使用管理密钥；接口密钥不会下发到浏览器。

## 唯一资源来源

```text
data/
  versions/
    default/
      assets/      本版本唯一在线资源：maps、libraries、sounds、清单
      world/       本版本 Configs、Envir 脚本、本地化
      runtime/     本版本 SQLite、备份、暂存、进程记录和管理密钥
      import/      旧参考数据及历史归档，不是在线资源来源
    复古/          复制后独立维护的其他版本
```

首次导入从 `data/versions/default/import/Crystal Database` 和 `data/versions/default/import/Crystal Client` 生成统一仓库，不覆盖已有存档和配置。发现同名地图差异时采用服务端地图并记录冲突；本机这份参考数据有 **12 张冲突地图**。

资源管理页面用于浏览与下载，已移除暂存、替换和发布入口。受保护的资源暂存/发布 API 保留：发布时保存玩家、停止引擎、备份替换文件、更新内容版本并重新启动，失败恢复文件；不要直接改写运行中的 assets。微端重新连接取得新清单，旧资源 URL 返回 410，下载时校验 SHA-256，损坏缓存会重新下载。

## 管理工具

开发管理界面可用 `./start-admin.sh --watch`，支持 Debug 热更新与必要时自动重启；普通启动/发布不变。见 [开发热更新](docs/ADMIN_DEVELOPMENT.md)；全部页面和后续功能遵守 [统一界面规范](docs/ADMIN_UI.md)。

- **系统设置**：总连接数、MaxIP、注册/登录权限、经验/掉落倍率、自动存档、安全区和怪物召回；人数/倍率即时更新，安全区效果变更明确提示正常重启。见 [系统设置说明](docs/SYSTEM_SETTINGS.md)。

- **运行监控**：在线玩家、队长/人数、交易对象/确认状态、所属行会/职位、内存、游戏循环、场景对象、世界数据数量、引擎日志；保存、停止/启动/重启、NPC/掉落脚本重载。
- **游戏账户**：表格查询、弹窗创建/编辑和离线空账户删除，金币/元宝、GM、封禁与解封、登录/仓库密码重置、强制改密及踢下线。
- **角色管理**：创建、等级/经验/PK、封禁/禁言、在线传送与离线位置/回城点、发物品、软删除/恢复；装备、背包、技能和任务只读检查。
- **行会管理**：创建、等级/资金/公告、成员增删、会长转交、职位及八项权限、受保护的解散；行会仓库只读。
- **刷怪管理**：全局怪物双栏图鉴（名称/形象 + 原引擎属性，每页 50 条）；地图多点布怪、每点独立数量与范围、一次备份批量保存；点击标记打开弹窗，显示怪物形象。阻挡格也可布点，引擎将阻挡中心调整到最近可行走格后按范围生成，保留原布点坐标。
- **NPC管理**：全局 NPC 瀑布流图鉴（每页 50 条），仅保留地图点击新建；独立 1:1 大地图（每格 48×32 像素）、原素材 NPC、小地图快速定位与鼠标坐标；点击 NPC 或空地打开管理/新建弹窗，属性/脚本选项卡支持形象预览和 TXT 读取/编辑/创建、备份、版本检查与重载。新建时名称自动补全拼音脚本路径，已有路径提示，可手动校正；地图选择支持代码/名称/编号搜索和 NPC 数量。位置/外观即时更新，更换脚本路径安全重启。
- **统一交互与存档保护**：列表使用表格，新增/编辑用紧凑弹窗，保存和删除前确认；持久化修改保留完整 SQLite 备份、读取版本检查、目标确认和事务失败回滚。操作审计及原因输入已移除，旧审计表下次启动时自动删除。
- **资源管理**：类型筛选、路径搜索、下载和冲突报告；弹窗每页 20 张原 `.Lib` 帧图片，宽度自适应多列瀑布流；地图缩略图及重新渲染，保持比例且最长边 1024 像素，显示尺寸/阻挡统计。按真实 48×32 像素等比渲染；重新生成覆盖所选版本的统一 PNG 缓存，管理地图与 Godot 小地图按修订刷新。
- **游戏开发**：配置、NPC/任务/掉落脚本编辑和未保存修改保护；编辑物品、怪物、地图、NPC、任务的基础定义，从原物品/怪物复制创建。所有定义修改检查读取版本、独立备份并保存到 SQLite；NPC 坐标、脚本路径和任务前置关系在保存前校验。

[运行与操作说明](docs/MACOS.md) · [模块和数据架构](docs/ARCHITECTURE.md) · [后续完善顺序](docs/ROADMAP.md) · [测试说明](tests/README.md)

**陪玩管理**：真实 TCP 注册建角/成长、12种人格与自定义修订、资料/日程/用量、100昵称池与批量随机创建、启停/暂停/解除、基础任务/补给/修理、组队/聊天/受限PK及可选多模型 LLM / 连接测试。所有陪玩持久数据随当前版本 SQLite 完整切换。使用方法和能力边界见 [陪玩管理与运行](docs/COMPANIONS_OPERATIONS.md)；[总体设计](docs/COMPANIONS.md) 和 [资料设计](docs/COMPANION_PROFILES.md) 中尚未交付的长期目标仍保留为规划。

## 验证

```bash
dotnet build Mir2.sln -c Release
MIR2_DATA_ROOT="$PWD/data/versions/default" dotnet run --project tests/Compatibility/Compatibility.csproj
dotnet run --project tests/ItemSecurity/ItemSecurity.csproj -c Release
./tests/Compatibility/smoke.sh
./tests/Compatibility/trade-recovery.sh
./tests/Management/smoke.sh
./tests/Management/administration.sh
python3 tests/Companions/smoke.py
```

账号、角色和行会存档编辑要求相关账户离线；在线传送和踢下线使用原引擎规则。刷怪管理和 NPC 位置/外观/显示条件即时更新；更换 NPC 脚本路径仍会重启引擎，立即补怪不重启。管理端启用强制改密后，玩家可在 Godot 登录界面完成修改。详见 [运营管理说明](docs/ADMINISTRATION.md)。

真实 Godot 集成测试覆盖登录改密、基础战斗、移动、物品、任务、仓库、双人交易及 SQLite 重启恢复；管理测试覆盖鉴权、发布/版本切换、文件编辑、配置和三端重复启动。测试使用隔离数据，不修改正式世界。

Godot 组队使用原 `Prguse/Title` 面板和 TCP 协议：P 打开，允许组队后可邀请、确认/拒绝、移除和离队，成员提示显示地图；`!!内容` 发送队聊。队长退出与队友断线沿用原引擎规则。

Godot 交易沿用原双面板和 10 格报价：T 打开，双方相邻面对面；邀请同意后双击/拖动物品，点击金币追加报价，双方确认成交。报价变化撤销双方确认；取消和掉线退回托管。交易托管随玩家快照保存，异常重启自动退回，满背包或金币上限的余款进入原邮件系统。

Godot 行会按 H 打开原版面板：公告、成员在线状态、职位与八项权限、招收/拒绝/移除、职位调整、离会和 `!~内容` 会聊。职位和公告修改由原引擎校验；管理台同步显示行会/职位。公告草稿不会被后台更新覆盖，检测到新公告后需重新读取。

行会仓库沿用原协议的 112 格与滚动布局：安全区内按职位权限存入、取出和交换，支持背包双击及跨窗口拖动；资金支持成员捐献、会长取款，操作等待服务端回执。管理台可核对同一份资金与仓库。SQLite 保存将账户和行会快照放入同一事务，写失败一起回滚；仍沿用自动保存周期，不保证每笔操作即时落盘。

网络层补发服务端未完整写出的 TCP 字节并串行发送，限制慢客户端的发送积压。微端拒绝坏包和超长帧、隔离旧连接任务，连接 10 秒超时、服务端 30 秒无响应时提示重连，HUD 显示原 KeepAlive 测得的延迟。

## 授权和边界

`src/Game/`、`src/Server/`、`src/Admin/`、`tests/` 沿用 GPL-3.0-only 范围；`src/Client/`、`src/Client.Core/`、`src/Resources/`、`src/Shared/` 沿用 MIT 范围。具体历史资格与第三方条款见 [LICENSE](LICENSE) 和 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。目录迁移不改变已有代码和素材的授权。

当前提供可游玩的基础移植及开发管理基础，尚未完整复刻行会增益/战争/领地、邮件界面、英雄、拍卖和全部编辑器功能；未提供签名/公证的独立 macOS 应用包。

微端保持 1024×768 逻辑画布，自动按屏幕 DPI 和可用区域选择初始窗口，支持手动缩放与换屏适配；本机 Retina 2× 实窗检查通过。
