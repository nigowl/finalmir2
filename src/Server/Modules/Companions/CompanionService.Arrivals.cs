using Mir2.Companions;

namespace Server.ConsoleHost;

public sealed partial class CompanionService
{
    private CompanionArrivalStatus ArrivalStatus()
    {
        var settings=store.Settings;var state=store.Configuration.Arrivals??new();
        var status=!settings.Arrivals.Enabled?"随机新增已关闭":!settings.Enabled?"总开关关闭，随机新增暂停":
            store.Records.Count>=settings.Arrivals.MaxProfiles?"达到档案上限，随机新增暂停":
            runtimeError??store.LoadError??(state.NextAt==null?"等待安排首次触发":"等待下次随机触发");
        return new(state,status,store.Records.Count);
    }
    private void PlanArrivals(DateTimeOffset now)
    {
        var settings=store.Settings;
        if(!settings.Enabled||!settings.Arrivals.Enabled||runtimeError!=null||store.LoadError!=null)return;
        var state=store.Configuration.Arrivals??new();
        if(store.Records.Count>=settings.Arrivals.MaxProfiles)
        {
            if(state.NextAt!=null)store.SaveArrivalState(state with{NextAt=null});
            return;
        }
        if(state.NextAt==null)
        {store.SaveArrivalState(state with{NextAt=CompanionScheduling.NextArrival(settings.Arrivals,now)});return;}
        if(now<state.NextAt)return;
        CompanionProfile profile;
        try
        {
            profile=PreviewBatch(new(1,settings.Nicknames,new(){Enabled=true,Scheduled=true,Schedule=settings.Arrivals.Schedule,Tags="随机加入"})).Profiles.Single();
        }
        catch(Exception error)when(error is InvalidDataException or InvalidOperationException)
        {
            store.SaveArrivalState(state with{LastAt=now,LastProfileId=null,Result=error.Message,
                NextAt=CompanionScheduling.NextArrival(settings.Arrivals,now)});return;
        }
        var next=state with{LastAt=now,LastProfileId=profile.Id,Created=state.Created+1,Result=$"已新增 {profile.Name}，按日程和名额排队",
            NextAt=store.Records.Count+1>=settings.Arrivals.MaxProfiles?null:CompanionScheduling.NextArrival(settings.Arrivals,now)};
        // 新档案与唯一下一次触发同一事务，重启既不重抽，也不重复创建。
        store.SaveBatch([profile],settings.Nicknames,next);
    }
}
