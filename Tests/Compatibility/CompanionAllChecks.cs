using System.Text.Json;
using Microsoft.Data.Sqlite;
using Mir2.Companions;
using Mir2.Hosting;
using Server.ConsoleHost;

internal static class CompanionAllChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var paths=new ProjectPaths(data:Path.Combine(root,"companion-all"));
        Directory.CreateDirectory(paths.World);
        using var storage=new SqliteDatabaseStorage(paths.Database);
        var previousStorage=Server.DatabaseStorage.Current;var previousDirectory=Directory.GetCurrentDirectory();
        try
        {
            Server.DatabaseStorage.Current=storage;Directory.SetCurrentDirectory(paths.World);
            var store=new CompanionStore(storage,new(paths,storage),paths);
            try{store.StartAll(store.StartAllVersion);throw new Exception("空档案仍开启陪玩");}
            catch(InvalidOperationException){check(!store.Settings.Enabled,"空档案改变总开关");}
            foreach(var (name,enabled) in new[]{("AllOne",true),("AllTwo",true),("AllDisabled",false)})
                store.SaveProfile(new(){Id=Guid.NewGuid(),Name=name,Enabled=enabled},"account"+name,"private-password");
            var model=new CompanionLlmModel{Id=Guid.NewGuid(),Name="batch-model",Endpoint="http://127.0.0.1/test",Model="model"};
            store.SaveSettings(new(store.Settings with{Models=[model],DefaultModelId=model.Id},ModelKeys:[new(model.Id,"private-model-key")]));
            foreach(var r in store.Records.Values.ToArray())store.Mutate(r,n=>{n.Desired="pause";n.Error="test-error";n.RestUntil=DateTimeOffset.UtcNow.AddHours(1);n.SuppressedUntil=n.RestUntil;n.OnlineSeconds[1]=5;});
            var stale=store.StartAllVersion;var first=store.Records.Values.First();
            store.SaveProfile(store.Public(first) with{Alias="changed"});
            try{store.StartAll(stale);throw new Exception("过期全部上线被接受");}
            catch(InvalidOperationException){check(!store.Settings.Enabled,"过期全部上线改变总开关");}
            var before=JsonSerializer.Serialize(store.Records.Values.ToArray());var config=JsonSerializer.Serialize(store.Configuration);
            var target=store.Records.Values.Where(r=>r.Profile.Enabled).Skip(1).First().Profile.Id;
            using var sql=new SqliteConnection($"Data Source={paths.Database};Pooling=False");sql.Open();using var trigger=sql.CreateCommand();
            trigger.CommandText=$"CREATE TRIGGER reject_all BEFORE UPDATE ON snapshots WHEN NEW.name='companions/profiles/{target:N}.json' BEGIN SELECT RAISE(ABORT,'batch start failure'); END";trigger.ExecuteNonQuery();
            try{store.StartAll(store.StartAllVersion);throw new Exception("全部上线部分失败未上抛");}
            catch(SqliteException){check(before==JsonSerializer.Serialize(store.Records.Values.ToArray())&&config==JsonSerializer.Serialize(store.Configuration),"全部上线失败内存未撤回");}
            var reopened=new CompanionStore(storage,new(paths,storage),paths);
            check(!reopened.Settings.Enabled&&reopened.Records.Values.All(r=>r.Desired!="run"),"全部上线失败数据库部分提交");
            trigger.CommandText="DROP TRIGGER reject_all";trigger.ExecuteNonQuery();
            check(store.StartAll(store.StartAllVersion)==2&&store.Settings.Enabled,"全部上线未开启总开关或数量错误");
            var active=store.Records.Values.Where(r=>r.Profile.Enabled).ToArray();
            check(active.All(r=>r.Desired=="run"&&r.Error==""&&r.SuppressedUntil==null&&r.RestUntil==null),"全部上线未恢复暂停/清理人工抑制");
            var disabled=store.Records.Values.Single(r=>!r.Profile.Enabled);
            check(disabled.Error=="test-error"&&disabled.Desired!="run","全部上线误启动停用陪玩");
            check(store.Records.Values.All(r=>r.OnlineSeconds[1]==5&&r.Password=="private-password"),"全部上线重置额度或凭据");
            check(CompanionModels.Select(store.Configuration,model.Id)?.Key=="private-model-key","全部上线丢失模型密钥");
            reopened=new CompanionStore(storage,new(paths,storage),paths);
            check(reopened.Settings.Enabled&&reopened.Records.Values.Count(r=>r.Desired=="run")==2,"全部上线重启后未保留");
        }
        finally{Server.DatabaseStorage.Current=previousStorage;Directory.SetCurrentDirectory(previousDirectory);}
    }
}
