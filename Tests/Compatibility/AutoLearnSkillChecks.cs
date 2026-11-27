using Mir2.Hosting;
using Mir2.Management;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class AutoLearnSkillChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
        public override void Broadcast(Packet packet) => Packets.Add(packet);
    }
    private sealed class Hero(HeroInfo info, Player owner) : HeroObject(info, owner)
    {
        protected override void Load(CharacterInfo info, Server.MirNetwork.MirConnection connection)
        { Info = info; HInfo = (HeroInfo)info; Info.Mount = new MountInfo(this); Stats = new Stats(); }
        public override void Broadcast(Packet packet) { }
    }

    public static void Run(Action<bool,string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "mir2-auto-learn-" + Guid.NewGuid().ToString("N"));
        var paths = new ProjectPaths(root); Directory.CreateDirectory(Path.Combine(paths.World,"Configs"));
        var file = Path.Combine(paths.World,"Configs","Setup.ini");
        File.WriteAllText(file,"; 保留配置\n[Other]\nValue=keep\n");
        var service = new SystemSettings(new(paths)); var before = service.Read();
        var world = Envir.Main; var oldItems = world.ItemInfoList; var oldMagics = world.MagicInfoList;
        var primary = new[] { Spell.Fencing, Spell.FireBall, Spell.Healing, Spell.FatalSword, Spell.Focus };
        var later = new[] { Spell.Slaying, Spell.GreatFireBall, Spell.Poisoning, Spell.DoubleSlash, Spell.DoubleShot };
        try
        {
            world.ItemInfoList = []; world.MagicInfoList = [];
            foreach (var role in Enum.GetValues<MirClass>())
            for (var tier = 0; tier < 2; tier++)
            {
                var spell = (tier == 0 ? primary : later)[(int)role];
                world.MagicInfoList.Add(new MagicInfo { Spell = spell, Name = spell.ToLocalizedString(), Level1 = (byte)(tier == 0 ? 7 : 19) });
                world.ItemInfoList.Add(new ItemInfo { Index = 100 + (int)spell, Name = spell.ToLocalizedString(), Type = ItemType.Book,
                    Shape = (short)spell, RequiredAmount = (byte)(tier == 0 ? 7 : 20), RequiredClass = (RequiredClass)(1 << (int)role) });
            }
            world.ItemInfoList.Insert(0,new ItemInfo { Index = 1, Type = ItemType.Book, Shape = (short)Spell.Fencing,
                RequiredClass = RequiredClass.Warrior, RequiredGender = RequiredGender.Female, RequiredAmount = 1 });
            world.ItemInfoList.Add(new ItemInfo { Index = 999, Type = ItemType.Book, Shape = 257, RequiredClass = RequiredClass.Warrior });
            var document = service.Read();
            check(document.SkillOptions.Length == 10 && document.SkillOptions.Count(skill=>skill.Class==MirClass.Warrior)==2,"技能清单没有按当前版本/职业去重");
            check(!document.Values.AutoLearnSkills && Enum.GetValues<MirClass>().All(role=>document.Values.SkillsFor(role).Length==0),"自动学习默认开启或默认选中技能");
            var edit = document.Values with { AutoLearnSkills = true };
            foreach (var role in Enum.GetValues<MirClass>()) edit.SetSkills(role,[primary[(int)role],later[(int)role]]);
            var saved = service.Save(new(edit,document.Version));
            check(!saved.RequiresRestart && Settings.AutoLearnSkills && Settings.AutoLearnWizardSkills.SequenceEqual(new[]{Spell.FireBall,Spell.GreatFireBall}),"自动学习未独立热更新");
            check(!document.Values.AutoLearnSkills && document.Values.AutoLearnWarriorSkills.Length==0 && File.ReadAllText(file).Contains("Value=keep"),"草稿或保存覆盖已读对象/无关配置");
            Settings.AutoLearnWarriorSkills = []; Settings.AutoLearnSkills = false; Settings.LoadAutoLearnSkills(new(file));
            check(Settings.AutoLearnSkills && Settings.AutoLearnWarriorSkills.SequenceEqual(new[]{Spell.Fencing,Spell.Slaying}),"重启读取未保留自动学习开关和选择");
            var content = File.ReadAllText(file);
            foreach (var invalid in new[] { edit with { AutoLearnWarriorSkills = [Spell.FireBall] }, edit with { AutoLearnWarriorSkills = [Spell.None] },
                edit with { AutoLearnWarriorSkills = [Spell.Fencing,Spell.Fencing] }, edit with { AutoLearnArcherSkills = null! }, edit with { AutoLearnTaoistSkills = [(Spell)255] } })
                Reject(()=>service.Save(new(invalid,saved.Version)),"非法职业/重复/空/未知技能配置被接受");
            check(File.ReadAllText(file)==content && Settings.AutoLearnSkills,"校验失败改变文件或运行值");
            world.MagicInfoList[0].Level1++;
            Reject(()=>service.Save(new(edit,saved.Version)),"技能定义变化后旧设置仍可覆盖"); world.MagicInfoList[0].Level1--;
            foreach (var role in Enum.GetValues<MirClass>())
            { Learning(role,primary[(int)role],later[(int)role],check); HeroLearning(role,primary[(int)role],later[(int)role],check); }
            var warrior = NewPlayer(MirClass.Warrior,50); Settings.AutoLearnWarriorSkills = [Spell.FireBall]; warrior.AutoLearnSelectedSkills();
            check(warrior.Info.Magics.Count==0,"运行时错误职业配置绕过真实书籍职业限制");
            Settings.AutoLearnWarriorSkills = [Spell.Fencing]; Settings.AutoLearnSkills = false; warrior.AutoLearnSelectedSkills();
            check(warrior.Info.Magics.Count==0,"关闭总开关后仍自动学习");
            Settings.AutoLearnSkills = true;
            world.ItemInfoList.Insert(0,new ItemInfo { Type = ItemType.Book, Shape = (short)Spell.Fencing, RequiredClass = RequiredClass.Warrior,
                RequiredType = RequiredType.MaxMC, RequiredAmount = 255 });
            warrior.AutoLearnSelectedSkills();
            check(warrior.Info.Magics.Count==1,"同技能另一份书符合条件时没有学习");
            Settings.LoadAutoLearnSkills(new(Path.Combine(root,"missing.ini")));
            check(!Settings.AutoLearnSkills && Enum.GetValues<MirClass>().All(role=>Settings.AutoLearnFor(role).Length==0) && !File.Exists(Path.Combine(root,"missing.ini")),"新版本缺配置时沿用旧选择或只读产生配置");
            File.WriteAllText(file,"[AutoLearnSkills]\nEnabled=True\nWarrior=1,invalid\n");
            Reject(()=>Settings.LoadAutoLearnSkills(new(file)),"无效磁盘配置被静默吞掉");
        }
        finally
        {
            world.ItemInfoList = oldItems; world.MagicInfoList = oldMagics;
            File.WriteAllText(file,""); service.Save(new(before.Values,service.Read().Version)); Directory.Delete(root,true);
        }
        void Reject(Action action,string message)
        {
            try { action(); } catch(Exception error) when(error is InvalidDataException or InvalidOperationException) { check(true,message);return; }
            throw new Exception(message);
        }
    }

    private static Player NewPlayer(MirClass role,ushort level)
    {
        var player = new Player { Info = new CharacterInfo { Name = "自动学习检查", Class = role, Gender = MirGender.Male, Level = level },
            Account = new AccountInfo(), Stats = new Stats(), HasUpdatedBaseStats = true };
        player.Info.Mount = new MountInfo(player); return player;
    }

    private static void Learning(MirClass role,Spell first,Spell second,Action<bool,string> check)
    {
        var player = NewPlayer(role,6); var book = Envir.Main.ItemInfoList.First(item=>item.Shape==(short)first && item.RequiredGender==RequiredGender.None);
        player.Info.Inventory[0] = new UserItem(book) { UniqueID = 12345, Count = 3 };
        player.AutoLearnSelectedSkills(); check(player.Info.Magics.Count==0,"未到学习等级仍学习："+role);
        player.Level = 7; player.AutoLearnSelectedSkills();
        check(player.Info.Magics.Select(m=>m.Spell).SequenceEqual(new[]{first}) && player.Packets.OfType<S.NewMagic>().Count()==1,"到级未学习或重复发技能包："+role);
        var learned = player.Info.Magics.Single(); learned.Experience=123; learned.Key=8; learned.Level=2;
        player.AutoLearnSelectedSkills(); check(learned.Experience==123&&learned.Key==8&&learned.Level==2&&player.Info.Magics.Count==1,"重复检查重置已学技能："+role);
        player.Level=19;player.AutoLearnSelectedSkills();check(player.Info.Magics.Count==1,"绕过技能书的更高等级要求："+role);
        player.Level=50;player.AutoLearnSelectedSkills();
        check(player.Info.Magics.Count==2&&player.Info.Magics.Any(m=>m.Spell==second&&m.Level==0&&!m.IsTempSpell),"跨多级没有补齐选中技能或错误升级技能："+role);
        check(player.Info.Inventory[0] is{UniqueID:12345,Count:3}&&player.Account.Gold==0,"自动学习消耗书籍/金币或修改物品编号："+role);
        check(player.Packets.OfType<S.Chat>().Count(packet=>packet.Type==ChatType.System&&packet.Message.StartsWith("升级自动学习："))==2,"学习结果没有系统反馈或重复提示："+role);
    }

    private static void HeroLearning(MirClass role, Spell first, Spell second, Action<bool, string> check)
    {
        var owner = NewPlayer(MirClass.Warrior, 60); owner.Account.Gold = 1234;
        var hero = new Hero(new HeroInfo(new ClientPackets.NewHero { Name = "升级检查", Class = role, Gender = MirGender.Male })
            { Level = 6, Inventory = [] }, owner);
        owner.Hero = hero;
        var book = Envir.Main.ItemInfoList.First(item => item.Shape == (short)first && item.RequiredGender == RequiredGender.None);
        owner.Info.Inventory[6] = new UserItem(book) { UniqueID = 54321, Count = 3 };
        hero.LevelUp(); check(hero.Info.Magics.Count == 0, "英雄按主人等级提前学习：" + role);
        Settings.AutoLearnSkills = false; hero.Level = 7; hero.LevelUp();
        check(hero.Info.Magics.Count == 0, "英雄忽略系统自动学习总开关：" + role); Settings.AutoLearnSkills = true;
        hero.LevelUp();
        check(hero.Info.Magics.Select(magic => magic.Spell).SequenceEqual(new[] { first }) && owner.Info.Magics.Count == 0 &&
            owner.Packets.OfType<S.NewMagic>().Count(packet => packet.Hero) == 1, "英雄升级未按职业列表学习或污染主人技能/回执：" + role);
        var learned = hero.Info.Magics.Single(); learned.Level = 2; learned.Key = 19; learned.Experience = 123;
        hero.LevelUp(); check(learned.Level == 2 && learned.Key == 19 && learned.Experience == 123 && hero.Info.Magics.Count == 1, "英雄升级重置已有等级/熟练度/快捷键：" + role);
        hero.Level = 19; hero.LevelUp(); check(hero.Info.Magics.Count == 1, "英雄绕过技能书等级条件：" + role);
        hero.Level = 50; hero.LevelUp(); check(hero.Info.Magics.Any(magic => magic.Spell == second && magic.Level == 0 && !magic.IsTempSpell), "英雄跨级未补学系统选中技能：" + role);
        check(owner.Info.Inventory[6] is { UniqueID: 54321, Count: 3 } && owner.Account.Gold == 1234 && hero.Info.Inventory.Length == 0, "英雄自动学习消耗共用包/金币或新建英雄背包：" + role);
        var forbidden = Envir.Main.ItemInfoList.Where(item => item.Shape == (short)first).ToArray();
        foreach (var item in forbidden) item.Bind = BindMode.NoHero;
        hero.Info.Magics.Clear(); hero.AutoLearnSelectedSkills();
        check(hero.Info.Magics.All(magic => magic.Spell != first), "英雄自动学习绕过NoHero书籍限制：" + role);
        foreach (var item in forbidden) item.Bind = BindMode.None;
    }
}
