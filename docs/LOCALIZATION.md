# 默认版本中文化

默认版本的物品、怪物、地图、NPC、任务名称与说明由原引擎世界快照保存。中文译表随 Shared/Server 程序内嵌；Godot 与管理端使用同一份名称、职业、性别、属性和技能译文。NPC/任务对话、公告、配置显示名和服务端消息写入所选版本的 world。MudBlazor 控件、分页、日期与输入错误提示使用中文区域设置和本地化器。

保留地图/素材/脚本文件名、脚本命令和参数、跳转目标、颜色标记、变量、协议字段及编号。旧英文物品/怪物引用在原名称查找失败后才通过中文别名查找；NPC 怪物计数、宠物检查/移除和清怪条件同样兼容旧名称。怪物改名时保留原掉落文件名；职业/等级装备的名称前缀族和变体数字后缀保持原版语义。不改变玩家的账户名、角色名、行会名、自定义职位或玩家资产。

NPC 按身份使用自然中文称呼，如李铁匠、张大婶、王屠户、刘镖头；怪物根据原素材及习惯命名，如雪原巨人、赤狐妖、千年尸霸。商人、军人、道长的对话按角色口吻撰写，不逐个颜色片段拼接句子。纪念人物保留真实身份。任务所需数量、奖励、费用、时限和坐标以原指令为准，不随润色改动。

译表同时包含原英文与上一版中文的直接升级映射；已汉化世界可使用同一入口再次迁移。完整 `#SAY`、任务描述段优先匹配译文，跨颜色标记重排行文后，仍按原顺序校验颜色、变量与跳转。原版的 `ManectricClub`、`ManectricHammer` 同时用于物品与怪物；只有这类歧义物品使用 `Item:` 译表键，物品查找通过 `ItemName/ItemAlias` 得到魔雷战棍/战锤，不误用怪物的棍卫/锤卫名称。

微端随包携带 Noto Sans CJK SC Regular 2.004（约16 MB，SIL OFL 1.1），界面主题和场景 `ThemeDB.FallbackFont` 共用字体，避免系统回退缺字导致“杂货”等汉字显示为方框。字体及许可证位于 `src/Client/Fonts/`，发布说明同时收录在 `THIRD-PARTY-NOTICES.md`；不依赖服务端游戏素材库提供字形。字体变更后须重开微端，运行中的客户端不会热换字体。

## 迁移

先构建 Release，并通过管理端正常保存停止游戏，然后正常退出对应服务端进程；不要强杀，也不要复制运行中的 SQLite 主文件。迁移入口与服务器共用版本实例锁，正在运行的版本会拒绝迁移。

```bash
dotnet build Mir2.sln -c Release
MIR2_DATA_ROOT="$PWD/data/versions/default" \
  dotnet src/Server/bin/Release/net8.0/Mir2.Server.dll --translate-chinese --check
MIR2_DATA_ROOT="$PWD/data/versions/default" \
  dotnet src/Server/bin/Release/net8.0/Mir2.Server.dll --translate-chinese
./start-server.sh
./start-admin.sh
```

`--check` 只规划译文，不写世界快照或文本。迁移拒绝缺译的定义/正文、物品或怪物译后名称冲突、脚本跳转/变量/颜色变化和 world 内的符号链接。它为本次变更创建 `runtime/backups/chinese-<UTC时间>/mir2.sqlite3` 和完整 `world/` 备份，原子替换文本，最后在同一个 SQLite 事务中保存世界快照。账户、行会等其他快照不参与迁移；成功后重复执行不再改写已翻译内容。

若写入抛出异常，SQLite 回滚，并从本次备份恢复已改文件及内存世界。备份逐字保留 Windows 导入中的特殊文件名。跨文件更新没有断电恢复日志，不能承诺操作系统崩溃或断电时自动恢复。

## 恢复

正常保存停服并退出对应服务端/管理端；将当前 runtime 数据库和 world 另行保留，避免覆盖后来产生的玩家进度。确认所选备份的 SQLite `PRAGMA integrity_check` 为 `ok` 后，将完整备份数据库和 world 一起恢复到该版本，并处理仅属于旧数据库的 WAL/SHM 文件。恢复数据库会回到备份时的全部玩家状态，不能在运行中替换，也不要只恢复世界快照而留下不匹配的脚本。重新启动后核对世界数量、账户、物品、资源版本和日志。

## 检查入口

```bash
MIR2_DATA_ROOT="$PWD/data/versions/default" \
  dotnet run --project tests/Compatibility/Compatibility.csproj -c Release -- --chinese-checks
```

该检查将世界文本复制到临时版本，读取不可变的原始导入世界定义，验证预检不写入、SQLite 注入故障后的文件/数据库撤回、完整备份、编号/属性/坐标/路径不变、原名和大小写/空格兼容、玩家快照不变、缺译拒绝、标记保护及幂等。另核对所有 NPC/任务/系统脚本的非显示指令、条件、奖励和参数不变，以及原英文/旧中文整段对话升级、歧义物品名称与纪念人物身份。它不读写默认版本的玩家存档。

真实 Godot 可在已启动的独立汉化测试版本上运行 `--reference-world --chinese-world`，验证原世界地图名、初始物品、NPC 名称及实际对话，并通过 `--screenshot=<绝对路径>` 留图。此模式创建测试账号，只能用于隔离版本。常规 `tests/Compatibility/smoke.sh` 另行覆盖游玩及重启；`tests/Management/ui.sh` 覆盖管理 CRUD、中文控件与资源/NPC/刷怪交互。

`--chinese-world` 同时检查字体与场景共用、常见名称及“购买杂货/出售杂货”实际渲染；可加 `--font-characters=<UTF-8文本文件>` 检查整份词表的汉字。检查暂时关闭系统字形回退，确认随包字体自身覆盖字形，之后恢复原设置。

原数据 `Envir/Drops/HiGreatGhoul.txt` 的 `1/1RedDagger Q` 行存在格式错误，迁移前后均由引擎提示，汉化未改写掉落规则。
