using System.Security.Cryptography;
using Mir2.Companions;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    public CompanionBatchPreview PreviewBatch(CompanionBatchPlan request)
    {
        store.Ready();
        if(request.Count is <1 or >100||request.Template==null||request.Nicknames==null||request.Nicknames.Length>1000)
            throw new InvalidDataException("批量数量须为1–100，昵称池最多1000项。");
        if(request.Template.Schedule?.Windows==null)throw new InvalidDataException("批量在线日程不能为空。");
        if(store.Records.Count+request.Count>500)throw new InvalidOperationException("创建后将超过当前版本500个陪玩档案上限。");
        var used=Envir.Main.CharacterList.Select(c=>c.Name).Concat(store.Records.Values.Select(r=>r.Profile.Name)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var excluded=new List<string>();var names=new List<string>();
        foreach(var raw in request.Nicknames)
        {
            if(raw==null||raw.Length>40)throw new InvalidDataException("昵称候选为空或过长。");
            var name=raw.Trim();
            if(!Envir.IsCharacterNameValid(name)||!used.Add(name)){excluded.Add(name);continue;}
            names.Add(name);
        }
        if(names.Count<request.Count)throw new InvalidDataException($"有效且未被使用的昵称只有{names.Count}个，无法创建{request.Count}个陪玩，请补充昵称池。");
        var pool=names.ToArray();RandomNumberGenerator.Shuffle(pool.AsSpan());
        var presets=store.Presets.Where(p=>!p.Archived).ToArray();
        var schedules=store.Schedules;
        if(request.RandomSchedule&&schedules.Length==0)throw new InvalidDataException("没有可用的日程预设，请先在日程管理中新增。");
        var classes=CompanionValidation.AvailableClasses;
        if(request.RandomClass&&classes.Length==0)throw new InvalidDataException("当前没有启用可创建的陪玩职业，请先在游戏设置中启用职业。");
        var profiles=pool.Take(request.Count).Select(name=>
        {
            var preset=request.RandomPersonality?presets[RandomNumberGenerator.GetInt32(presets.Length)]:null;
            var p=request.Template with{Id=Guid.NewGuid(),Version="",Name=name,Class=request.RandomClass?classes[RandomNumberGenerator.GetInt32(classes.Length)]:request.Template.Class,
                Gender=request.RandomGender?RandomNumberGenerator.GetInt32(2):request.Template.Gender,Schedule=request.RandomSchedule?CompanionSchedulePresets.Pick(schedules):CompanionSchedulePresets.Copy(request.Template.Schedule),
                PresetKey=preset?.Key??request.Template.PresetKey,PresetRevision=preset?.Revision??request.Template.PresetRevision,Traits=preset?.Traits??request.Template.Traits};
            CompanionValidation.Profile(p,store);return p;
        }).ToArray();
        return new(profiles,excluded.ToArray(),names.Count);
    }
    public CompanionProfile[] CreateBatch(CompanionBatchCreate request)
    {
        store.Ready();
        if(request.Profiles==null||request.Profiles.Length is <1 or >100||request.Profiles.Any(p=>p==null||p.Id==Guid.Empty)||request.Nicknames==null||request.Nicknames.Length is <1 or >1000||request.Nicknames.Any(n=>n==null||n.Length>40))
            throw new InvalidDataException("批量资料、编号或昵称池无效。");
        if(request.Profiles.Select(p=>p.Id).Distinct().Count()!=request.Profiles.Length||request.Profiles.Select(p=>p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=request.Profiles.Length)
            throw new InvalidDataException("本批次编号或昵称重复。");
        if(request.Profiles.All(p=>store.Records.TryGetValue(p.Id,out var r)&&AdminChanges.Version(p with{Version=""})==AdminChanges.Version(r.Profile)))
            return request.Profiles.Select(p=>store.Public(store.Get(p.Id))).ToArray();
        AdminChanges.Match(store.Settings.Version,request.SettingsVersion);
        if(request.Profiles.Any(p=>store.Records.ContainsKey(p.Id))||store.Records.Count+request.Profiles.Length>500)throw new InvalidOperationException("批次已部分存在或超过档案上限，请重新预览。");
        foreach(var profile in request.Profiles)CompanionValidation.Profile(profile,store);
        store.SaveBatch(request.Profiles,request.Nicknames);
        return request.Profiles.Select(p=>store.Public(store.Get(p.Id))).ToArray();
    }
}
