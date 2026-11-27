using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mir2.Companions;
using Mir2.Hosting;
using Server.ConsoleHost;

internal static class CompanionManagementChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var paths=new ProjectPaths(data:Path.Combine(root,"companion-management"));
        Directory.CreateDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);
        var previousStorage=Server.DatabaseStorage.Current;var previousDirectory=Directory.GetCurrentDirectory();
        try
        {
            Server.DatabaseStorage.Current=storage;Directory.SetCurrentDirectory(paths.World);
            var store=new CompanionStore(storage,new(paths,storage),paths);
            CheckDefaults(check,store);
            CheckSchedules(check,store,storage,paths);
            CheckSelection(check,store,storage,paths);
            CheckRandomAndLlm(check,store,storage,paths);
            CheckModels(check);
            CheckPageSizes(check,store,paths);
            CheckAccounts(check,store,paths);
        }
        finally{Server.DatabaseStorage.Current=previousStorage;Directory.SetCurrentDirectory(previousDirectory);}
    }
    private static void CheckAccounts(Action<bool,string> check,CompanionStore store,ProjectPaths paths)
    {
        using var service=new CompanionService(null!,store,paths);
        var profile=service.Create(new(){Name="编号规则检查",Enabled=false});
        var single=store.Get(profile.Id);
        check(System.Text.RegularExpressions.Regex.IsMatch(single.Account,"^pw[0-9]+$"),"单个新增没有使用pw数字账户");
        var batch=new CompanionProfile{Id=Guid.NewGuid(),Name="批次编号检查",Enabled=false};
        store.SaveBatch([batch],store.Settings.Nicknames);
        var account=store.Get(batch.Id).Account;
        check(System.Text.RegularExpressions.Regex.IsMatch(account,"^pw[0-9]+$")&&account!=single.Account,"批量新增账户格式或唯一性错误");
        var before=store.Records.ToDictionary(p=>p.Key,p=>JsonSerializer.Serialize(p.Value with{Account=""}));
        store.NormalizeAccounts();
        check(store.Records.Values.All(r=>System.Text.RegularExpressions.Regex.IsMatch(r.Account,"^pw[0-9]+$"))&&
            store.Records.Values.Select(r=>r.Account).Distinct(StringComparer.OrdinalIgnoreCase).Count()==store.Records.Count,"旧陪玩账户迁移格式或唯一性错误");
        check(before.All(p=>p.Value==JsonSerializer.Serialize(store.Get(p.Key) with{Account=""})),"账户迁移修改了凭据/档案/用量或保护");
        store.Mutate(store.Get(profile.Id),r=>{r.Account="MissingLegacy";r.Registered=true;r.AccountId=int.MaxValue;});
        var snapshot=JsonSerializer.Serialize(store.Records);
        try{store.NormalizeAccounts();throw new Exception("身份不匹配仍迁移账户");}
        catch(InvalidOperationException){check(snapshot==JsonSerializer.Serialize(store.Records),"迁移身份拒绝后改变账户");}
    }
    private static void CheckPageSizes(Action<bool,string> check,CompanionStore store,ProjectPaths paths)
    {
        var previous=store.Records.Values.ToArray();store.Records.Clear();
        try
        {
            for(var i=0;i<121;i++)
            {
                var profile=new CompanionProfile{Id=Guid.NewGuid(),Name=$"页长测试{i:000}",Enabled=false,Scheduled=false};
                store.Records.Add(profile.Id,new(){Profile=profile});
            }
            var service=new CompanionService(null!,store,paths);var before=JsonSerializer.Serialize(store.Records);
            foreach(var size in new[]{10,20,30,50,100})
            {
                var pages=Enumerable.Range(0,(121+size-1)/size).Select(page=>service.List(page,pageSize:size)).ToArray();
                check(pages.All(page=>page.Total==121)&&pages.SelectMany(page=>page.Items).Select(row=>row.Profile.Id).Distinct().Count()==121,
                    $"每页{size}条漏读或重复档案");
                check(pages[0].Items.Length==size&&pages[^1].Items.Length==121%size,$"每页{size}条实际截断或尾页数量错误");
            }
            check(service.List().Items.Length==20,"变更旧巡检默认页长");
            foreach(var size in new[]{0,101})
                try{service.List(pageSize:size);throw new Exception("接受非法页长");}
                catch(InvalidDataException){check(true,"非法页长拒绝");}
            check(before==JsonSerializer.Serialize(store.Records),"页长查询改变档案或调度状态");
        }
        finally{store.Records.Clear();foreach(var record in previous)store.Records.Add(record.Profile.Id,record);}
    }
    private static void CheckDefaults(Action<bool,string> check,CompanionStore store)
    {
        check(store.Schedules.Length==8&&store.Schedules.All(p=>p.Version.Length>0),"默认日程数量或版本缺失");
        check(store.Schedules.All(p=>p.Schedule.TimeZone=="Asia/Shanghai"),"预设未使用北京时间");
        var night=store.Schedules.Single(p=>p.Name.StartsWith("夜猫子")).Schedule;
        var preview=CompanionScheduling.Preview(night,1,DateTimeOffset.Parse("2026-11-26T15:00:00Z"))[0];
        check(preview.End-preview.Start==TimeSpan.FromHours(7),"夜猫子未跨午夜");
        var original=store.Schedules[0].Schedule;var copied=CompanionSchedulePresets.Copy(original);
        copied.Windows[0]=new(AllDay:true);copied.DailyMinutes=1;
        check(!CompanionSchedulePresets.Matches(copied,original)&&original.DailyMinutes==540,"日程草稿串改预设");
        var names=CompanionNames.Fill(["自定昵称",CompanionNames.All[0],"自定昵称"]);
        check(names.Length==1000&&names[0]=="自定昵称"&&names[1]==CompanionNames.All[0]&&names.Distinct().Count()==1000,"补齐昵称丢失旧名或重复");
        check(CompanionNames.All[99]=="明天见兄弟","原100昵称顺序被改变");
    }
    private static void CheckSchedules(Action<bool,string> check,CompanionStore store,SqliteDatabaseStorage storage,ProjectPaths paths)
    {
        var preset=new CompanionSchedulePreset(Guid.NewGuid(),"测试作息",new());
        store.SaveSchedule(preset,true);var saved=store.Schedules.Single(p=>p.Id==preset.Id);
        try{store.SaveSchedule(preset with{Id=Guid.NewGuid()},true);throw new Exception("重名预设被接受");}catch(InvalidDataException){check(true,"拒绝重名");}
        store.SaveSchedule(saved with{Name="更新作息"},false);
        try{store.DeleteSchedule(saved.Id,saved.Version);throw new Exception("过期删除被接受");}catch(InvalidOperationException){check(true,"拒绝过期删除");}
        var latest=store.Schedules.Single(p=>p.Id==preset.Id);
        check(new CompanionStore(storage,new(paths,storage),paths).Schedules.Any(p=>p.Name=="更新作息"),"日程重启丢失");
        store.DeleteSchedule(latest.Id,latest.Version);
        check(!store.Schedules.Any(p=>p.Id==preset.Id),"日程未删除");
        using var original=storage.Read("companions/schedules.json");using var backup=new MemoryStream();original.CopyTo(backup);
        storage.Write("companions/schedules.json",JsonSerializer.SerializeToUtf8Bytes(new CompanionEnvelope<CompanionSchedulePreset[]>(999,[])));
        var blocked=new CompanionStore(storage,new(paths,storage),paths);
        try{blocked.SaveSchedule(preset,true);throw new Exception("未知日程格式仍允许写入");}catch(InvalidOperationException){check(blocked.LoadError!=null,"未知格式未锁定");}
        using(var stream=storage.Read("companions/schedules.json"))check(JsonDocument.Parse(stream).RootElement.GetProperty("Schema").GetInt32()==999,"未知日程被覆盖");
        storage.Write("companions/schedules.json",backup.ToArray());
    }
    private static void CheckSelection(Action<bool,string> check,CompanionStore store,SqliteDatabaseStorage storage,ProjectPaths paths)
    {
        foreach(var name in new[]{"SelectedOne","SelectedTwo","Unselected"})
            store.SaveProfile(new(){Id=Guid.NewGuid(),Name=name,Scheduled=false},"account"+name,"private-password");
        var records=store.Records.Values.ToArray();var target=records[0].Profile.Id;
        foreach(var row in records)store.Mutate(row,n=>{n.OnlineSeconds[1]=123;n.RestUntil=DateTimeOffset.UtcNow.AddHours(1);n.SessionUntil=DateTimeOffset.UtcNow.AddHours(2);n.SessionDeaths=2;});
        records=store.Records.Values.ToArray();
        using var service=new CompanionService(null!,store,paths);
        CompanionSelection[] Targets()=>records.Take(2).Select(r=>new CompanionSelection(r.Profile.Id,store.Public(store.Get(r.Profile.Id)).Version)).ToArray();
        var untouched=JsonSerializer.Serialize(store.Get(records[2].Profile.Id));var configuration=JsonSerializer.Serialize(store.Configuration);
        var selections=Targets();var schedule=store.Schedules[2].Schedule;
        check(service.ScheduleSelected(new(selections,true,schedule))==2,"批量日程数量错误");
        check(store.Get(target).Profile.Scheduled&&CompanionSchedulePresets.Matches(store.Get(target).Profile.Schedule,schedule),"选中日程未更新");
        check(untouched==JsonSerializer.Serialize(store.Get(records[2].Profile.Id)),"未选角色被改动");
        check(records.Take(2).All(r=>store.Get(r.Profile.Id).SessionUntil==r.SessionUntil&&store.Get(r.Profile.Id).SessionDeaths==2&&store.Get(r.Profile.Id).OnlineSeconds[1]==123),"批量日程清除用量或计划");
        var applied=JsonSerializer.Serialize(store.Records.Values.ToArray());
        try{service.ScheduleSelected(new(selections,false,new()));throw new Exception("过期日程批量被接受");}catch(InvalidOperationException){check(applied==JsonSerializer.Serialize(store.Records.Values.ToArray()),"过期批量部分修改");}
        foreach(var bad in new[]{Array.Empty<CompanionSelection>(),new[]{Targets()[0],Targets()[0]},new[]{new CompanionSelection(Guid.NewGuid(),"")}})
            try{service.CommandSelected(new("start",bad));throw new Exception("错误选择被接受");}catch(Exception e)when(e is InvalidDataException or FileNotFoundException){check(applied==JsonSerializer.Serialize(store.Records.Values.ToArray()),"错误选择改变档案");}
        store.Mutate(store.Get(target),r=>r.Error="受保护错误");
        try{service.CommandSelected(new("start",Targets()));throw new Exception("批量上线清除了错误保护");}catch(InvalidOperationException){check(store.Get(target).Error=="受保护错误"&&store.Get(records[1].Profile.Id).Desired=="off","错误角色未整批拒绝");}
        store.Mutate(store.Get(target),r=>r.Error="");
        using var sql=new SqliteConnection($"Data Source={paths.Database};Pooling=False");sql.Open();using var trigger=sql.CreateCommand();
        trigger.CommandText=$"CREATE TRIGGER reject_selected BEFORE UPDATE ON snapshots WHEN NEW.name='companions/profiles/{records[1].Profile.Id:N}.json' BEGIN SELECT RAISE(ABORT,'selected failure'); END";trigger.ExecuteNonQuery();
        var before=JsonSerializer.Serialize(store.Records.Values.ToArray());
        try{service.CommandSelected(new("start",Targets()));throw new Exception("批量失败没有上抛");}catch(SqliteException){check(before==JsonSerializer.Serialize(store.Records.Values.ToArray()),"批量失败内存未回滚");}
        check(new CompanionStore(storage,new(paths,storage),paths).Records.Values.All(r=>r.Desired=="off"),"批量失败数据库部分提交");
        trigger.CommandText="DROP TRIGGER reject_selected";trigger.ExecuteNonQuery();
        check(service.CommandSelected(new("start",Targets()))==2,"批量上线数量错误");
        check(!store.Settings.Enabled&&store.Get(target).Desired=="run"&&store.Get(target).RestUntil==records[0].RestUntil,"批量上线改变总开关或休息");
        check(store.Get(target).SessionDeaths==2&&store.Get(target).OnlineSeconds[1]==123&&configuration==JsonSerializer.Serialize(store.Configuration),"批量上线清除死亡预算/用量或改配置");
        service.CommandSelected(new("stop",Targets()));
        check(store.Get(target).Desired=="off"&&store.Get(target).RecoveryDisabled&&store.Get(target).SuppressedUntil>DateTimeOffset.UtcNow,"批量下线没有抑制自动重连");
        check(untouched==JsonSerializer.Serialize(store.Get(records[2].Profile.Id)),"批量启停改了未选角色");
        var reopened=new CompanionStore(storage,new(paths,storage),paths);
        check(reopened.Get(target).Profile.Scheduled&&CompanionSchedulePresets.Matches(reopened.Get(target).Profile.Schedule,schedule)&&reopened.Get(target).OnlineSeconds[1]==123,"批量日程/用量重启丢失");
    }
    private static void CheckRandomAndLlm(Action<bool,string> check,CompanionStore store,SqliteDatabaseStorage storage,ProjectPaths paths)
    {
        using var service=new CompanionService(null!,store,paths);
        var ids=store.Records.Keys.Take(2).ToArray();var other=store.Records.Values.Single(r=>!ids.Contains(r.Profile.Id));
        var untouched=JsonSerializer.Serialize(other);
        CompanionSelection[] Targets()=>ids.Select(id=>new CompanionSelection(id,store.Public(store.Get(id)).Version)).ToArray();
        service.ScheduleSelected(new(Targets(),true,new(){TimeZone="invalid-ignored"},RandomSchedule:true));
        var presets=store.Schedules;var fixedPlans=ids.ToDictionary(id=>id,id=>JsonSerializer.Serialize(store.Get(id).Profile.Schedule));
        check(ids.All(id=>presets.Any(p=>CompanionSchedulePresets.Matches(p.Schedule,store.Get(id).Profile.Schedule))),"随机日程没有使用预设");
        check(!ReferenceEquals(store.Get(ids[0]).Profile.Schedule.Windows,store.Get(ids[1]).Profile.Schedule.Windows),"随机日程共享可写数组");
        var chosen=presets.First(p=>CompanionSchedulePresets.Matches(p.Schedule,store.Get(ids[0]).Profile.Schedule));
        store.SaveSchedule(chosen with{Schedule=chosen.Schedule with{DailyMinutes=111}},false);
        check(ids.All(id=>fixedPlans[id]==JsonSerializer.Serialize(store.Get(id).Profile.Schedule)),"修改预设重抽或追改了角色日程");
        var model=new CompanionLlmModel{Id=Guid.NewGuid(),Name="批量模型",Model="fixture",Endpoint="http://127.0.0.1/v1/chat/completions"};
        var disabled=model with{Id=Guid.NewGuid(),Enabled=false};
        store.SaveSettings(new(store.Settings with{Models=[model,disabled],DefaultModelId=model.Id}));
        var stale=Targets();check(service.LlmSelected(new(stale,1,model.Id))==2,"批量LLM数量错误");
        check(ids.All(id=>store.Get(id).Profile.LlmMode==1&&store.Get(id).Profile.LlmModelId==model.Id),"批量LLM模式或模型未更新");
        var before=JsonSerializer.Serialize(store.Records.Values.ToArray());
        try{service.LlmSelected(new(stale,0));throw new Exception("过期LLM设置被接受");}catch(InvalidOperationException){check(before==JsonSerializer.Serialize(store.Records.Values.ToArray()),"过期LLM部分更新");}
        foreach(var invalid in new[]{new CompanionSelectedLlm(Targets(),3),new(Targets(),1,disabled.Id),new(Targets(),1,Guid.NewGuid())})
            try{service.LlmSelected(invalid);throw new Exception("错误LLM请求被接受");}catch(InvalidDataException){check(before==JsonSerializer.Serialize(store.Records.Values.ToArray()),"无效LLM修改了角色");}
        service.LlmSelected(new(Targets(),0));
        check(ids.All(id=>store.Get(id).Profile.LlmMode==0&&store.Get(id).Profile.LlmModelId==model.Id),"关闭LLM清除了原模型指定");
        service.LlmSelected(new(Targets(),2));
        check(ids.All(id=>store.Get(id).Profile.LlmMode==2&&store.Get(id).Profile.LlmModelId==Guid.Empty),"批量LLM不能使用默认模型");
        check(untouched==JsonSerializer.Serialize(store.Get(other.Profile.Id)),"随机日程或LLM修改了未选角色");
        check(ids.All(id=>fixedPlans[id]==JsonSerializer.Serialize(store.Get(id).Profile.Schedule))&&store.Configuration.LlmCalls==0,"LLM设置改变了日程或发起请求");
        var preview=service.PreviewBatch(new(100,store.Settings.Nicknames,new(){Scheduled=true},false,false,false,RandomSchedule:true));
        check(preview.Profiles.Length==100&&preview.Profiles.All(p=>store.Schedules.Any(s=>CompanionSchedulePresets.Matches(s.Schedule,p.Schedule))),"批量新增没有逐角色使用随机预设");
        check(preview.Profiles.Skip(1).All(p=>!ReferenceEquals(p.Schedule.Windows,preview.Profiles[0].Schedule.Windows)),"批量新增随机日程共享数组");
        var created=service.CreateBatch(new(preview.Profiles,store.Settings.Nicknames,store.Settings.Version));
        store.SaveSettings(new(store.Settings with{Arrivals=store.Settings.Arrivals with{RandomSchedule=true}}));
        var reopened=new CompanionStore(storage,new(paths,storage),paths);
        check(reopened.Settings.Arrivals.RandomSchedule&&ids.All(id=>fixedPlans[id]==JsonSerializer.Serialize(reopened.Get(id).Profile.Schedule)),"重启丢失随机新增设置或重抽现有日程");
        check(created.All(p=>CompanionSchedulePresets.Matches(p.Schedule,reopened.Get(p.Id).Profile.Schedule)),"随机批次保存或重启后重新抽取");
        store.SaveBatch([],store.Settings.Nicknames.Take(999).Append("错误@昵称").ToArray());
        reopened=new CompanionStore(storage,new(paths,storage),paths);
        check(reopened.LoadError==null&&reopened.Settings.Nicknames.Length==999,"批量预览已排除的非法昵称仍落库并阻断重启");
        foreach(var preset in store.Schedules)store.DeleteSchedule(preset.Id,preset.Version);
        try{service.ScheduleSelected(new(Targets(),true,new(),RandomSchedule:true));throw new Exception("空预设仍可随机分配");}catch(InvalidDataException){check(true,"空预设拒绝");}
        try{service.PreviewBatch(new(1,store.Settings.Nicknames,new(),RandomSchedule:true));throw new Exception("空预设仍能随机创建");}catch(InvalidDataException){check(true,"空预设拒绝新增");}
    }
    private static void CheckModels(Action<bool,string> check)
    {
        check(CompanionModelEndpoints.Models("https://example.com")=="https://example.com/v1/models","根地址未补v1");
        check(CompanionModelEndpoints.Models("https://example.com/api/chat/completions?x=1")=="https://example.com/api/models?x=1","完整接口未转模型列表");
        check(CompanionModelEndpoints.Chat("https://example.com/v1/")=="https://example.com/v1/chat/completions","v1被重复追加");
        foreach(var value in new[]{"file:///tmp/test","https://key@example.com","https://example.com/#secret","not-url"})
            try{CompanionModelEndpoints.Models(value);throw new Exception("非法地址被接受");}catch(InvalidDataException){check(true,"拒绝非法地址");}
        var model=new CompanionLlmModel{Id=Guid.NewGuid(),Name="测试",Endpoint="https://example.com/v1/chat/completions",Model="a"};
        var old=new CompanionConfiguration(new(){Models=[model]},"","test",default,0,new(){{model.Id,"private-key"}});
        check(CompanionModels.SavedKey(old,model.Id,"https://example.com/v1")=="private-key","同一API无法复用密钥");
        try{CompanionModels.SavedKey(old,model.Id,"https://other.example.com/v1");throw new Exception("旧密钥发送到其他地址");}catch(InvalidDataException e){check(!e.Message.Contains("private-key"),"错误泄露密钥");}
        var changed=old.Settings with{Models=[model with{Endpoint="https://other.example.com/v1"}]};
        try{CompanionModels.Apply(old,new(changed),"test");throw new Exception("保存新地址复用旧密钥");}catch(InvalidDataException){check(true,"拒绝跨地址凭据复用");}
        check(CompanionModels.Apply(old,new(changed,ModelKeys:[new(model.Id,Clear:true)]),"test").ModelKeys![model.Id]=="","清除密钥不能更换地址");
    }
}
