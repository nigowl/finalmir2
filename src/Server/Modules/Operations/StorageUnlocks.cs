using Server.MirEnvir;

namespace Server.ConsoleHost;

public static class StorageUnlocks
{
    public static Mir2.StorageUnlockHint[] Read() => Server.Settings.StorageQuestIds.Select((id, index) =>
    {
        var page = index + 2;
        var quest = Envir.Main.QuestInfoList.FirstOrDefault(quest => quest.Index == id);
        var npc = quest == null ? null : Envir.Main.NPCs.FirstOrDefault(npc => npc.ObjectID == quest.NpcIndex);
        var description = quest == null || npc?.CurrentMap == null ? "当前版本尚未配置开通任务，请联系管理员。" :
            $"前往{npc.CurrentMap.Info.Title}（{npc.CurrentLocation.X},{npc.CurrentLocation.Y}）找{npc.Name}，完成“{quest.Name}”（{quest.RequiredMinLevel}级起）后开通。" +
            (page > 2 ? "须先开通上一仓库；同账户角色共用。" : "同账户角色共用，永久开放。");
        return new Mir2.StorageUnlockHint(page, id, description);
    }).ToArray();
}
