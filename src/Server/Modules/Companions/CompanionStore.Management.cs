using Mir2.Companions;

namespace Server.ConsoleHost;

public sealed partial class CompanionStore
{
    public CompanionSchedulePreset[] Schedules => schedules.Select(value => value with
    { Schedule = CompanionSchedulePresets.Copy(value.Schedule), Version = AdminChanges.Version(value with { Version = "" }) }).ToArray();

    public void SaveSchedule(CompanionSchedulePreset value, bool creating)
    {
        PrepareWrite(); CompanionValidation.SchedulePreset(value);
        var index = schedules.FindIndex(p => p.Id == value.Id);
        if (creating && index >= 0) throw new InvalidOperationException("该日程预设已存在，请刷新。");
        if (!creating && index < 0) throw new FileNotFoundException("日程预设已不存在。");
        if (!creating) AdminChanges.Match(Schedules[index].Version, value.Version);
        if (creating && schedules.Count >= 100 || schedules.Any(p => p.Id != value.Id && p.Name.Equals(value.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("日程预设最多100个，名称不能重复。");
        var old = schedules.ToArray();
        var saved = value with { Version = "", Schedule = CompanionSchedulePresets.Copy(value.Schedule) };
        changes.Commit(() =>
        {
            if (creating) schedules.Add(saved); else schedules[index] = saved;
            Write("companions/schedules.json", schedules.ToArray());
        }, () => { schedules.Clear(); schedules.AddRange(old); });
    }

    public void DeleteSchedule(Guid id, string version)
    {
        PrepareWrite();
        var found = Schedules.SingleOrDefault(p => p.Id == id) ?? throw new FileNotFoundException("日程预设已不存在。");
        AdminChanges.Match(found.Version, version);
        var old = schedules.ToArray();
        changes.Commit(() => { schedules.RemoveAll(p => p.Id == id); Write("companions/schedules.json", schedules.ToArray()); },
            () => { schedules.Clear(); schedules.AddRange(old); });
    }

    public int ChangeSelected(CompanionSelection[] selected, Action<CompanionRecord> change)
    {
        PrepareWrite();
        if (selected == null || selected.Length is < 1 or > 500 || selected.Any(p => p == null || p.Id == Guid.Empty) ||
            selected.Select(p => p.Id).Distinct().Count() != selected.Length)
            throw new InvalidDataException("请选择1–500个不同的陪玩角色。");
        var old = selected.Select(p => Get(p.Id)).ToArray();
        for (var i = 0; i < old.Length; i++) AdminChanges.Match(Public(old[i]).Version, selected[i].Version);
        var next = old.Select(value => value with { OnlineSeconds = new(value.OnlineSeconds) }).ToArray();
        foreach (var value in next) change(value);
        // 全部校验后一次提交；任一档案失败不能只执行半批。
        changes.Commit(() => { foreach (var value in next) { Write(Key(value.Profile.Id), value); Records[value.Profile.Id] = value; } },
            () => { foreach (var value in old) Records[value.Profile.Id] = value; });
        return next.Length;
    }
}
