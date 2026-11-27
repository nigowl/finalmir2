using Mir2.Companions;

namespace Server.ConsoleHost;

public sealed record CompanionModelConnection(CompanionLlmModel Model,string Key);

public static class CompanionModels
{
    private static readonly Guid LegacyId=Guid.Parse("00000000-0000-0000-0000-000000000001");
    public static CompanionConfiguration Upgrade(CompanionConfiguration source)
    {
        var settings=source.Settings;
        if(settings.Models==null||settings.LlmEndpoint==null||settings.LlmModel==null)throw new InvalidDataException("模型配置字段不能为空。");
        var keys=new Dictionary<Guid,string>(source.ModelKeys??[]);
        if(settings.Models.Length==0&&settings.LlmEndpoint.Length>0&&settings.LlmModel.Length>0)
        {
            settings=settings with{Models=[new(){Id=LegacyId,Name="原有模型",Endpoint=settings.LlmEndpoint,Model=settings.LlmModel}],DefaultModelId=LegacyId};
            keys[LegacyId]=source.LlmKey;
        }
        return source with{Settings=settings with{LlmEndpoint="",LlmModel="",HasLlmKey=false},LlmKey="",ModelKeys=keys};
    }
    public static CompanionConfiguration Apply(CompanionConfiguration old,CompanionSettingsEdit edit,string instance)
    {
        var keys=new Dictionary<Guid,string>();
        foreach(var model in edit.Settings.Models)
        {
            var replacement=edit.ModelKeys?.SingleOrDefault(k=>k.Id==model.Id);
            if (replacement?.Clear != true && replacement?.Key == null) SavedKey(old, model.Id, model.Endpoint);
            keys[model.Id]=replacement?.Clear==true?"":replacement?.Key??old.ModelKeys?.GetValueOrDefault(model.Id)??"";
        }
        return new(edit.Settings with{Version="",LlmEndpoint="",LlmModel="",HasLlmKey=false,Nicknames=CompanionNames.Normalize(edit.Settings.Nicknames),Models=edit.Settings.Models.Select(m=>m with{HasKey=false}).ToArray()},
            "",instance,old.LlmDay,old.LlmCalls,keys,old.Arrivals);
    }
    public static string SavedKey(CompanionConfiguration configuration, Guid id, string endpoint)
    {
        var key = configuration.ModelKeys?.GetValueOrDefault(id) ?? "";
        if (key.Length == 0) return key;
        var model = configuration.Settings.Models.SingleOrDefault(m => m.Id == id);
        // 已保存的凭据不能随草稿地址变化发给另一接口。
        if (model == null || CompanionModelEndpoints.Chat(model.Endpoint) != CompanionModelEndpoints.Chat(endpoint))
            throw new InvalidDataException("API地址已变更，请重新输入密钥或勾选清除密钥。");
        return key;
    }
    public static CompanionModelConnection? Select(CompanionConfiguration configuration,Guid modelId)
    {
        var effective=Upgrade(configuration);
        var id=modelId==Guid.Empty?effective.Settings.DefaultModelId:modelId;
        var model=effective.Settings.Models.FirstOrDefault(m=>m.Id==id&&m.Enabled);
        return model==null?null:new(model,effective.ModelKeys?.GetValueOrDefault(id)??"");
    }
}
