using Mir2.Companions;
using Mir2.Management;

namespace Server.ConsoleHost;

public static class CompanionModule
{
    public static void MapCompanions(this RouteGroupBuilder management)
    {
        var group=management.MapGroup("/companions");
        group.MapGet("/world-checks",(GameRuntime runtime,CancellationToken token)=>runtime.Invoke(CompanionWorldChecks.Read,token));
        group.MapGet("/overview",(GameRuntime runtime,CompanionService service,CancellationToken token)=>runtime.Invoke(service.Overview,token));
        group.MapGet("",(GameRuntime runtime,CompanionService service,int? page,string? search,CancellationToken token)=>runtime.Invoke(()=>service.List(page??0,search),token));
        group.MapGet("/{id:guid}",(GameRuntime runtime,CompanionService service,Guid id,CancellationToken token)=>runtime.Invoke(()=>service.Detail(id),token));
        group.MapPost("",(GameRuntime runtime,CompanionService service,CompanionProfile value,CancellationToken token)=>runtime.Invoke(()=>service.Create(value),token));
        group.MapPut("/{id:guid}",(GameRuntime runtime,CompanionService service,Guid id,CompanionProfile value,CancellationToken token)=>runtime.Invoke(()=>
        {if(id!=value.Id)throw new InvalidDataException("陪玩编号不匹配。");return service.Save(value);},token));
        group.MapPost("/{id:guid}/command",async(GameRuntime runtime,CompanionService service,Guid id,CompanionCommand value,CancellationToken token)=>
        {await runtime.Invoke(()=>{service.Command(id,value);return true;},token);return new ActionResult("陪玩操作已保存，运行状态将随后更新。");});
        group.MapPut("/settings",async(GameRuntime runtime,CompanionStore store,CompanionSettingsEdit value,CancellationToken token)=>
        {await runtime.Invoke(()=>{CompanionValidation.Settings(value);store.SaveSettings(value);return true;},token);return new ActionResult("本版本陪玩设置已备份并保存。");});
        group.MapPost("/models/test",(CompanionService service,CompanionLlmTest value,CancellationToken token)=>service.TestModel(value,token));
        group.MapPost("/batch-preview",(GameRuntime runtime,CompanionService service,CompanionBatchPlan value,CancellationToken token)=>runtime.Invoke(()=>service.PreviewBatch(value),token));
        group.MapPost("/batch",(GameRuntime runtime,CompanionService service,CompanionBatchCreate value,CancellationToken token)=>runtime.Invoke(()=>service.CreateBatch(value),token));
        group.MapPost("/presets",async(GameRuntime runtime,CompanionStore store,PersonalityPreset value,CancellationToken token)=>
        {await runtime.Invoke(()=>{CompanionValidation.Preset(value,store);store.SavePreset(value);return true;},token);return new ActionResult("自定义人格修订已保存。");});
        group.MapPost("/presets/{key}/{revision:int}/archive",async(GameRuntime runtime,CompanionStore store,string key,int revision,bool archived,CancellationToken token)=>
        {await runtime.Invoke(()=>{store.ArchivePreset(key,revision,archived);return true;},token);return new ActionResult(archived?"人格修订已归档，原角色仍可使用。":"人格修订已恢复。");});
        group.MapPost("/schedule-preview",(GameRuntime runtime,CompanionStore store,CompanionProfile value,CancellationToken token)=>runtime.Invoke(()=>
            CompanionScheduling.Preview(value.Schedule,store.Records.GetValueOrDefault(value.Id)?.Seed??value.Id.GetHashCode(),DateTimeOffset.UtcNow),token));
        group.MapPost("/start-all",async(GameRuntime runtime,CompanionService service,CompanionCommand value,CancellationToken token)=>
        {
            var count=await runtime.Invoke(()=>service.StartAll(value),token);
            return new ActionResult($"已为 {count} 个已启用陪玩提交上线，按在线上限、日程和原服限制排队。");
        });
        group.MapPost("/stop-all",async(GameRuntime runtime,CompanionService service,CompanionStore store,CancellationToken token)=>
        {
            await runtime.Invoke(()=>
            {
                var settings=store.Settings;store.SaveSettings(new(settings with{Enabled=false}));
                return true;
            },token);
            return new ActionResult("已关闭本版本总开关并请求全部陪玩下线。");
        });
    }
}
