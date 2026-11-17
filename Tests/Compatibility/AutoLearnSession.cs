using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Crystal.Native;
using Mir2.Hosting;
using Mir2.Management;
using C = ClientPackets;
using S = ServerPackets;

internal static class AutoLearnSession
{
    public static async Task Run(string[] args)
    {
        var at = Array.IndexOf(args,"--auto-learn-session"); var port = int.Parse(args[at+1]); var record = args[at+2];
        var paths = new ProjectPaths();
        if (!paths.Data.Contains("mir2-native.",StringComparison.Ordinal)) throw new InvalidOperationException("自动学习TCP检查必须使用隔离fixture。");
        using var http = new HttpClient { BaseAddress = new Uri(Environment.GetEnvironmentVariable("MIR2_SERVER")!) };
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",File.ReadAllText(paths.AdminKeyFile).Trim());
        http.DefaultRequestHeaders.Add("X-Mir2-Version",paths.VersionId);
        async Task<ServerSettingsDocument> Settings() => (await http.GetFromJsonAsync<ServerSettingsDocument>("/management/settings"))!;
        async Task Save(ServerSettings values)
        {
            using var response = await http.PutAsJsonAsync("/management/settings",new ServerSettingsEdit(values,(await Settings()).Version));
            if (!response.IsSuccessStatusCode) throw new Exception(await response.Content.ReadAsStringAsync());
        }
        Packet.IsServer=false;using var connection=new GameConnection();var state=new GameState();var packets=new List<Packet>();
        async Task Until(Func<bool> condition)
        {
            var timer=Stopwatch.StartNew();
            while(true)
            {
                while(connection.Received.TryDequeue(out var packet)){state.Apply(packet);packets.Add(packet);}
                if(condition())return;
                if(connection.Errors.TryDequeue(out var error))throw new IOException(error);
                if(timer.Elapsed>TimeSpan.FromSeconds(20))throw new TimeoutException(string.Join(" / ",packets.OfType<S.Chat>().TakeLast(6).Select(chat=>chat.Message)));
                await Task.Delay(20);
            }
        }
        void Check(bool value,string message){if(!value)throw new Exception(message);}
        async Task Level(ushort level)
        {
            packets.Clear();await connection.Send(new C.Chat {Message=$"@lv {level}"});
            await Until(()=>state.User!.Level==level&&packets.OfType<S.Chat>().Any(chat=>chat.Message.Contains("->")));
        }
        string Snapshot()=>JsonSerializer.Serialize(new {state.User!.Level,state.User.Gold,
            inventory=state.User.Inventory.Select(item=>item==null?null:new{item.UniqueID,item.ItemIndex,item.Count}),
            spells=state.User.Magics.OrderBy(magic=>magic.Spell).Select(magic=>new{magic.Spell,magic.Level,magic.Key,magic.Experience,magic.IsTempSpell})});
        await connection.Connect("127.0.0.1",port);await Until(()=>packets.OfType<S.Connected>().Any());
        await connection.Send(new C.ClientVersion{VersionHash=[]});await Until(()=>packets.OfType<S.ClientVersion>().Any());
        await connection.Send(new C.Login{AccountID="NativeGM",Password="Native123"});await Until(()=>packets.OfType<S.LoginSuccess>().Any());
        await connection.Send(new C.StartGame{CharacterIndex=state.Characters.Single(character=>character.Name=="NativeGM").Index});await Until(()=>state.User!=null&&state.Map!=null);
        if(args.Contains("--verify-save"))
        {
            Check(Snapshot()==File.ReadAllText(record),"重启后技能等级/熟练度/快捷键、库存或金币不一致。");
            var restored=(await Settings()).Values;
            Check(restored.AutoLearnSkills&&restored.AutoLearnWarriorSkills.SequenceEqual(new[]{Spell.Fencing,Spell.Slaying,Spell.Thrusting}),"重启后自动学习配置丢失。");
        }
        else
        {
            var current=await Settings();
            Check(current.SkillOptions.Any(option=>option.Class==MirClass.Warrior&&option.Spell==Spell.Slaying),"真实版本缺少攻杀剑术。");
            var settings=current.Values with{AutoLearnSkills=false,AutoLearnWarriorSkills=[Spell.Fencing,Spell.Slaying,Spell.Thrusting],AutoLearnWizardSkills=[Spell.GreatFireBall]};
            await Save(settings);await Level(51);
            Check(state.User!.Magics.Count==0,"关闭开关仍学习技能。");
            var inventory=JsonSerializer.Serialize(state.User.Inventory);var gold=state.User.Gold;
            await Save(settings with{AutoLearnSkills=true});await Level(52);
            Check(state.User.Magics.Select(magic=>magic.Spell).Order().SequenceEqual(settings.AutoLearnWarriorSkills.Order())&&
                packets.OfType<S.NewMagic>().Count()==3&&packets.OfType<S.Chat>().Count(chat=>chat.Type==ChatType.System&&chat.Message.StartsWith("升级自动学习："))==3,
                "升级没有收到三项技能原回执/系统反馈或学习了其他职业。");
            Check(JsonSerializer.Serialize(state.User.Inventory)==inventory&&state.User.Gold==gold,"自动学习消耗物品或金币。");
            await connection.Send(new C.MagicKey{Spell=Spell.Fencing,Key=1});state.User.Magics.Single(magic=>magic.Spell==Spell.Fencing).Key=1;
            var known=JsonSerializer.Serialize(state.User.Magics);await Level(53);
            Check(!packets.OfType<S.NewMagic>().Any()&&JsonSerializer.Serialize(state.User.Magics)==known,"重复升级重置或重复学习技能。");
            File.WriteAllText(record,Snapshot());
        }
        packets.Clear();await connection.Send(new C.LogOut());await Until(()=>packets.OfType<S.LogOutSuccess>().Any());
        Console.WriteLine(args.Contains("--verify-save")?"PASS automatic learning original TCP restart: configured class lists, permanent skill levels/XP/hotkeys, inventory and gold restored.":
            "PASS automatic learning original TCP: disabled/enabled upgrades, three selected warrior skills and system notices, other class exclusion, unchanged inventory/gold and duplicate protection.");
    }
}
