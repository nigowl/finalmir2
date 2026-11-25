using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class GuildWarChecks
{
    public static void Run(Action<bool, string> check)
    {
        var world = Envir.Main; var oldCost = Settings.Guild_WarCost; var oldNewbie = Settings.NewbieGuild;
        var map = new Map(new MapInfo { Index = 88010 });
        var leader = new WarPlayer { Info = new CharacterInfo { Index = 88011, Name = "WarLeaderA" }, CurrentMap = map, CurrentLocation = new(10, 10) };
        var enemy = new WarPlayer { Info = new CharacterInfo { Index = 88012, Name = "WarLeaderB" }, CurrentMap = map, CurrentLocation = new(11, 10) };
        var guild = new GuildObject(new GuildInfo(leader, "WarGuildA") { GuildIndex = 88011, Gold = 5000 });
        var target = new GuildObject(new GuildInfo(enemy, "WarGuildB") { GuildIndex = 88012 });
        leader.MyGuild = guild; leader.MyGuildRank = guild.Ranks[0]; enemy.MyGuild = target; enemy.MyGuildRank = target.Ranks[0];
        var npc = (NPCObject)RuntimeHelpers.GetUninitializedObject(typeof(NPCObject));
        typeof(MapObject).GetField(nameof(MapObject.ObjectID), BindingFlags.Public | BindingFlags.Instance)!.SetValue(npc, 88013U);
        npc.Info = new NPCInfo { Location = new(10, 10) }; npc.Visible = true; npc.VisibleLog = new() { [leader.Info.Index] = true };
        map.NPCs.Add(npc);
        void Ready() { leader.NPCObjectID = npc.ObjectID; leader.NPCPage = new NPCPage(NPCScript.RequestWarKey); }
        void Rejected(string name, string reason)
        {
            var gold = guild.Gold; leader.GuildWarReturn(name);
            check(guild.Gold == gold && !guild.IsAtWar() && !target.IsAtWar(), reason);
        }
        try
        {
            Settings.Guild_WarCost = 3000; Settings.NewbieGuild = "ReservedGuild";
            Rejected(target.Name, "没有NPC会话仍可宣战");
            Ready(); leader.NPCPage = new NPCPage(NPCScript.MainKey); Rejected(target.Name, "普通NPC页面可宣战");
            Ready(); leader.NPCObjectID++; Rejected(target.Name, "错误NPC编号仍可宣战");
            Ready(); leader.CurrentLocation = new(100, 100); Rejected(target.Name, "远离NPC仍可宣战"); leader.CurrentLocation = new(10, 10);
            Ready(); leader.Dead = true; Rejected(target.Name, "死亡仍可宣战"); leader.Dead = false;
            Ready(); npc.Visible = false; Rejected(target.Name, "隐藏NPC仍可宣战"); npc.Visible = true;
            Ready(); npc.VisibleLog[leader.Info.Index] = false; Rejected(target.Name, "本人不可见NPC仍可宣战"); npc.VisibleLog[leader.Info.Index] = true;
            Ready(); leader.MyGuildRank = new GuildRank { Index = 1, Options = (GuildRankOptions)255 };
            Rejected(target.Name, "非会长凭其他权限仍可宣战"); leader.MyGuildRank = guild.Ranks[0];
            foreach (var name in new[] { "", "ab", new string('名', 21), "bad\\guild", "bad\nguild", "UnknownGuild", guild.Name })
            { Ready(); Rejected(name, "无效/未知/本行会宣战改变了资金或战争：" + name); }
            Ready(); Settings.NewbieGuild = target.Name; Rejected(target.Name, "向新手行会宣战成功"); Settings.NewbieGuild = "ReservedGuild";
            Ready(); guild.Gold = 2999; Rejected(target.Name, "行会资金不足仍扣款或开战"); guild.Gold = 5000;
            Ready(); guild.NeedSave = false; leader.GuildWarReturn(target.Name);
            check(guild.Gold == 2000 && guild.NeedSave && guild.IsEnemy(target) && target.IsEnemy(guild), "原宣战没有精确扣行会资金/标记保存或建立双向战争");
            check(leader.NPCPage == null && leader.Packets.OfType<S.GuildStorageGoldChange>().Count(p => p.Type == 2 && p.Amount == 3000) == 1,
                "宣战未消费会话或资金回执不是一笔");
            check(leader.GetNameColour(leader) == Color.Blue && leader.GetNameColour(enemy) == Color.Orange,
                "原战争本人/敌方名称颜色错误");
            leader.GuildWarReturn(target.Name);
            check(guild.Gold == 2000 && world.GuildsAtWar.Count(w => w.GuildA == guild || w.GuildB == guild) == 1, "重复确认再次扣款或创建重复战争");
            Ready(); leader.GuildWarReturn(target.Name);
            check(guild.Gold == 2000, "新NPC会话向已经开战行会重复扣款");
            var war = world.GuildsAtWar.Single(w => w.GuildA == guild || w.GuildB == guild); war.EndWar(); world.GuildsAtWar.Remove(war);
            check(!guild.IsAtWar() && !target.IsAtWar() && leader.GetNameColour(leader) == Color.White && leader.GetNameColour(enemy) == Color.White,
                "原战争结束后关系或名称颜色残留");
        }
        finally
        {
            foreach (var war in world.GuildsAtWar.Where(w => w.GuildA == guild || w.GuildB == guild).ToArray())
            { war.EndWar(); world.GuildsAtWar.Remove(war); }
            world.Guilds.Remove(guild); world.Guilds.Remove(target); Settings.Guild_WarCost = oldCost; Settings.NewbieGuild = oldNewbie;
        }
    }

    private sealed class WarPlayer : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void ReceiveChat(string text, ChatType type) { }
        public override void BroadcastInfo() { }
    }
}
