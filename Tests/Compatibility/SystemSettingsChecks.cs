using Mir2.Hosting;
using Mir2.Management;
using Server.ConsoleHost;

internal static class SystemSettingsChecks
{
    public static void Run(Action<bool,string> check,string root)
    {
        var paths=new ProjectPaths(data:Path.Combine(root,"system-settings"));
        Directory.CreateDirectory(Path.Combine(paths.World,"Configs"));
        var file=Path.Combine(paths.World,"Configs","Setup.ini");
        const string initial="; 保留注释\r\n[Network]\r\nMaxUser=50\r\nMaxIP=5\r\nPort=7000\r\n[Unknown]\r\nCustom=不修改\r\n";
        File.WriteAllText(file,initial);
        var service=new SystemSettings(new(paths));var original=service.Read();
        try
        {
            check(File.ReadAllText(file)==initial,"系统设置只读产生写入");
            var edit=original.Values with{MaxUser=321,MaxIP=13,MaxAccountsPerIPHour=0,MaxCharactersPerIPHour=0,ExpRate=1000f,DropRate=1.5f,PublicShopPriceRate=1.8f};
            var saved=service.Save(new(edit,original.Version));
            check(!saved.RequiresRestart&&Server.Settings.MaxUser==321&&Server.Settings.MaxIP==13&&Server.Settings.ExpRate==1000f,"人数及倍率未热更新");
            check(Server.Settings.MaxAccountsPerIPHour==0&&Server.Settings.MaxCharactersPerIPHour==0&&
                new InIReader(file).ReadUInt16("Network","MaxAccountsPerIPHour",999,false)==0&&
                new InIReader(file).ReadUInt16("Network","MaxCharactersPerIPHour",999,false)==0,"注册/建角不限未热更新或无法按原配置重读");
            check(Server.Settings.PublicShopPriceRate == 1.8f && saved.Values.PublicShopPriceRate == 1.8f,"公共商店倍率未热更新");
            var content=File.ReadAllText(file);
            check(content.Contains("Custom=不修改")&&content.Contains("; 保留注释")&&content.Contains("Port=7000"),"系统设置覆盖未知键、注释或端口");
            check(new InIReader(file).ReadUInt16("Network","MaxIP",0,false)==13&&new InIReader(file).ReadSingle("Game","ExpRate",0,false)==1000f,"原引擎无法重读设置");
            check(Directory.GetFiles(Path.Combine(paths.Runtime,"backups"),"Setup.ini",SearchOption.AllDirectories).Any(p=>File.ReadAllText(p)==initial),"系统设置未保留完整原配置备份");
            Reject(()=>service.Save(new(edit,original.Version)),"过期配置被覆盖");
            foreach(var bad in new[]{edit with{MaxAccountsPerIPHour=-1},edit with{MaxAccountsPerIPHour=65536},edit with{MaxCharactersPerIPHour=-1},edit with{MaxCharactersPerIPHour=65536},edit with{PublicShopPriceRate=.99f},edit with{PublicShopPriceRate=10.01f},edit with{MaxIP=0},edit with{MaxUser=65536},edit with{DropRate=0},edit with{DropRate=100.01f},edit with{ExpRate=1000.01f},edit with{ExpRate=float.NaN},edit with{SaveDelay=0},edit with{MonsterRecallCooldown=99}})
                Reject(()=>service.Save(new(bad,saved.Version)),"非法参数被接受");
            foreach (var field in typeof(ServerSettings).GetProperties().Where(field => field.PropertyType == typeof(float)))
                foreach (var value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 0, float.MaxValue })
                {
                    var bad = edit with { }; field.SetValue(bad, value);
                    Reject(() => service.Save(new(bad, saved.Version)), $"{field.Name}接受非法倍率{value}");
                }
            foreach (var bad in new[] { edit with { PlayerAttackSpeedBonus = 31 }, edit with { PlayerAttackSpeedBonus = -21 },
                edit with { PlayerMoveMilliseconds = 299 }, edit with { PlayerMoveMilliseconds = 1001 },
                edit with { PlayerActionSpeedPercent = 79 }, edit with { PlayerActionSpeedPercent = 151 },
                edit with { CriticalDamageWeight = 0 }, edit with { MagicResistWeight = 256 }, edit with { PoisonResistWeight = 0 },
                edit with { MaxLuck = 0 }, edit with { HealthRegenWeight = 0 }, edit with { ManaRegenWeight = 0 },
                edit with { MaxDropGold = 0 }, edit with { DropRange = 21 }, edit with { DropStackSize = 101 }, edit with { MaxBossTames = -1 } })
                Reject(() => service.Save(new(bad, saved.Version)), "平衡整数参数超出安全范围");
            check(File.ReadAllText(file)==content&&Server.Settings.MaxIP==13,"校验失败改变配置或运行参数");
            File.WriteAllText(file,content+"\n[Network]\nMaxIP=10\n");
            var duplicate=service.Read();Reject(()=>service.Save(new(edit,duplicate.Version)),"重复节被接受");
            check(Server.Settings.MaxIP==13,"解析失败影响运行参数");
            File.WriteAllText(file,content);Directory.Move(Path.Combine(paths.Runtime,"backups"),Path.Combine(paths.Runtime,"saved-backups"));
            File.WriteAllText(Path.Combine(paths.Runtime,"backups"),"阻断备份写入");
            try{service.Save(new(edit with{MaxIP=25},service.Read().Version));throw new Exception("备份失败仍保存设置");}
            catch(IOException){check(File.ReadAllText(file)==content&&Server.Settings.MaxIP==13,"备份失败未保留配置及运行参数");}
            File.Delete(Path.Combine(paths.Runtime,"backups"));
            var safe=service.Save(new(edit with{SafeZoneBorder=!edit.SafeZoneBorder},service.Read().Version));
            check(safe.RequiresRestart,"安全区效果变更未标记重启");
            var runningTiming = Server.Settings.PlayerTiming;
            var timing = service.Save(new(safe.Values with { PlayerMoveMilliseconds = 650, PlayerActionSpeedPercent = 90 }, safe.Version));
            var settings = new InIReader(file);
            check(timing.RequiresRestart && timing.Values.PlayerMoveMilliseconds == 650 && timing.Values.PlayerActionSpeedPercent == 90 &&
                settings.ReadInt32("Balance", "PlayerMoveMilliseconds", 0, false) == 650 &&
                settings.ReadInt32("Balance", "PlayerActionSpeedPercent", 0, false) == 90 &&
                Server.Settings.PlayerTiming == runningTiming, "速度配置未保存或重启前错误更改了运行速度");
        }
        finally{service.Save(new(original.Values,service.Read().Version));}
        void Reject(Action action,string message)
        {
            try{action();}catch(Exception e)when(e is InvalidDataException or InvalidOperationException){check(true,message);return;}
            throw new Exception(message);
        }
    }
}
