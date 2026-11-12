namespace Mir2.Commands;

public sealed record GameCommandInfo(string Name, string ShortName, string Original, string Arguments, string Example, string Description, bool Custom = false);

public static class GameCommands
{
    // 只统一命令名称；权限、参数和游戏规则仍由原引擎对应分支执行。
    public static IReadOnlyList<GameCommandInfo> All { get; } = Array.AsReadOnly(new GameCommandInfo[]
    {
        new("传送", "mm", "MAPMOVE", "地图代码 [实例号] [X Y]", "@传送 0 300 300 / @mm 0", "传送到地图；省略坐标时随机落点。原 MAPMOVE 继续可用。"),
        new("移动", "mv", "MOVE", "[X Y]", "@移动 300 300", "同图坐标移动；无坐标时随机移动。保留传送装备与地图限制。"),
        new("前往玩家", "go", "GOTO", "角色名", "@前往玩家 张三", "GM 前往在线角色所在位置。"),
        new("召回玩家", "rc", "RECALL", "角色名", "@召回玩家 张三", "GM 将在线角色召到身边。"),
        new("创建物品", "mk", "MAKE", "物品名称或编号 [数量]", "@创建物品 1 2", "创建指定物品，仍检查背包容量；GM/测试服。"),
        new("刷怪", "mb", "MOB", "怪物名称或编号 [数量] [散布范围]", "@刷怪 1 3 2", "生成怪物；保留攻城对象、宠物等原限制。"),
        new("召唤宠物", "rm", "RECALLMOB", "怪物名称或编号 [数量] [宠物等级]", "@召唤宠物 1 1 3", "生成自己的宠物；等级 0–7。"),
        new("等级", "lv", "LEVEL", "[角色名] 等级", "@等级 40 / @lv 张三 40", "设置等级；修改他人仍要求 GM。"),
        new("英雄等级", "lh", "LEVELHERO", "[角色名] 等级", "@英雄等级 40", "设置英雄等级；需已有英雄。"),
        new("给予金币", "gg", "GIVEGOLD", "[角色名] 数量", "@给予金币 1000", "给予自己或在线角色金币；保留原数量上限。"),
        new("给予真珠", "gp", "GIVEPEARLS", "[角色名] 数量", "@给予真珠 100", "给予宠物真珠；修改他人仍要求 GM。"),
        new("给予元宝", "gc", "GIVECREDIT", "[角色名] 数量", "@给予元宝 100", "给予账户元宝；修改他人仍要求 GM。"),
        new("给予技能", "gs", "GIVESKILL", "[角色名] 技能枚举 等级", "@给予技能 FireBall 3", "学习或设置技能，等级 0–3；参数沿用原技能枚举。"),
        new("删除技能", "ds", "DELETESKILL", "[角色名] 技能枚举", "@删除技能 FireBall", "GM 删除自己或在线角色的指定技能。"),
        new("无敌", "sm", "SUPERMAN", "", "@无敌", "切换无敌模式；GM/测试服。"),
        new("管理员模式", "gm", "GAMEMASTER", "", "@管理员模式", "切换原 GM 模式；不会授予账户 GM 权限。"),
        new("隐身观察", "ob", "OBSERVER", "", "@隐身观察", "GM 切换观察模式。"),
        new("管理员登录", "li", "LOGIN", "", "@管理员登录", "进入原 GM 密码认证流程，下一条聊天输入密码。"),
        new("击杀", "kl", "KILL", "[角色名]", "@击杀 张三", "GM 击杀在线角色；省略目标时作用于面前格。"),
        new("复活", "rv", "REVIVE", "[角色名]", "@复活", "GM 复活自己或指定在线角色。"),
        new("清空背包", "cb", "CLEARBAG", "[角色名]", "@清空背包", "删除目标背包物品，不可撤销；GM/测试服。"),
        new("清除状态", "bf", "CLEARBUFFS", "", "@清除状态", "原玩家命令：清除自己的 Buff。"),
        new("清理怪物", "cm", "CLEARMOB", "[地图代码]", "@清理怪物 0", "GM 杀死目标地图怪物；省略地图使用当前地图。"),
        new("重载掉落", "rd", "RELOADDROPS", "", "@重载掉落", "GM 重新读取掉落配置。"),
        new("重载脚本", "rn", "RELOADNPCS", "", "@重载脚本", "GM 重新读取 NPC 脚本。"),
        new("清除IP封锁", "ci", "CLEARIPBLOCKS", "", "@清除IP封锁", "GM 清空引擎当前 IP 阻断表。"),
        new("备份角色", "bp", "BACKUPPLAYER", "角色名", "@备份角色 张三", "GM 保存原引擎角色归档备份。"),
        new("归档角色", "ap", "ARCHIVEPLAYER", "角色名", "@归档角色 张三", "先归档再从当前列表移除；不能归档自己。"),
        new("载入角色", "lp", "LOADPLAYER", "归档文件名", "@载入角色 张三", "调用原 LOADPLAYER，要求当前角色及编号匹配；不替代完整数据库恢复。"),
        new("恢复角色", "rp", "RESTOREPLAYER", "角色名或归档文件名 [账户名]", "@恢复角色 张三", "恢复删除标记；带账户时可从原归档恢复。"),
        new("改变性别", "sx", "CHANGEGENDER", "[角色名]", "@改变性别", "切换性别，在线目标会退出角色；GM/测试服。"),
        new("改变职业", "cc", "CHANGECLASS", "[角色名] 职业枚举", "@改变职业 Wizard", "改变职业，在线目标会退出角色；沿用 Warrior/Wizard/Taoist 等枚举。"),
        new("发型", "hr", "HAIR", "[编号]", "@发型 1", "设置发型；省略编号时使用原默认值。"),
        new("装饰", "dc", "DECO", "编号", "@装饰 0", "生成原引擎装饰对象；GM/测试服。"),
        new("设置PK值", "pk", "ADJUSTPKPOINT", "[角色名] 数值", "@设置PK值 0", "设置目标 PK 值，修改他人仍要求 GM。"),
        new("装备觉醒", "aw", "AWAKENING", "装备类型 觉醒类型", "@装备觉醒 Weapon DC", "对已装备的武器/衣服/头盔觉醒；使用原枚举。"),
        new("移除觉醒", "ra", "REMOVEAWAKENING", "装备类型", "@移除觉醒 Weapon", "移除指定已装备物品的觉醒。"),
        new("查找玩家", "fd", "FIND", "角色名", "@查找玩家 张三", "查询在线角色位置，保留探测装备及冷却要求。"),
        new("对象信息", "inf", "INFO", "[角色名]", "@对象信息", "GM/测试服查询在线角色或面前对象的信息。"),
        new("触发脚本", "tr", "TRIGGER", "触发键 [角色名]", "@触发脚本 TEST 张三", "GM 调用默认 NPC Trigger；无角色名时作用于所有在线角色。"),
        new("切换标记", "sf", "SETFLAG", "标记编号", "@切换标记 1", "切换自己的脚本标记，并重算附近 NPC 可见性。"),
        new("查看标记", "lf", "LISTFLAGS", "", "@查看标记", "列出自己已开启的脚本标记。"),
        new("清除标记", "cf", "CLEARFLAGS", "[角色名]", "@清除标记", "清空目标脚本标记；修改他人仍要求 GM。"),
        new("清空任务", "cq", "CLEARQUESTS", "[角色名]", "@清空任务", "清除进行中与已完成任务；GM/测试服。"),
        new("设置任务", "sq", "SETQUEST", "任务编号 状态 [角色名]", "@设置任务 1 1", "状态 0 取消、1 完成；GM/测试服。"),
        new("允许行会邀请", "ag", "ALLOWGUILD", "", "@允许行会邀请", "原玩家命令：切换自己接收行会邀请。"),
        new("退出行会", "lg", "LEAVEGUILD", "", "@退出行会", "原玩家命令：退出行会，保留行会战限制。"),
        new("创建行会", "cg", "CREATEGUILD", "[角色名] 行会名", "@创建行会 测试行会", "创建行会；目标必须无行会，保留名称长度限制。"),
        new("发起行会战", "sw", "STARTWAR", "行会名", "@发起行会战 测试行会", "GM 对指定行会发起原行会战。"),
        new("开始攻城", "sc", "STARTCONQUEST", "攻城编号", "@开始攻城 1", "切换强制攻城状态，要求已有行会。"),
        new("重置攻城", "rs", "RESETCONQUEST", "攻城编号", "@重置攻城 1", "非战时重置指定攻城，要求已有行会。"),
        new("城门", "gt", "GATES", "[Open或Close]", "@城门 Close", "原行会权限命令：开关城门；无参数时切换。"),
        new("行会旗帜", "fl", "CHANGEFLAG", "[编号]", "@行会旗帜 2", "原行会权限命令：旗帜编号 0–11；省略随机。"),
        new("行会旗色", "fc", "CHANGEFLAGCOLOUR", "[R G B]", "@行会旗色 255 200 0", "原行会权限命令：设置旗色；省略随机。"),
        new("允许交易", "at", "ALLOWTRADE", "", "@允许交易", "原玩家命令：切换接收交易邀请。"),
        new("观察玩家", "ov", "OBSERVE", "角色名", "@观察玩家 张三", "观察在线角色，保留服务器和目标的观察许可。"),
        new("允许观察", "ao", "ALLOWOBSERVE", "", "@允许观察", "原玩家命令：切换自己的被观察许可。"),
        new("允许队伍召回", "ar", "ENABLEGROUPRECALL", "", "@允许队伍召回", "原玩家命令：切换接收队伍召回。"),
        new("召回队伍", "gr", "GROUPRECALL", "", "@召回队伍", "原玩家命令：保留队伍、传送装备及冷却限制。"),
        new("召回队员", "rcm", "RECALLMEMBER", "角色名", "@召回队员 张三", "原玩家命令：召回指定队友，保留原召回条件。"),
        new("召回伴侣", "rl", "RECALLLOVER", "", "@召回伴侣", "原玩家命令：保留婚姻、戒指、许可和冷却限制。"),
        new("扩展背包", "bi", "ADDINVENTORY", "", "@扩展背包", "原玩家付费命令：花金币扩展背包。"),
        new("扩展仓库", "bs", "ADDSTORAGE", "", "@扩展仓库", "原玩家付费命令：花 100 万金币扩展/续期仓库 10 天。"),
        new("召唤英雄", "sh", "SUMMONHERO", "", "@召唤英雄", "原玩家命令：召唤/收回已有英雄，保留冷却。"),
        new("骑乘", "ri", "RIDE", "", "@骑乘", "原玩家命令：切换骑乘。"),
        new("切换变身", "tf", "TOGGLETRANSFORM", "", "@切换变身", "原玩家命令：暂停/恢复已有变身状态。"),
        new("自尽", "di", "DIE", "", "@自尽", "原玩家命令：角色死亡，仍有原死亡后果。"),
        new("时间", "tm", "TIME", "", "@时间", "原玩家命令：显示服务器时间。"),
        new("投骰子", "ro", "ROLL", "", "@投骰子", "原玩家命令：在队伍中投掷原 1–5 点骰子。"),
        new("地图信息", "mp", "MAP", "", "@地图信息", "原玩家命令：显示当前地图名称及代码。"),
        new("设置计时器", "st", "SETTIMER", "键 秒数 类型", "@设置计时器 TEST 60 0", "调用原脚本计时器，类型使用原计时器编号。"),
        new("照明", "lt", "SETLIGHT", "亮度", "@照明 10", "GM 设置角色亮度，数值 0–255。"),
        new("公告", "bc", "!", "内容", "@公告 服务器维护通知 / @bc 服务器维护通知", "GM 全服公告；原 @! 内容继续可用。")
    });

    private static readonly IReadOnlyDictionary<string, string> Names = BuildNames();

    private static Dictionary<string, string> BuildNames()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var command in All)
            foreach (var name in new[] { command.Name, command.ShortName, command.Original }) names.Add(name, command.Original);
        return names;
    }

    public static string Resolve(string name) => Names.TryGetValue(name, out var original) ? original : name.ToUpperInvariant();

    public static GameCommandInfo Find(string name)
    {
        var original = Resolve(name);
        return All.FirstOrDefault(command => command.Original == original);
    }
}
