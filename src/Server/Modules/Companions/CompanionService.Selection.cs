using Mir2.Companions;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    public int CommandSelected(CompanionSelectedCommand request)
    {
        if (request == null || request.Action is not ("start" or "stop")) throw new InvalidDataException("未知批量操作。");
        var now = DateTimeOffset.UtcNow;
        return store.ChangeSelected(request.Profiles, value =>
        {
            if (request.Action == "start")
            {
                if (value.Error.Length > 0 || value.SessionDeaths >= value.Profile.DeathLimit)
                    throw new InvalidOperationException($"「{value.Profile.Name}」存在错误或死亡预算保护，请先逐个核对；本批次未执行。");
                if (value.CharacterId == 0 && !CompanionValidation.CanCreateClass(value.Profile.Class))
                    throw new InvalidOperationException($"「{value.Profile.Name}」的职业当前未开放创建。");
                value.Profile = value.Profile with { Enabled = true };
                value.Desired = "run"; value.RecoveryDisabled = false; value.SuppressedUntil = null;
            }
            else
            {
                value.Desired = "off"; value.RecoveryDisabled = true; value.RecoveryAt = null;
                value.SuppressedUntil = CompanionScheduling.Preview(value.Profile.Schedule, value.Seed, now)
                    .FirstOrDefault(w => w.Start <= now && w.End > now)?.End ?? now.AddMinutes(value.Profile.Schedule.RestMinutes);
            }
        });
    }

    public int ScheduleSelected(CompanionSelectedSchedule request)
    {
        if (request?.Schedule == null) throw new InvalidDataException("请选择有效日程。");
        if (!request.RandomSchedule) CompanionScheduling.Validate(request.Schedule);
        var presets = store.Schedules;
        if (request.RandomSchedule && presets.Length == 0) throw new InvalidDataException("没有可用的日程预设，请先在日程管理中新增。");
        return store.ChangeSelected(request.Profiles, value => value.Profile = value.Profile with
        { Scheduled = request.Scheduled, Schedule = request.RandomSchedule ? CompanionSchedulePresets.Pick(presets) : CompanionSchedulePresets.Copy(request.Schedule) });
    }

    public int LlmSelected(CompanionSelectedLlm request)
    {
        if (request == null || request.Mode is < 0 or > 2) throw new InvalidDataException("请选择有效的LLM增强模式。");
        if (request.Mode > 0 && CompanionModels.Select(store.Configuration, request.ModelId) == null)
            throw new InvalidDataException("请选择当前版本已启用的模型，或先配置默认模型。");
        return store.ChangeSelected(request.Profiles, value => value.Profile = value.Profile with
        { LlmMode = request.Mode, LlmModelId = request.Mode == 0 ? value.Profile.LlmModelId : request.ModelId });
    }
}
