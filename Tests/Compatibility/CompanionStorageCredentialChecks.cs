using System.Reflection;
using Microsoft.Data.Sqlite;
using Mir2.Companion;
using Mir2.Companions;
using Mir2.Hosting;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using S = ServerPackets;

internal static class CompanionStorageCredentialChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        Unlock(check);
        var paths=new ProjectPaths(data:Path.Combine(root,"storage-credentials"));Directory.CreateDirectory(paths.World);
        var previous=Server.DatabaseStorage.Current;var cwd=Directory.GetCurrentDirectory();
        var world=Envir.Main;var accounts=world.AccountList;var characters=world.CharacterList;
        try
        {
            Directory.SetCurrentDirectory(paths.World);using var storage=new SqliteDatabaseStorage(paths.Database);Server.DatabaseStorage.Current=storage;
            var store=new CompanionStore(storage,new(paths,storage),paths);world.AccountList=[];world.CharacterList=[];
            foreach(var (name,index) in new[]{("LegacyAuto",1),("LegacyManual",2),("LegacyEmpty",3)})
            {
                var account=new AccountInfo{Index=index,AccountID=name};account.Password="Login12345";
                var character=new CharacterInfo{Index=index,Name="凭据角色"+index,CreationIP="",AccountInfo=account};character.Heroes=new HeroInfo[character.MaximumHeroCount];
                account.Characters.Add(character);world.AccountList.Add(account);world.CharacterList.Add(character);
                if(index<3){account.StoragePassword=index==1?CompanionStorageRules.Password(name,"Login12345"):"Manual12345";account.StoragePasswordLastSet=new DateTime(2026,11,1);}
                var id=Guid.NewGuid();store.SaveProfile(new(){Id=id,Name=character.Name},name,"Login12345");
                store.Mutate(store.Get(id),r=>{r.AccountId=index;r.CharacterId=index;r.Registered=true;});
            }
            var automatic=world.AccountList[0];var manual=world.AccountList[1];var before=store.Records.ToDictionary(x=>x.Key,x=>x.Value.Account);
            var loginHash=automatic.Password;var storageHash=automatic.StoragePassword;var salt=automatic.StorageSalt.ToArray();var changed=automatic.StoragePasswordLastSet;
            using var sql=new SqliteConnection($"Data Source={paths.Database};Pooling=False");sql.Open();using var trigger=sql.CreateCommand();
            var denied=store.Records.Values.Single(r=>r.AccountId==2);
            trigger.CommandText=$"CREATE TRIGGER reject_credential BEFORE UPDATE ON snapshots WHEN NEW.name='companions/profiles/{denied.Profile.Id:N}.json' BEGIN SELECT RAISE(ABORT,'credential failure'); END";trigger.ExecuteNonQuery();
            try{store.NormalizeAccounts();throw new Exception("凭据迁移失败未上抛");}
            catch(SqliteException)
            {
                check(automatic.AccountID=="LegacyAuto"&&automatic.StoragePassword==storageHash&&automatic.StorageSalt.SequenceEqual(salt),"事务失败后账户名称或仓库凭据未回滚");
                check(before.All(p=>store.Get(p.Key).Account==p.Value),"凭据迁移失败后档案出现部分改名");
            }
            trigger.CommandText="DROP TRIGGER reject_credential";trigger.ExecuteNonQuery();store.NormalizeAccounts();
            check(automatic.ValidateStoragePassword(CompanionStorageRules.Password(automatic.AccountID,"Login12345")),"账号改名后自动仓库密码仍依赖旧账号");
            check(!automatic.ValidateStoragePassword(CompanionStorageRules.Password("LegacyAuto","Login12345")),"旧自动仓库密码仍可解锁改名账户");
            check(automatic.Password==loginHash&&automatic.StoragePasswordLastSet==changed,"自动凭据改名修改登录密码或人工设置时间");
            check(manual.ValidateStoragePassword("Manual12345"),"账号迁移覆盖了已有人工仓库密码");
            check(!world.AccountList[2].HasStoragePassword,"无仓库密码的账户被迁移强制加密");
            var after=automatic.StoragePassword;store.NormalizeAccounts();check(after==automatic.StoragePassword,"重复规范化反复更换仓库凭据");
        }
        finally{world.AccountList=accounts;world.CharacterList=characters;Server.DatabaseStorage.Current=previous;Directory.SetCurrentDirectory(cwd);}
    }
    private static void Unlock(Action<bool,string> check)
    {
        foreach(var code in new byte[]{0,4,2})
        {
            var events=new List<WorkerEvent>();var p=new CompanionProfile{Id=Guid.NewGuid(),Name="仓库回执检查"};
            var session=new CompanionSession(new(p,"ReceiptAccount","ReceiptPassword",true,"127.0.0.1",1,"test","storage-check",CompanionProtocol.Fingerprint,"","","",1),e=>{events.Add(e);return Task.CompletedTask;});
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            var pending=typeof(CompanionSession).GetField("pendingAsset",flags)!;pending.SetValue(session,"storage-unlock");
            try
            {
                var task=(Task<bool>)typeof(CompanionSession).GetMethod("StorageResult",flags)!.Invoke(session,[new S.StorageUnlockResult{Result=code}])!;
                if(code==2){try{task.GetAwaiter().GetResult();throw new Exception("错误密码被接受");}catch(InvalidOperationException){check(pending.GetValue(session)!=null,"密码拒绝后未保留停止保护");}}
                else check(task.GetAwaiter().GetResult()&&pending.GetValue(session)==null&&events.Any(e=>e.Text=="storage-unlock:成功"),"正常或无密码仓库解锁成功被误判为认证失败");
            }
            finally{session.Stop().GetAwaiter().GetResult();}
        }
    }
}
