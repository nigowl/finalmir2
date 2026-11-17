using Mir2.Companions;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public static class CompanionValidation
{
    public static bool CanCreateClass(int value) => value is >= 0 and <= 4 && Server.Settings.CanCreateClass((MirClass)value);
    public static int[] AvailableClasses => Enumerable.Range(0,5).Where(CanCreateClass).ToArray();

    public static void Profile(CompanionProfile p, CompanionStore store)
    {
        if(p.Name==null||p.Alias==null||p.Notes==null||p.Tags==null||p.SpeechStyle==null||p.Goal==null||p.Schedule==null||p.PresetKey==null)
            throw new InvalidDataException("陪玩资料字段不能为空。");
        if (!Envir.IsCharacterNameValid(p.Name)) throw new InvalidDataException("昵称不符合原角色命名规则。");
        if (p.Alias.Length > 60 || p.Notes.Length > 1000 || p.Tags.Length > 120 || p.SpeechStyle.Length > 30 || p.Goal.Length > 40)
            throw new InvalidDataException("陪玩资料过长。");
        if (p.Class is < 0 or > 4 || p.Gender is < 0 or > 1 || p.LlmMode is < 0 or > 2 || p.DeathLimit is < 1 or > 20)
            throw new InvalidDataException("职业、性别、LLM 模式或死亡预算无效。");
        if (p.PkMode is not ("avoid" or "defend" or "duel")) throw new InvalidDataException("不支持的 PK 模式。");
        Guilds(p.Guilds);
        if(p.LlmMode>0&&CompanionModels.Select(store.Configuration,p.LlmModelId)==null)throw new InvalidDataException("增强模式需要选择当前版本中已启用的模型。");
        if (p.Traits == null || PersonalityPresets.Values(p.Traits).Any(x => x is < 0 or > 100)) throw new InvalidDataException("人格维度必须为 0–100。");
        if (!store.Presets.Any(x => x.Key == p.PresetKey && x.Revision == p.PresetRevision)) throw new InvalidDataException("人格预制修订不存在。");
        if (p.Schedule == null) throw new InvalidDataException("在线日程不能为空。");
        CompanionScheduling.Validate(p.Schedule);
        var old = store.Records.GetValueOrDefault(p.Id);
        if (old?.CharacterId is not > 0 && !CanCreateClass(p.Class))
            throw new InvalidDataException("当前版本未开放此职业创建，请选择已开放职业。");
        if (old?.CharacterId > 0 && (old.Profile.Name != p.Name || old.Profile.Class != p.Class || old.Profile.Gender != p.Gender))
            throw new InvalidOperationException("已创建的角色昵称、职业和性别不能通过陪玩配置修改。");
        if (store.Records.Values.Any(x => x.Profile.Id != p.Id && x.Profile.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("该版本已有同名陪玩档案。");
        if (old == null && Envir.Main.CharacterList.Any(x => x.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("该昵称已被原角色使用，请选择其他昵称。");
    }
    public static void Guilds(CompanionGuildPreferences? guild)
    {
        if (guild == null || guild.Mode is not ("off" or "join" or "create" or "auto") || guild.Name == null ||
            guild.Name.Length > 20 || guild.Name.Length is > 0 and < 3 || guild.Name != guild.Name.Trim() ||
            guild.Name.Any(c => char.IsControl(c) || c == '\\'))
            throw new InvalidDataException("行会策略无效，行会名称须为空或3–20个字符，不能含控制字符或反斜杠。");
    }
    public static void Settings(CompanionSettingsEdit edit)
    {
        var s = edit.Settings;
        if(s==null||s.LlmEndpoint==null||s.LlmModel==null)throw new InvalidDataException("陪玩系统配置不能为空。");
        if(s.Arrivals==null||s.Arrivals.Schedule==null||s.Arrivals.MinMinutes is <1 or >43200||
            s.Arrivals.MaxMinutes<s.Arrivals.MinMinutes||s.Arrivals.MaxMinutes>43200||s.Arrivals.MaxProfiles is <1 or >500)
            throw new InvalidDataException("随机新增间隔须为1分钟–1个月（30天）且最大值不小于最小值，档案上限须为1–500。");
        CompanionScheduling.Validate(s.Arrivals.Schedule);
        if (s.MaxOnline is < 1 or > 100 || s.ReservePlayers is < 1 or > 1000 || s.DailyLlmCalls is < 0 or > 100000 || s.LlmModel.Length > 200 || edit.LlmKey?.Length > 4096)
            throw new InvalidDataException("陪玩数量、真人预留或 LLM 配额无效。");
        if (s.LlmEndpoint.Length > 0 && (!Uri.TryCreate(s.LlmEndpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0))
            throw new InvalidDataException("LLM 地址必须是无内嵌凭据的 http/https URL。");
        if(s.Models==null||s.Models.Length>20||s.Models.Any(m=>m==null)||s.Models.Select(m=>m.Id).Distinct().Count()!=s.Models.Length)
            throw new InvalidDataException("模型最多20个且编号不能重复。");
        if(s.Nicknames==null||s.Nicknames.Length>1000||s.Nicknames.Any(n=>n==null||n.Length>40))throw new InvalidDataException("昵称池最多1000项，每项最多40字符。");
        foreach(var model in s.Models)Model(model);
        if(s.DefaultModelId!=Guid.Empty&&!s.Models.Any(m=>m.Id==s.DefaultModelId&&m.Enabled))throw new InvalidDataException("默认模型必须存在且已启用。");
        if(edit.ModelKeys?.Any(k=>k==null||k.Key?.Length>4096||!s.Models.Any(m=>m.Id==k.Id))==true||edit.ModelKeys?.Select(k=>k.Id).Distinct().Count()!=edit.ModelKeys?.Length)
            throw new InvalidDataException("模型密钥编辑编号或长度无效。");
    }
    public static void Model(CompanionLlmModel m)
    {
        if(m==null||m.Id==Guid.Empty||string.IsNullOrWhiteSpace(m.Name)||m.Name.Length>60||string.IsNullOrWhiteSpace(m.Model)||m.Model.Length>200||m.Endpoint==null||m.Endpoint.Length>2048||
            !Uri.TryCreate(m.Endpoint,UriKind.Absolute,out var uri)||uri.Scheme is not("http"or"https")||uri.UserInfo.Length>0)
            throw new InvalidDataException("请填写模型名称、模型标识及无内嵌凭据的完整 http/https 接口地址。");
    }
    public static void Preset(PersonalityPreset p, CompanionStore store)
    {
        if (string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 40 || p.Key.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '_') ||
            p.Revision is < 1 or > 10000 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 40 || p.Description==null || p.Description.Length > 300 || p.BuiltIn)
            throw new InvalidDataException("自定义人格名称、标识或修订无效。");
        if (p.Traits==null || PersonalityPresets.Values(p.Traits).Any(x => x is < 0 or > 100) || store.Presets.Any(x => x.Key == p.Key && x.Revision == p.Revision) || PersonalityPresets.All.Any(x => x.Key == p.Key))
            throw new InvalidDataException("人格维度或不可变修订无效，内置人格请使用新标识复制。");
        if (store.CustomPresets.Count >= 200) throw new InvalidOperationException("自定义人格修订达到 200 条上限。");
    }
}
