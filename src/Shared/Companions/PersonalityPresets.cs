#nullable enable
namespace Mir2.Companions;

public static class PersonalityPresets
{
    public static readonly PersonalityPreset[] All =
    [
        new("balanced", 1, "随性玩家", "任务、练级、社交均衡。", new()),
        new("laid_back", 1, "懒惰玩家", "偏好近处短目标，完成后常休息。", new(20,15,25,40,40,20,50,50,25)),
        new("diligent", 1, "勤奋练级者", "持续推进任务和经验，补给后继续。", new(90,25,45,35,45,35,50,50,85)),
        new("pk_fanatic", 1, "PK 狂", "备战后优先寻找允许的对抗。", new(65,95,80,55,20,45,50,50,65,60)),
        new("helpful", 1, "热心队友", "愿意等待、组队和帮助队友。", new(65,15,40,80,95,40,50,50,50,65)),
        new("explorer", 1, "探索者", "探索可达区域，尝试新路线。", new(60,20,65,45,40,95)),
        new("cautious", 1, "稳健老手", "谨慎试怪，留足补给并及时撤退。", new(65,10,15,35,55,25,75)),
        new("collector", 1, "装备收藏者", "根据装备缺口选择打宝目标。", new(70,25,50,45,35,65,50,95,75)),
        new("frugal", 1, "节俭打宝者", "重视扣除药费后的实际收益。", new(75,15,30,40,35,35,95,60)),
        new("social", 1, "社交达人", "主动邀约和聊天，遵守冷却。", new(40,15,35,95,70,50,50,50,50,90)),
        new("solo", 1, "独行侠", "偏好独立路线，较少主动社交。", new(75,40,65,10,20,60,50,50,50,15)),
        new("competitive", 1, "竞技好手", "重视战力准备和团队配合。", new(80,75,60,60,60,40,50,50,80))
    ];
    public static readonly string[] TraitNames = ["勤奋", "对抗", "风险", "社交", "助人", "探索", "节俭", "收集", "坚持", "健谈"];
    public static int[] Values(Personality p) => [p.Diligence,p.Aggression,p.Risk,p.Social,p.Helpfulness,p.Exploration,p.Frugality,p.Collection,p.Persistence,p.Talkative];
    public static Personality From(int[] v) => v.Length == 10 ? new(v[0],v[1],v[2],v[3],v[4],v[5],v[6],v[7],v[8],v[9]) : throw new InvalidDataException("人格必须包含十个维度。");
}
