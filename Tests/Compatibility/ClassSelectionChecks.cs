using Crystal.Native;
using Client.MirObjects;
using Mir2.Hosting;
using Server.ConsoleHost;
using S = ServerPackets;

internal static class ClassSelectionChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "mir2-class-settings-" + Guid.NewGuid().ToString("N"));
        var paths = new ProjectPaths(root); Directory.CreateDirectory(Path.Combine(paths.World, "Configs"));
        File.WriteAllText(Path.Combine(paths.World, "Configs", "Setup.ini"), "[Permission]\n; 保留原注释\nAllowCreateAssassin=False\nAllowCreateArcher=False\n");
        var service = new SystemSettings(new DevelopmentFiles(paths)); var before = service.Read();
        try
        {
            var enabled = before.Values with { AllowCreateAssassin = true, AllowCreateArcher = true };
            var document = service.Save(new(enabled, before.Version));
            check(document.Values.AllowCreateAssassin && document.Values.AllowCreateArcher && Server.Settings.AllowCreateAssassin && Server.Settings.AllowCreateArcher,
                "管理设置没有恢复两个职业运行开关");
            var text = File.ReadAllText(Path.Combine(paths.World, "Configs", "Setup.ini"));
            check(text.Contains("AllowCreateAssassin=True") && text.Contains("AllowCreateArcher=True") && text.Contains("保留原注释"), "职业开关没有随所选版本保存或破坏原配置");
            document = service.Save(new(document.Values with { AllowCreateAssassin = false }, document.Version));
            check(!document.Values.AllowCreateAssassin && document.Values.AllowCreateArcher, "刺客与弓箭手开关不能独立保存");
            document = service.Save(new(document.Values with { AllowCreateWarrior = false, AllowCreateWizard = false, AllowCreateTaoist = false }, document.Version));
            check(!Server.Settings.CanCreateClass(MirClass.Warrior) && !Server.Settings.CanCreateClass(MirClass.Wizard) && !Server.Settings.CanCreateClass(MirClass.Taoist) &&
                Server.Settings.CanCreateClass(MirClass.Archer), "其他三个职业开关未保存至运行参数");
            text = File.ReadAllText(Path.Combine(paths.World, "Configs", "Setup.ini"));
            check(text.Contains("AllowCreateWarrior=False") && text.Contains("AllowCreateWizard=False") && text.Contains("AllowCreateTaoist=False"), "三个新增职业开关未保存在所选版本配置");
            document = service.Save(new(new Mir2.Management.ServerSettings(), document.Version));
            check(Enum.GetValues<MirClass>().All(Server.Settings.CanCreateClass) && document.Values.MaxUser == 50 && document.Values.ExpRate == 1 &&
                document.Values.MagicResistWeight == 10, "重置默认参数不能正常保存或职业没有全部恢复");
        }
        finally { service.Save(new(before.Values, service.Read().Version)); Directory.Delete(root, true); }
        var state = new GameState();
        state.Apply(new S.LoginSuccess { Characters = [new SelectInfo { Index = 1 }, new SelectInfo { Index = 2 }] });
        state.Apply(new S.DeleteCharacter { Result = 0 });
        check(state.Characters.Count == 2, "删除拒绝回执提前移除了角色");
        state.Apply(new S.DeleteCharacterSuccess { CharacterIndex = 2 });
        check(state.Characters.Count == 1 && state.Characters[0].Index == 1, "删角成功没有按角色编号移除");
        state.Apply(new S.DeleteCharacterSuccess { CharacterIndex = 2 });
        check(state.Characters.Count == 1, "重复删角回执删除了其他角色");
        check(OriginalPlayerArt.SelectionStart(MirClass.Assassin, MirGender.Male) == 80 && OriginalPlayerArt.SelectionStart(MirClass.Assassin, MirGender.Female) == 360 &&
            OriginalPlayerArt.SelectionStart(MirClass.Archer, MirGender.Male) == 100 && OriginalPlayerArt.SelectionStart(MirClass.Archer, MirGender.Female) == 160,
            "选角素材仍把刺客/弓箭手映射为战士或空白女弓手");
    }
}
