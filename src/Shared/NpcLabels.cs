public static class NpcLabels
{
    public static string Encode(string name, string script, bool quests) => DisplayName(name, script) + "\n" + Role(name, script, quests);
    public static string DisplayName(string name, string script)
    {
        if (name.Contains('_')) name = name[(name.LastIndexOf('_') + 1)..];
        var leaf = script.Replace('\\', '/').Split('/')[^1].ToLowerInvariant();
        if (name == "小道童" && leaf.StartsWith("apprentice", StringComparison.Ordinal))
        {
            var index = int.TryParse(leaf[10..], out var value) ? value : 1;
            var names = new[] { "清和", "清玄", "清微", "清宁", "清虚", "清远", "清尘", "清云", "清逸", "清衡", "清灵", "清隐" };
            if (index is >= 1 and <= 12) return names[index - 1] + "道童";
        }
        if (leaf.StartsWith("gm-", StringComparison.Ordinal))
        {
            var title = leaf[3..] switch {
                "bracelet" => "程玉川", "necklace" => "沈璎珞", "ring" => "顾环之", "armour" => "铁云山", "boot" => "陆踏雪",
                "belt" => "裴束云", "helmet" => "秦镇岳", "weapon" => "莫藏锋", "book" => "文知书", "stone" => "石问璞",
                "gem" => "颜琢玉", "meat" => "屠百味", "grocery" => "方百宝", "potion" => "苏青囊", "craftingmaterial" => "唐巧手",
                "fishing" => "江蓑翁", "mount" => "韩牧云", "ore" => "金开山", "script" => "玄机先生", "quest" => "沈问事",
                "awakening" => "白炼心", "manager" => "温总管", "teleporter" => "陆行舟", _ => null };
            if (title != null && (name.EndsWith("使者", StringComparison.Ordinal) || name == "总管事")) return title;
        }
        return name switch {
            "边境村告示牌" => "边境榜", "比奇城告示牌" => "比奇榜", "土城告示牌" => "土城榜", "行会告示牌" => "行会榜", "领地告示牌" => "领地榜",
            "英雄使者" => "云游子", "盟重日常使者" => "沈千秋", "牢头" => "狄牢头", "老向导" => "陆引山", "试炼老人" => "问心老人",
            "左护卫曹勇" => "曹勇", "右护卫马忠" => "马忠", "玄清的神兽" => "玄清灵兽", "怪老头" => "沧溟老人",
            "唐法师" when leaf == "archermage" => "唐玄羽",
            "秘境守门人" => "守玄老人", "怪客" => "无名客", "领地管事" => "温管事", "领地货郎" => "吴货郎", "领地传送使" => "行远使",
            "传送使者" => "陆行舟", "巧匠村传送阵" => "巧匠古阵", "引路人" => leaf switch { "proceeder30-em001" => "顾长风", "proceeder40-em002" => "裴问路", "proceeder41-em003" => "温知途", _ => "引路人" },
            "边境驿卒" => "赵行舟", "比奇驿官" => "沈远山", "林地向导" => "林知途", "奇隆驿商" => "顾行商", "石桥引路人" => "石渡川",
            "道馆行脚道人" => "玄渡道人", "蛇谷探路人" => "柳探幽", "土城军驿使" => "秦驿尘", "般若渡船泊主" => "江泊舟", "雪岭引路人" => "白踏雪",
            _ => name };
    }
    public static (string Name, string Role) Read(string value)
    {
        var split = value.IndexOf('\n');
        return split < 0 ? (value, "江湖人士") : (value[..split], value[(split + 1)..].Trim());
    }

    public static string Role(string name, string script, bool quests = false)
    {
        var file = script.Replace('\\', '/').ToLowerInvariant();
        var leaf = file.Split('/')[^1];
        bool Has(params string[] values) => values.Any(value => file.Contains(value, StringComparison.Ordinal));
        if (Has("apprais")) return "鉴定大师";
        if (Has("siegeclerk")) return "攻城报名";
        if (Has("heroguide", "heroes/")) return "英雄指引";
        if (Has("盟重日常") || name.Contains("日常")) return "日常委托";
        if (name is "卢克·汤普森" or "约翰·施瓦茨") return "先贤纪念";
        if (name is "巧匠村传送阵") return "驿站传送";
        if (name == "神秘古石") return "遗迹线索";
        var specific = leaf switch {
            "jane" => "新手指引", "sincloud" => "刺客导师", "lottery" => "猜数游戏", "examiner" => "试炼令牌", "oldmaninfinity" => "无尽试炼",
            "mongchonscout" => "醉侠轶闻", "sir.mogu" => "酒肆掌柜", "subjagationmanager" => "讨伐军务", "premium" => "秘境传送", "far" => "古文释读",
            "luke" => "禁宫守卫", "strangeman" => "古墓指引", "wickedtrader" => "黑市交易", "oldskeleton" => "遗骸线索", "master" => "避难村民",
            "subjugationlead" => "要塞军务", "soho" => "中毒伤者", "1gime" => "肉铺掌柜", "perry" => "传承轶闻", "dealer" => "材料收购",
            "mastermk" => "武学教头", "investigatortv" => "村镇巡捕", "leadtrainertv" => "道术教头", "brokencarriage" => "失物线索", "caveguidetv" => "洞窟向导",
            "merchant" => "行商锻修", "vulnerableson" => "寻亲委托", "mysteriousyu" => "秘境传送", "skeletonpile" => "遗迹线索", "gt" => "领地管事",
            "mondelegate" => "土城接引", "swampnpc" => "沼泽守卫", "lostsoul" => "游魂线索", "inspector1" => "深渊封印", "inspector" => "巡捕问案",
            "oddoldman" => "时光商人", "villagechief" => "村落长者", "ancientprajna-d2074" => "遗迹线索", "leftescort" or "rightescort" => "军营护卫",
            "excavenger" => "古窟探查", "missdo-em000" or "missmi-em000" or "missre-em000" => "试炼接引", "grim" => "隐商修衣", "conquest" => "攻城内务",
            "wanderer_charlotte" => "雪狱接引", "highpriestshinsu" => "道门灵兽", _ => null };
        if (specific != null) return specific;
        if (Has("gtstairs")) return "领地通道";
        if (Has("gtbulletinboard")) return "行会告示";
        if (Has("gtpeddler", "gtstore")) return "领地商人";
        if (Has("apprentice")) return "道门学徒";
        if (Has("gm-"))
        {
            var kind = file.Split('/')[^1][3..] switch {
                "bracelet" => "手镯", "necklace" => "项链", "ring" => "戒指", "armour" => "战甲", "boot" => "战靴", "belt" => "腰带",
                "helmet" => "头盔", "weapon" => "兵器", "book" => "秘籍", "stone" => "灵石", "gem" => "宝石", "meat" => "肉食",
                "grocery" => "杂货", "potion" => "药品", "craftingmaterial" => "材料", "fishing" => "渔具", "mount" => "坐骑", "ore" => "矿石",
                "script" => "机关", "quest" => "任务", "awakening" => "觉醒", "teleporter" => "传送", _ => "事务" };
            return "管理·" + kind;
        }
        if (Has("administrator")) return "城镇事务";
        if (Has("citytravel/", "transport", "teleport", "sailor", "gtstairs")) return "驿站传送";
        if (Has("warehouse", "storage")) return "仓库存取";
        if (Has("trustmerchant")) return "寄售商人";
        if (Has("guildterritory/", "gtadmin", "gtmerchant")) return "行会事务";
        if (Has("bountyboard", "bulletinboard", "/board") || name.Contains("告示牌")) return "告示委托";
        if (Has("wa-master")) return "战士导师";
        if (Has("trainertao", "bigtaoist", "highpriest")) return "道士导师";
        if (Has("archercaptain", "archermage")) return "弓术导师";
        if (Has("awakening", "downgrade")) return "装备锻造";
        if (Has("craft")) return "制造合成";
        if (Has("blacksmith", "weaponsmith")) return "兵器锻修";
        if (Has("drapery", "clothing", "clothes")) return "衣甲商人";
        if (Has("bracelet", "necklace", "/ring", "jewellery", "accessory", "helmets", "/helmet")) return "首饰装备";
        if (Has("potion") || name.Contains("药师")) return "药品商人";
        if (Has("butcher")) return "肉铺掌柜";
        if (Has("/book")) return "武学书商";
        if (Has("/stone", "/stones")) return "灵石商人";
        if (Has("petstore", "pearlstore")) return "宠物商人";
        if (Has("fisher", "wisefisher")) return "渔具商人";
        if (Has("stablegirl")) return "坐骑饲养";
        if (Has("grocery", "peddlar", "peddler", "pedlar", "travellingmerchant")) return "杂货商人";
        if (Has("materials")) return "材料商人";
        if (Has("hairdresser")) return "梳妆理发";
        if (Has("mirguide")) return "江湖向导";
        if (Has("prison")) return "牢狱守卫";
        if (Has("signpost")) return "道路指引";
        if (Has("pillar", "timestone", "chaosnode", "rock", "ashes", "bones", "skull", "monument")) return "遗迹线索";
        if (Has("proceeder")) return "历练指引";
        if (Has("soldier", "solider", "captain", "guard", "general", "squadleader")) return "城镇守卫";
        return quests ? "任务指引" : "江湖异闻";
    }
}
