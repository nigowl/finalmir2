#nullable enable
using System.Security.Cryptography;
using System.Text;

namespace Mir2.Companions;

public static class CompanionScheduling
{
    public static void Validate(CompanionSchedule s)
    {
        _ = Zone(s.TimeZone);
        if (s.Windows == null || s.Windows.Length is < 1 or > 14 || s.Attendance is < 0 or > 100 || s.JitterMinutes is < 0 or > 120 ||
            s.SessionMinutes is < 1 or > 720 || s.RestMinutes is < 0 or > 720 || s.DailyMinutes is < 1 or > 1440)
            throw new InvalidDataException("在线日程、出勤概率或时长无效。");
        if (s.Windows.Any(w => w == null || w.Days is < 1 or > 127 || w.StartMinute is < 0 or > 1439 || w.EndMinute is < 0 or > 1439 || !w.AllDay && w.StartMinute == w.EndMinute))
            throw new InvalidDataException("在线窗口必须有星期、有效开始/结束时间；全天请明确勾选。");
    }
    public static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
        { throw new InvalidDataException("不支持的在线时区。", e); }
    }
    public static string Day(CompanionSchedule s, DateTimeOffset now) => TimeZoneInfo.ConvertTime(now, Zone(s.TimeZone)).ToString("yyyy-MM-dd");
    public static SchedulePreview[] Preview(CompanionSchedule s, int seed, DateTimeOffset now, int days = 7)
    {
        Validate(s); var zone = Zone(s.TimeZone);
        var local = TimeZoneInfo.ConvertTime(now, zone).Date;
        var result = new List<SchedulePreview>();
        for (var day = -1; day < days; day++)
        {
            var date = local.AddDays(day);
            for (var i = 0; i < s.Windows.Length; i++)
            {
                var w = s.Windows[i]; if ((w.Days & (1 << (int)date.DayOfWeek)) == 0) continue;
                var key = $"{date:yyyy-MM-dd}/{i}/{w.StartMinute}/{w.EndMinute}/{w.Days}/{w.AllDay}";
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed + "/" + key));
                var start = w.AllDay ? date : date.AddMinutes(w.StartMinute);
                var end = w.AllDay ? date.AddDays(1) : date.AddMinutes(w.EndMinute).AddDays(w.EndMinute <= w.StartMinute ? 1 : 0);
                var from = Utc(start, zone, false).AddMinutes(BitConverter.ToUInt32(hash, 4) % (uint)(s.JitterMinutes + 1));
                var until = Utc(end, zone, true);
                if (until > now && until > from) result.Add(new(from, until, BitConverter.ToUInt32(hash, 0) % 100 < s.Attendance, key));
            }
        }
        return result.OrderBy(x => x.Start).ToArray();
    }
    private static DateTimeOffset Utc(DateTime value, TimeZoneInfo zone, bool end)
    {
        value = DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(value)) value = value.AddMinutes(1);
        if (zone.IsAmbiguousTime(value))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(value);
            return new DateTimeOffset(value, end ? offsets.Min() : offsets.Max()).ToUniversalTime();
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(value, zone), TimeSpan.Zero);
    }
}
