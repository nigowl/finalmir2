using Microsoft.Data.Sqlite;
using Mir2.Companions;
using Mir2.Hosting;
using Server.ConsoleHost;
using System.Text.Json;

internal static class CompanionDispatchChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var now=DateTimeOffset.Parse("2026-11-14T01:00:00Z");
        var lazy=new CompanionProfile{Traits=PersonalityPresets.All.Single(p=>p.Key=="laid_back").Traits,Schedule=new(){SessionMinutes=120,RestMinutes=30}};
        var diligent=lazy with{Traits=PersonalityPresets.All.Single(p=>p.Key=="diligent").Traits};
        var lazyPlans=Enumerable.Range(0,100).Select(seed=>CompanionScheduling.Session(lazy,true,seed)).ToArray();
        var diligentPlans=Enumerable.Range(0,100).Select(seed=>CompanionScheduling.Session(diligent,true,seed)).ToArray();
        check(lazyPlans.Max(p=>p.OnlineMinutes)<diligentPlans.Min(p=>p.OnlineMinutes),"勤奋/坚持人格未形成更长会话");
        check(lazyPlans.Average(p=>p.RestMinutes)>diligentPlans.Average(p=>p.RestMinutes),"懒惰人格没有更长休息");
        check(lazyPlans.Select(p=>p.OnlineMinutes).Distinct().Count()>10,"每次会话未随机变化");
        check(CompanionScheduling.Session(lazy,false,7)==new CompanionSessionPlan(120,30),"关闭智能调度未恢复固定时长");
        check(CompanionScheduling.Session(lazy with{Schedule=lazy.Schedule with{RestMinutes=0}},true,7).RestMinutes==0,"零休息被强制增加");
        foreach(var minutes in new[]{1,43200})
        {
            var arrival=new CompanionArrivalSettings{MinMinutes=minutes,MaxMinutes=minutes};
            check(CompanionScheduling.NextArrival(arrival,now)==now.AddMinutes(minutes),"固定1分钟/1月触发时间计算错误");
        }
        for(var i=0;i<20;i++)
        {
            var next=CompanionScheduling.NextArrival(new(){MinMinutes=1,MaxMinutes=43200},now);
            check(next>=now.AddMinutes(1)&&next<=now.AddDays(30),"随机触发超出1分钟–30天边界");
        }
        foreach(var invalid in new[]{new CompanionArrivalSettings{MinMinutes=0},new(){MinMinutes=100,MaxMinutes=99},new(){MaxMinutes=43201},new(){MaxProfiles=501},new(){Schedule=null!}})
        {
            try{CompanionValidation.Settings(new(new(){Arrivals=invalid}));throw new Exception("非法随机新增设置被接受");}
            catch(InvalidDataException){check(true,"拒绝非法随机新增设置");}
        }
        Persistence(check,root,now,lazy);
    }
    private static void Persistence(Action<bool,string> check,string root,DateTimeOffset now,CompanionProfile template)
    {
        var paths=new ProjectPaths(data:Path.Combine(root,"companion-dispatch"));Directory.CreateDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);
        var previous=Server.DatabaseStorage.Current;var directory=Directory.GetCurrentDirectory();
        try
        {
            Server.DatabaseStorage.Current=storage;Directory.SetCurrentDirectory(paths.World);
            var store=new CompanionStore(storage,new(paths,storage),paths);
            for(var i=0;i<20;i++)
            {
                var profile=template with{Id=Guid.NewGuid(),Name=$"Queue{i:00}",Enabled=true,Schedule=template.Schedule with{DailyMinutes=1440}};
                store.SaveProfile(profile);store.Mutate(store.Get(profile.Id),r=>{r.QueueSince=now;r.Seed=i;});
            }
            var first=CompanionStore.Waiting(store.Records.Values).Take(10).ToArray();
            foreach(var r in first){store.BeginSession(r,now,now.AddHours(4));store.EndSession(store.Get(r.Profile.Id),now.AddHours(1),"本次游玩结束");store.Mutate(store.Get(r.Profile.Id),v=>v.QueueSince=now.AddHours(2));}
            var next=CompanionStore.Waiting(store.Records.Values).Take(10).ToArray();
            check(next.All(r=>!first.Any(f=>f.Profile.Id==r.Profile.Id)),"20档案10名额时，已玩角色挤占更早等待者");
            var selected=next[0];store.BeginSession(selected,now.AddHours(2),now.AddHours(3));
            var running=store.Get(selected.Profile.Id);var deadline=running.SessionUntil;
            check(deadline<=now.AddHours(3)&&running.QueueSince==null,"会话未按在线窗口截断或仍保留旧排队优先级");
            var reopened=new CompanionStore(storage,new(paths,storage),paths);
            reopened.BeginSession(reopened.Get(selected.Profile.Id),now.AddHours(2).AddMinutes(1),now.AddHours(4));
            check(reopened.Get(selected.Profile.Id).SessionUntil==deadline&&reopened.Get(selected.Profile.Id).SessionNumber==running.SessionNumber,"重启重抽会话或延长本次截止时间");
            var settings=reopened.Settings with{Enabled=true,Arrivals=new(){Enabled=true,MinMinutes=1,MaxMinutes=1}};
            reopened.SaveSettings(new(settings));var timer=reopened.Configuration.Arrivals!;
            check(timer.NextAt!=null,"首次启用未安排唯一触发时间");
            var version=reopened.Settings.Version;
            reopened.SaveArrivalState(timer with{LastAt=now,Result="测试事件"});
            check(reopened.Settings.Version==version,"自动事件推进使未改设置的管理草稿过期");
            reopened.SaveSettings(new(reopened.Settings with{DailyLlmCalls=23}));
            check(reopened.Configuration.Arrivals!.NextAt==timer.NextAt,"无关设置保存重新抽取触发时间");
            var id=Guid.NewGuid();var before=reopened.Configuration;var profileToAdd=new CompanionProfile{Id=id,Name="ArrivalOne"};
            using var sql=new SqliteConnection($"Data Source={paths.Database};Pooling=False");sql.Open();using var inject=sql.CreateCommand();
            inject.CommandText="CREATE TRIGGER reject_arrival BEFORE UPDATE ON snapshots WHEN NEW.name='companions/settings.json' BEGIN SELECT RAISE(ABORT,'arrival failure'); END";inject.ExecuteNonQuery();
            try{reopened.SaveBatch([profileToAdd],reopened.Settings.Nicknames,timer with{Created=1});throw new Exception("新增和下一次事件未整体失败");}
            catch(SqliteException){check(!reopened.Records.ContainsKey(id)&&reopened.Configuration==before,"随机新增失败未撤回内存");}
            var failed=new CompanionStore(storage,new(paths,storage),paths);
            check(!failed.Records.ContainsKey(id)&&failed.Configuration.Arrivals==before.Arrivals,"随机新增与下次时间部分落库");
            inject.CommandText="DROP TRIGGER reject_arrival";inject.ExecuteNonQuery();
            reopened.SaveBatch([profileToAdd],reopened.Settings.Nicknames,timer with{Created=1});
            var saved=new CompanionStore(storage,new(paths,storage),paths);
            check(saved.Records.ContainsKey(id)&&saved.Configuration.Arrivals!.Created==1,"新档案和下一次事件未共同保存");
            check(!JsonSerializer.Serialize(saved.Settings).Contains("Password"),"调度设置泄露登录凭据");
            var copyPaths=new ProjectPaths(data:Path.Combine(root,"companion-dispatch-copy"));Directory.CreateDirectory(copyPaths.World);
            storage.Backup(copyPaths.Database);
            using(var copyStorage=new SqliteDatabaseStorage(copyPaths.Database))
            {
                var copy=new CompanionStore(copyStorage,new(copyPaths,copyStorage),copyPaths);
                check(!copy.Settings.Enabled&&copy.Configuration.Arrivals!.Created==1&&copy.Records.Count==21,"复制版本未关闭总开关或丢失随机新增计数/档案");
                var sourceTimer=saved.Configuration.Arrivals;
                Server.DatabaseStorage.Current=copyStorage;Directory.SetCurrentDirectory(copyPaths.World);
                try{copy.SaveSettings(new(copy.Settings with{Enabled=true}));}
                finally{Server.DatabaseStorage.Current=storage;Directory.SetCurrentDirectory(paths.World);}
                check(copy.Configuration.Instance==copyPaths.VersionId&&copy.Configuration.Arrivals!.NextAt>DateTimeOffset.UtcNow.AddSeconds(50),"副本启用后没有重新安排未来事件");
                check(saved.Configuration.Arrivals==sourceTimer&&saved.Settings.Enabled,"副本启用修改了原版本的计时或总开关");
                check(copy.Get(selected.Profile.Id).SessionUntil==saved.Get(selected.Profile.Id).SessionUntil,"复制版本丢失已保存会话计划");
            }
            saved.SaveSettings(new(saved.Settings with{Enabled=false}));
            check(saved.Configuration.Arrivals!.NextAt==null,"总开关关闭仍保留可直接追补的过期触发");
        }
        finally{Server.DatabaseStorage.Current=previous;Directory.SetCurrentDirectory(directory);}
    }
}
