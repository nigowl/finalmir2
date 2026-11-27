#nullable enable
namespace Mir2.Companions;

public static class CompanionSchedulePresets
{
    public static CompanionSchedule Pick(CompanionSchedulePreset[] presets)
    {
        if (presets.Length == 0) throw new InvalidDataException("没有可用的日程预设，请先在日程管理中新增。");
        return Copy(presets[System.Security.Cryptography.RandomNumberGenerator.GetInt32(presets.Length)].Schedule);
    }
    public static CompanionSchedule Copy(CompanionSchedule schedule) => schedule with { Windows = schedule.Windows.ToArray() };

    public static bool Matches(CompanionSchedule a, CompanionSchedule b) => a.TimeZone == b.TimeZone &&
        a.Windows.SequenceEqual(b.Windows) && a.Attendance == b.Attendance && a.JitterMinutes == b.JitterMinutes &&
        a.SessionMinutes == b.SessionMinutes && a.RestMinutes == b.RestMinutes && a.DailyMinutes == b.DailyMinutes;

    public static CompanionSchedulePreset[] Defaults() =>
    [
        Preset(1, "上班族（9–18点）", 9, 18, 540, 180, 15, 62),
        Preset(2, "未成年（18–21点）", 18, 21, 180, 90, 10),
        Preset(3, "夜猫子（22–5点）", 22, 5, 420, 180, 15),
        Preset(4, "晚间休闲（19–23点）", 19, 23, 240, 120, 10),
        Preset(5, "午休小憩（12–14点）", 12, 14, 120, 60, 5),
        Preset(6, "周末畅玩（10–23点）", 10, 23, 720, 180, 15, 65),
        Preset(7, "早鸟（5–9点）", 5, 9, 240, 120, 10),
        new(Guid.Parse("cab00000-0000-0000-0000-000000000008"), "全天轮换", new()
        { Windows = [new(AllDay: true)], JitterMinutes = 0, DailyMinutes = 1440, SessionMinutes = 240, RestMinutes = 5 })
    ];

    private static CompanionSchedulePreset Preset(int id, string name, int start, int end, int daily, int session, int rest, int days = 127) =>
        new(Guid.Parse($"cab00000-0000-0000-0000-{id:000000000000}"), name, new()
        { Windows = [new(days, start * 60, end * 60)], JitterMinutes = 0, DailyMinutes = daily, SessionMinutes = session, RestMinutes = rest });
}
