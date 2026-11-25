using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class GuildCreationChecks
{
    public static void Run(Action<bool, string> check)
    {
        var map = new Map(new MapInfo { Index = 88100 });
        var account = new AccountInfo { Gold = 5000 };
        var token = new UserItem(new ItemInfo { Index = 88101 }) { UniqueID = 88101, Count = 3 };
        var player = new Applicant { Info = new CharacterInfo { Index = 88101, Name = "建会检查", Level = 1,
            AccountInfo = account, Inventory = [token] }, CurrentMap = map, CurrentLocation = new(10, 10) };
        var npc = (NPCObject)RuntimeHelpers.GetUninitializedObject(typeof(NPCObject));
        typeof(MapObject).GetField(nameof(MapObject.ObjectID), BindingFlags.Public | BindingFlags.Instance)!.SetValue(npc, 88102U);
        npc.Info = new NPCInfo { Location = new(10, 10) }; npc.Visible = true; npc.VisibleLog = new() { [player.Info.Index] = true }; map.NPCs.Add(npc);
        var oldLevel = Settings.Guild_RequiredLevel; var count = Envir.Main.GuildList.Count;
        void Ready() { player.CanCreateGuild = true; player.NPCObjectID = npc.ObjectID; player.NPCPage = new NPCPage(NPCScript.GuildCreateKey); }
        void Reject(string name, string reason)
        {
            player.GuildNameReturn(name);
            check(!player.CanCreateGuild && player.MyGuild == null && account.Gold == 5000 && token.Count == 3 &&
                Envir.Main.GuildList.Count == count, reason);
        }
        try
        {
            Settings.Guild_RequiredLevel = 22;
            Reject("待建行会", "没有授权仍可建会");
            Ready(); player.NPCPage = null; Reject("待建行会", "旧授权无NPC页面仍可建会");
            Ready(); player.NPCPage = new NPCPage(NPCScript.MainKey); Reject("待建行会", "普通页面仍可建会");
            Ready(); player.NPCObjectID++; Reject("待建行会", "错误NPC仍可建会");
            Ready(); player.CurrentLocation = new(100, 100); Reject("待建行会", "离开NPC后旧授权仍有效"); player.CurrentLocation = new(10, 10);
            Ready(); player.Dead = true; Reject("待建行会", "死亡仍可建会"); player.Dead = false;
            Ready(); npc.Visible = false; Reject("待建行会", "隐藏NPC仍可建会"); npc.Visible = true;
            Ready(); npc.VisibleLog[player.Info.Index] = false; Reject("待建行会", "本人不可见NPC仍可建会"); npc.VisibleLog[player.Info.Index] = true;
            foreach (var name in new[] { "", "ab", "   ", new string('名', 21), "行\\会", "行\n会", "行\0会" })
            { Ready(); Reject(name, "无效名称改变会籍或资产"); }
            Ready(); Reject("待建行会", "提交时等级不足仍扣费或建会");
            check(player.NPCPage == null, "建会提交后未消费原页面");
        }
        finally { Settings.Guild_RequiredLevel = oldLevel; }
    }

    private sealed class Applicant : PlayerObject
    {
        public override void ReceiveChat(string text, ChatType type) { }
    }
}
