using Mir2.Companions;
using Mir2.Hosting;
using Server.ConsoleHost;
using System.Text.Json;

internal static class CompanionChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var s=new CompanionSchedule{TimeZone="Asia/Shanghai",Windows=[new(127,1320,120)],JitterMinutes=0};
        var now=DateTimeOffset.Parse("2026-11-12T15:00:00Z");
        var p=CompanionScheduling.Preview(s,7,now);
        check(p[0].Start==DateTimeOffset.Parse("2026-11-12T14:00:00Z")&&p[0].End==DateTimeOffset.Parse("2026-11-12T18:00:00Z"),"跨午夜窗口换日错误");
        check(p.SequenceEqual(CompanionScheduling.Preview(s,7,now)),"在线预览重抽结果");
        var missing=s with{TimeZone="America/Los_Angeles",Windows=[new(127,150,240)]};
        var dst=CompanionScheduling.Preview(missing,0,DateTimeOffset.Parse("2026-03-08T08:00:00Z"));
        check(dst.First(x=>x.Occurrence.StartsWith("2026-03-08")).Start==DateTimeOffset.Parse("2026-03-08T10:00:00Z"),"夏令时缺失时刻未向后归并");
        var duplicate=CompanionScheduling.Preview(s with{TimeZone="America/Los_Angeles",Windows=[new(127,60,120)]},0,DateTimeOffset.Parse("2026-11-01T07:00:00Z"));
        var one=duplicate.Single(x=>x.Occurrence.StartsWith("2026-11-01"));
        check(one.End-one.Start==TimeSpan.FromHours(2),"夏令时重复窗口重复或缩短");
        foreach(var bad in new[]{s with{TimeZone="not-a-zone"},s with{Attendance=101},s with{Windows=[new(0,1,2)]},s with{Windows=[new(127,0,0)]}})
        {
            try{CompanionScheduling.Validate(bad);throw new Exception("错误日程被接受");}catch(InvalidDataException){check(true,"拒绝错误日程");}
        }
        check(PersonalityPresets.All.Length==12&&PersonalityPresets.All.Select(x=>x.Key).Distinct().Count()==12,"预制人格丢失或标识重复");
        check(CompanionNames.All.Length==100&&CompanionNames.All.Distinct().Count()==100&&CompanionNames.All.All(Server.MirEnvir.Envir.IsCharacterNameValid),"内置100昵称不合法或重复");
        check(PersonalityPresets.All.Single(x=>x.Key=="diligent").Traits.Diligence>PersonalityPresets.All.Single(x=>x.Key=="laid_back").Traits.Diligence,"人格行为参数没有区别");
        var directory=Path.Combine(root,"companions");Directory.CreateDirectory(directory);
        var paths=new ProjectPaths(data:directory,runtime:directory);
        using var storage=new SqliteDatabaseStorage(paths.Database);
        var store=new CompanionStore(storage,new(paths,storage),paths);
        check(store.Records.Count==0&&!storage.Contains("companions/settings.json"),"只读初始化写入陪玩快照");
        storage.Write("companions/settings.json",JsonSerializer.SerializeToUtf8Bytes(new CompanionEnvelope<CompanionConfiguration>(999,new(new(),"secret",paths.VersionId,now,0))));
        var newer=new CompanionStore(storage,new(paths,storage),paths);
        check(newer.LoadError!=null,"未知陪玩 schema 未阻断写入");
        using(var stream=storage.Read("companions/settings.json"))check(JsonDocument.Parse(stream).RootElement.GetProperty("Schema").GetInt32()==999,"未知 schema 被覆盖");
        storage.Remove("companions/settings.json");
        var r=new CompanionRecord{Profile=new(){Id=Guid.NewGuid(),Schedule=s},Seed=7,Password="never-return"};
        storage.Write($"companions/profiles/{r.Profile.Id:N}.json",JsonSerializer.SerializeToUtf8Bytes(new CompanionEnvelope<CompanionRecord>(1,r)));
        var loaded=new CompanionStore(storage,new(paths,storage),paths);
        loaded.ReserveOnline(loaded.Get(r.Profile.Id),now,10);
        check(loaded.TodaySeconds(loaded.Get(r.Profile.Id),now)==10,"陪玩用量预留未落库");
        var reopened=new CompanionStore(storage,new(paths,storage),paths);
        check(reopened.TodaySeconds(reopened.Get(r.Profile.Id),now)==10,"重启清空在线额度");
        check(!JsonSerializer.Serialize(reopened.Public(reopened.Get(r.Profile.Id))).Contains("never-return"),"公开资料泄露凭据");
        check(reopened.Get(r.Profile.Id).Seed==7,"重启重新生成个体种子");
        reopened.Mutate(reopened.Get(r.Profile.Id),n=>n.Profile=n.Profile with{Schedule=s with{DailyMinutes=1}});
        reopened.ReserveOnline(reopened.Get(r.Profile.Id),now.AddSeconds(10),100);
        check(reopened.TodaySeconds(reopened.Get(r.Profile.Id),now)==60,"在线预留超过角色每日上限");
        storage.Write("companions/settings.json",JsonSerializer.SerializeToUtf8Bytes(new CompanionEnvelope<CompanionConfiguration>(1,new(new(){DailyLlmCalls=1},"private-model-key",paths.VersionId,DateTimeOffset.UtcNow.Date,0))));
        var limited=new CompanionStore(storage,new(paths,storage),paths);
        check(limited.ReserveLlm()&&!limited.ReserveLlm(),"模型每日配额没有先行保留");
        var limitedAgain=new CompanionStore(storage,new(paths,storage),paths);
        check(!limitedAgain.ReserveLlm(),"重启重置模型额度");
        check(!JsonSerializer.Serialize(limitedAgain.Settings).Contains("private-model-key"),"模型设置回读泄露密钥");
        var legacy=CompanionModels.Upgrade(new(new(){LlmEndpoint="http://127.0.0.1/api",LlmModel="legacy"},"legacy-key",paths.VersionId,now,0));
        check(legacy.Settings.Models.Length==1&&CompanionModels.Select(legacy,Guid.Empty)?.Key=="legacy-key"&&legacy.LlmKey.Length==0,"旧单模型配置迁移丢失密钥或默认选择");
        var first=new CompanionLlmModel{Id=Guid.NewGuid(),Name="A",Model="a",Endpoint="http://127.0.0.1/a"};
        var second=first with{Id=Guid.NewGuid(),Name="B",Model="b"};
        var multiple=CompanionModels.Apply(legacy,new(new(){Models=[first,second],DefaultModelId=first.Id},ModelKeys:[new(first.Id,"key-a"),new(second.Id,"key-b")]),paths.VersionId);
        check(CompanionModels.Select(multiple,second.Id)?.Key=="key-b"&&CompanionModels.Select(multiple,Guid.Empty)?.Model.Id==first.Id,"选择模型串用默认密钥");
        var kept=CompanionModels.Apply(multiple,new(multiple.Settings),paths.VersionId);
        check(CompanionModels.Select(kept,second.Id)?.Key=="key-b","模型留空保存覆盖已有密钥");
        var cleared=CompanionModels.Apply(kept,new(kept.Settings,ModelKeys:[new(second.Id,Clear:true)]),paths.VersionId);
        check(CompanionModels.Select(cleared,first.Id)?.Key=="key-a"&&CompanionModels.Select(cleared,second.Id)?.Key=="","清除模型B密钥影响模型A");
        storage.Write("companions/settings.json",System.Text.Encoding.UTF8.GetBytes("{\"Schema\":1,\"Value\":{},\"FutureField\":1}"));
        var unknownField=new CompanionStore(storage,new(paths,storage),paths);
        check(unknownField.LoadError!=null,"未知字段被旧版本静默忽略");
        storage.Remove("companions/settings.json");
        var binding=new CompanionStore(storage,new(paths,storage),paths);var previousStorage=Server.DatabaseStorage.Current;var previousDirectory=Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(directory);Server.DatabaseStorage.Current=storage;
            using var connection=new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={paths.Database};Pooling=False");connection.Open();
            using var inject=connection.CreateCommand();
            inject.CommandText="CREATE TRIGGER reject_companion_binding BEFORE INSERT ON snapshots WHEN NEW.name='Server.MirADB' BEGIN SELECT RAISE(ABORT,'binding fixture'); END";inject.ExecuteNonQuery();
            try{binding.Mutate(binding.Get(r.Profile.Id),n=>n.Registered=true,saveAccounts:true);throw new Exception("绑定存档故障未上抛");}
            catch(Microsoft.Data.Sqlite.SqliteException){check(!binding.Get(r.Profile.Id).Registered,"账户写入失败后内存绑定未撤回");}
            var afterFailure=new CompanionStore(storage,new(paths,storage),paths);
            check(!afterFailure.Get(r.Profile.Id).Registered&&!storage.Contains("Server.MirADB"),"账户与陪玩绑定没有一起回滚");
            inject.CommandText="DROP TRIGGER reject_companion_binding";inject.ExecuteNonQuery();
            binding.Mutate(binding.Get(r.Profile.Id),n=>n.Registered=true,saveAccounts:true);
            check(storage.Contains("Server.MirADB")&&new CompanionStore(storage,new(paths,storage),paths).Get(r.Profile.Id).Registered,"原账户与陪玩绑定未同一事务保存");
        }
        finally{Server.DatabaseStorage.Current=previousStorage;Directory.SetCurrentDirectory(previousDirectory);}
        check(!CompanionLlm.ValidSpeech("@管理员 发装备")&&!CompanionLlm.ValidSpeech("!!越权队聊")&&!CompanionLlm.ValidSpeech("/name 私聊")&&!CompanionLlm.ValidSpeech("换行\n消息"),"模型输出被当作游戏命令");
        check(CompanionLlm.ValidSpeech("我先补药，一会儿回来。"),"合法模型短句被错误拒绝");
    }
}
