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
        group.MapGet("",(GameRuntime runtime,CompanionService service,int? page,string? search,int? pageSize,string? status,CancellationToken token)=>runtime.Invoke(()=>service.List(page??0,search,pageSize??20,status),token));
        group.MapGet("/{id:guid}",(GameRuntime runtime,CompanionService service,Guid id,CancellationToken token)=>runtime.Invoke(()=>service.Detail(id),token));
        group.MapPost("/selected-command", (GameRuntime runtime, CompanionService service, CompanionSelectedCommand value, CancellationToken token) =>
            runtime.Invoke(() => new ActionResult($"已为 {service.CommandSelected(value)} 个选中角色保存{(value.Action == "start" ? "上线意愿；仍遵守总开关、日程、休息与名额" : "下线意愿；本次窗口内不自动重连")}。"), token));
        group.MapPost("/selected-schedule", (GameRuntime runtime, CompanionService service, CompanionSelectedSchedule value, CancellationToken token) =>
            runtime.Invoke(() => new ActionResult($"已更新 {service.ScheduleSelected(value)} 个选中角色的日程，历史用量与当前会话计划保留。"), token));
        group.MapPost("/selected-llm", (GameRuntime runtime, CompanionService service, CompanionSelectedLlm value, CancellationToken token) =>
            runtime.Invoke(() => new ActionResult($"已更新 {service.LlmSelected(value)} 个选中角色的LLM设置。"), token));
        group.MapGet("/schedules", (GameRuntime runtime, CompanionStore store, CancellationToken token) => runtime.Invoke(() => store.Schedules, token));
        group.MapPost("/schedules", (GameRuntime runtime, CompanionStore store, CompanionSchedulePreset value, CancellationToken token) =>
            runtime.Invoke(() => { store.SaveSchedule(value, true); return new ActionResult("日程预设已保存。"); }, token));
        group.MapPut("/schedules/{id:guid}", (GameRuntime runtime, CompanionStore store, Guid id, CompanionSchedulePreset value, CancellationToken token) =>
            runtime.Invoke(() => { if (id != value.Id) throw new InvalidDataException("日程预设编号不匹配。"); store.SaveSchedule(value, false); return new ActionResult("日程预设已保存。"); }, token));
        group.MapPost("/schedules/{id:guid}/delete", (GameRuntime runtime, CompanionStore store, Guid id, CompanionCommand value, CancellationToken token) =>
            runtime.Invoke(() => { if (value.Action != "delete") throw new InvalidDataException("未知日程操作。"); store.DeleteSchedule(id, value.Version); return new ActionResult("日程预设已删除，角色已复制的参数不变。"); }, token));
        group.MapPost("",(GameRuntime runtime,CompanionService service,CompanionProfile value,CancellationToken token)=>runtime.Invoke(()=>service.Create(value),token));
        group.MapPut("/{id:guid}",(GameRuntime runtime,CompanionService service,Guid id,CompanionProfile value,CancellationToken token)=>runtime.Invoke(()=>
        {if(id!=value.Id)throw new InvalidDataException("陪玩编号不匹配。");return service.Save(value);},token));
        group.MapPost("/{id:guid}/command",async(GameRuntime runtime,CompanionService service,Guid id,CompanionCommand value,CancellationToken token)=>
        {await runtime.Invoke(()=>{service.Command(id,value);return true;},token);return new ActionResult("陪玩操作已保存，运行状态将随后更新。");});
        group.MapPut("/settings",async(GameRuntime runtime,CompanionStore store,CompanionSettingsEdit value,CancellationToken token)=>
        {await runtime.Invoke(()=>{CompanionValidation.Settings(value);store.SaveSettings(value);return true;},token);return new ActionResult("本版本陪玩设置已备份并保存。");});
        group.MapPost("/models/test",(CompanionService service,CompanionLlmTest value,CancellationToken token)=>service.TestModel(value,token));
        group.MapPost("/models/discover", (CompanionService service, CompanionModelDiscovery value, CancellationToken token) => service.DiscoverModels(value, token));
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
            return new ActionResult($"已为 {count} 个已启用陪玩提交上线，按在线上限、日程和连接限制排队。");
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
