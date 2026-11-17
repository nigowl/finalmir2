# NPC 模板字符

模板由原服务端在执行 `#SAY`、`#ELSESAY` 及玩家/怪物的条件、动作参数时读取实际状态。客户端不推测生命、装备或行会数据。未改变原 TCP 包、脚本跳转、交易权限或存档。

## 写法和边界

- `<$HP>`、`<$hp>` 大小写不敏感；同一行可紧邻、重复或夹在中文中。
- 函数参数允许首尾空白，例如 `<$CONQUESTGUARD( A10, %A11 )>`。
- `OUTPUT` 使用原脚本变量，支持多位编号及可选 `%`：`<$OUTPUT(A10)>`、`<$OUTPUT(%a10)>`。未定义仍显示原变量引用 `%A10`，已定义为空则显示空字符串。
- 攻城函数接受实际数字编号或变量编号；地图计数接受原地图代码或存有地图代码的变量，地图代码内部空格不被拆散。
- 每个原始模板只替换一次，变量内容中包含的 `<$...>` 不会递归执行。
- 未知名称、错误参数数量或格式保留原文便于诊断，不抛异常，也不阻断同一行后续合法模板。
- `CLASS`、`MAPLIGHT` 保留原枚举字符串供脚本比较和动作使用；`MAP` 是地图代码而非显示名称。

## 玩家模板全表（54 类）

| 分组 | 模板 | 实际来源 |
| --- | --- | --- |
| 身份 | `NPCNAME`、`USERNAME`、`LEVEL`、`CLASS` | 当前交互 NPC、角色姓名、等级、职业；NPC 名中的下划线显示为空格 |
| 地图 | `MAP`、`X_COORD`、`Y_COORD`、`MAPLIGHT` | 当前地图代码、坐标、世界光照 |
| 生命/魔力 | `HP`、`MAXHP`、`MP`、`MAXMP` | 原当前值与原属性上限 |
| 账户 | `GAMEGOLD`、`CREDIT` | 原账户金币、元宝 |
| 装备 | `ARMOUR`、`WEAPON`、`RING_L`、`RING_R`、`BRACELET_L`、`BRACELET_R`、`NECKLACE`、`BELT`、`BOOTS`、`HELMET`、`AMULET`、`STONE`、`TORCH` | 原装备槽；空槽使用已有本地化“未装备”提示 |
| 坐骑 | `MOUNT`、`MOUNTLOYALTY` | 原坐骑、当前/最大忠诚度；未装备时使用原提示 |
| 其他 | `DATE`、`USERCOUNT`、`PKPOINT`、`PARCELAMOUNT`、`ROLLRESULT` | 服务端日期、在线数、PK值、待领取邮件数、当前 NPC 掷点结果 |
| 行会 | `GUILDNAME`、`AGITGUILDNAME`、`GUILDWARTIME`、`GUILDWARFEE` | 原行会与战争设置；无行会保留原提示 |
| 行会领地 | `GUILDEXTENDFEE`、`GUILDRENTFEE`、`GUILDGTRENTALDAYSLEFT` | 原行会租期及续租/租用设置 |
| 变量 | `OUTPUT(变量)` | 原 NPCVar，不另存一份变量 |
| 地图计数 | `MONSTERCOUNT(地图代码)` | 原地图的 MonsterCount；地图不存在返回原 `N/A` |
| 城池 | `CONQUESTOWNER(城池)`、`CONQUESTGOLD(城池)`、`CONQUESTRATE(城池)`、`CONQUESTSCHEDULE(城池)` | 原城主行会、城池资金、NPC税率及申请攻城的行会；不存在时保留原空值/提示语义 |
| 城防 | `CONQUESTGUARD(城池,守卫)`、`CONQUESTGATE(城池,城门)`、`CONQUESTWALL(城池,城墙)`、`CONQUESTSIEGE(城池,攻城器械)` | 原名称、存活/修理状态及实际修理费用；对象不存在返回原提示 |
| 旧脚本名称 | `USERWEAPON` | `WEAPON` 的原装备槽别名 |
| 旧精炼说明 | `UPGRADEWEAPONFEE` | 当前装备武器按原 `PlayerObject.RefineCost` 计算的精炼费用；实际提交仍按所选物品验证和扣款 |
| 旧攻城说明 | `CASTLEWARDATE` | 当前版本实际排期中最近一场的 `yyyy-MM-dd HH:mm`；无排期时显示“暂无战争安排”。无城池参数，不能表示指定沙巴克或固定“两天后”；已开始的强制攻城读取原开始时间 |

表中的名称均放入 `<$...>` 使用。

## 怪物模板全表（13 类）

`OUTPUT(变量)`、`USERNAME`、`LEVEL`、`MAP`、`X_COORD`、`Y_COORD`、`HP`、`MAXHP`、`DATE`、`MAPLIGHT`、`USERCOUNT`、`GUILDWARTIME`、`GUILDWARFEE`。姓名、等级、位置和生命读取当前执行脚本的原怪物对象。

## 其他原脚本标记

- `{文字/RED}` 等颜色标记和 `<购买/@Buy>` 按钮不由变量解析器改写。
- `<$ITEM:编号>`、`<$MONSTER:编号>`、`<$NPC:编号>` 继续由 `LinkFormatter` 生成原实体链接并下发必要元数据；未知编号仍保留可诊断的编号。
- 不全局替换 `|`：它也用于实体链接，生命文案只在对应脚本中改成 `/`。

示例：

```text
{<$USERNAME>/KHAKI}，来啦！我就是{<$NPCNAME>/KHAKI}。
生命：{<$HP>/<$MAXHP>/RED}　魔力：{<$MP>/<$MAXMP>/ROYALBLUE}
当前佩戴：{左：<$RING_L>　右：<$RING_R>/CORAL}
<购买/@Buy> <返回/@Main>
```

## 关键验证与生效

`dotnet run --project tests/Compatibility/Compatibility.csproj -- --npc-template-checks` 验证全部原玩家/怪物模板、旧脚本名称、空值/错误边界、Check/Act/Say 调用及商店/修理会话。

已有原生隔离 fixture 可使用 `--verify-npc-templates`；角色离商人太远时加 `--npc-location=X,Y`，坐标应为商人附近可行走格。检查真实 TCP 对话、Godot 文本及购买/返回链接，不发起购买或修改正式数据。

本次仅对 default 的17份 NPC 显示脚本通过 `DevelopmentFiles.Save` 做版本检查、原文备份及原子替换；19条源码译文同步更新，不改原翻译键、脚本模板/跳转或 SQLite。解析器修复须重启服务端宿主生效；仅重载脚本不能更新已加载的程序集。运行中的正式宿主本轮未重启。
