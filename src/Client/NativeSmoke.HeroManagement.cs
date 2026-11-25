using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyHeroManagement()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-heroes.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("英雄实测仅允许隔离版本。");
        stage = "hero management login";
        var archer = OS.GetCmdlineUserArgs().Contains("--verify-hero-archer");
        var expectedName = archer ? "长弓回归英雄" : "星月英雄";
        var expectedClass = archer ? MirClass.Archer : MirClass.Wizard;
        var portraitIndex = 1410 + (int)expectedClass;
        await Until(() => Client.HandshakeReady); Client.Login("TradeRight", "Trade123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading);
        VerifyQuestCoordinates();
        var player = State.User!;
        var portrait = (Button)Client.FindChild("HeroShortcut", true, false);
        var pet = (Button)Client.FindChild("PetShortcut", true, false);
        if (OS.GetCmdlineUserArgs().Contains("--verify-hero-save"))
        {
            await Until(() => State.ActiveHero?.Name == expectedName && portrait.Icon != null);
            await VerifyHeroHudLayout();
            if (State.ActiveHero!.Class != expectedClass || State.ActiveHero.Gender != MirGender.Female || player.Inventory.Any(item => item?.Info.Type == ItemType.SealedHero))
                throw new Exception("保存重启后英雄职业、性别或封印物品错误。");
            if (OS.GetCmdlineUserArgs().Contains("--verify-hud")) await VerifyQuestNavigationReply();
            await Screenshot("hero-hud");
            if (State.HeroState == HeroSpawnState.Summoned) await ToggleHero("休息");
            await Until(() => State.HeroState == HeroSpawnState.Unsummoned);
            packets.RemoveAll(packet => packet is S.LogOutSuccess or S.UserInformation);
            Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>();
            Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single().Index });
            await Packet<S.UserInformation>();
            await Until(() => State.HeroState == HeroSpawnState.Unsummoned && State.ActiveHero?.Name == expectedName && portrait.Visible && portrait.Icon != null);
            if (((Control)Client.FindChild("HeroManagement", true, false)).Visible || portrait.Icon != Client.Assets.Sprite("Prguse", portraitIndex)?.Texture)
                throw new Exception("休息状态重新进入后头像丢失，或自动弹出英雄管理。");
            await ToggleHero("召唤");
            await Until(() => State.HeroState == HeroSpawnState.Summoned);
            await ToggleHero("休息");
            await Until(() => State.HeroState == HeroSpawnState.Unsummoned);
            await ToggleHero("召唤");
            await Until(() => State.HeroState == HeroSpawnState.Summoned);
            var mode = (OptionButton)Client.FindChild("HeroPortraitMode", true, false);
            await Until(() => !mode.Disabled);
            await ClickSelectionControl(mode); await Until(() => mode.GetPopup().Visible);
            mode.GetPopup().EmitSignal(PopupMenu.SignalName.IndexPressed, mode.GetItemIndex((int)HeroBehaviour.Follow)); mode.GetPopup().Hide();
            await Until(() => State.User!.HeroBehaviour == HeroBehaviour.Follow);
            Client.Send(new C.SetHeroBehaviour { Behaviour = HeroBehaviour.Attack });
            await Until(() => State.User!.HeroBehaviour == HeroBehaviour.Attack);
            GD.Print("PASS native hero saved identity, class/gender portrait, consumed seal after restart and resting hero re-login/summon."); await Client.Quit(); return;
        }
        if (portrait.Visible || portrait.Icon != null || pet.Visible) throw new Exception("无英雄时头像仍显示，或未召唤宠物仍显示。");
        async Task Result(byte expected, string name, MirGender gender = MirGender.Female, MirClass job = MirClass.Wizard)
        {
            packets.RemoveAll(packet => packet is S.NewHero);
            Client.Send(new C.NewHero { Name = name, Gender = gender, Class = job });
            if ((await Packet<S.NewHero>()).Result != expected) throw new Exception($"英雄拒绝原因错误，预期 {expected}。");
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
        }
        await Result(7, "未授权英雄");
        await Until(() => State.Actors.Values.Any(actor => actor.Name == "英雄使者"));
        var guide = State.Actors.Values.Single(actor => actor.Name == "英雄使者");
        async Task MainPage()
        {
            packets.RemoveAll(packet => packet is S.NPCResponse);
            Client.Send(new C.CallNPC { ObjectID = guide.Id, Key = "[@MAIN]" });
            var page = await Packet<S.NPCResponse>();
            if (!page.Page.Any(line => line.Contains("@CREATEHERO") && line.Contains("@MANAGEHERO"))) throw new Exception("英雄使者未提供原创建/管理链接。");
        }
        await MainPage(); await Result(7, "无页面英雄");
        packets.RemoveAll(packet => packet is S.HeroCreateRequest);
        Client.Send(new C.CallNPC { ObjectID = guide.Id, Key = "[@CREATEHERO]" }); await Packet<S.HeroCreateRequest>();
        var create = (ConfirmationDialog)Client.FindChild("HeroCreation", true, false);
        await Until(() => create.Visible);
        var jobs = (OptionButton)create.FindChild("HeroClass", true, false);
        if (archer ? jobs.GetItemIndex((int)MirClass.Archer) < 0 || jobs.ItemCount != 5 : jobs.GetItemIndex((int)MirClass.Archer) >= 0)
            throw new Exception("创建界面未按原服开关显示五职业/弓箭手。");
        await Result(1, "x"); await Result(5, "TradeLeft"); await Result(5, "回归英雄");
        await Result(2, "性别错误", (MirGender)255); await Result(3, "职业错误", job: (MirClass)255);
        if (!archer) await Result(3, "未开放职业", job: MirClass.Archer);
        var name = (LineEdit)create.FindChild("HeroName", true, false); name.Text = expectedName;
        jobs.Select(jobs.GetItemIndex((int)expectedClass)); ((OptionButton)create.FindChild("HeroGender", true, false)).Select(1);
        await Screenshot("hero-create"); packets.RemoveAll(packet => packet is S.NewHero);
        create.GetOkButton().EmitSignal(BaseButton.SignalName.Pressed);
        if ((await Packet<S.NewHero>()).Result != 10) throw new Exception("真实创建失败。");
        await Until(() => player.Inventory.Any(item => item?.Info.Type == ItemType.SealedHero));
        var seal = player.Inventory.Single(item => item?.Info.Type == ItemType.SealedHero);
        var heroId = seal.AddedStats[Stat.Hero];
        if (heroId <= 0 || State.Hero != null || portrait.Visible) throw new Exception("封印英雄未保持独立编号，或未激活就显示英雄栏。");
        var notice = (Label)create.FindChild("HeroCreationNotice", true, false);
        if (!notice.Text.Contains("封印英雄已放入背包") || !notice.Text.Contains("双击使用") || !notice.Text.Contains("激活后显示右下角英雄栏"))
            throw new Exception("创建成功提示没有说明封印道具及激活后显示英雄栏。");
        await Screenshot("hero-created-sealed");
        Client.Send(new C.UseItem { UniqueID = seal.UniqueID, Grid = MirGridType.Inventory });
        await Until(() => State.Hero?.Name == expectedName && State.HeroState == HeroSpawnState.Summoned && portrait.Visible && portrait.Icon != null);
        if (State.User != player || State.Hero!.Gender != MirGender.Female || State.Hero.Class != expectedClass || player.Inventory.Any(item => item?.UniqueID == seal.UniqueID))
            throw new Exception("英雄激活覆盖本人身份、性别职业不符或未消耗封印。");
        if (archer && !player.Inventory.Any(item => item?.Info.Type == ItemType.Potion && item.Info.Shape == 1))
            throw new Exception("弓箭英雄首次初始化没有把普通出生物品放入共用背包。");
        if (portrait.Icon != Client.Assets.Sprite("Prguse", portraitIndex)?.Texture) throw new Exception("英雄没有按真实职业/性别使用头像。");
        var hp = (ProgressBar)Client.FindChild("HeroHP", true, false); var mp = (ProgressBar)Client.FindChild("HeroMP", true, false);
        await Until(() => hp.MaxValue > 1 && mp.MaxValue > 1);
        var health = State.Hero.HP; var mana = State.Hero.MP;
        State.Apply(new S.HeroHealthChanged { HP = 10, MP = 5 });
        await Until(() => hp.Value == 10 && mp.Value == 5);
        State.Apply(new S.HeroHealthChanged { HP = health, MP = mana });
        await Screenshot("hero-hud");
        await ToggleHero("休息");
        await Until(() => State.HeroState == HeroSpawnState.Unsummoned && hp.Value == 0 && mp.Value == 0);
        await ToSignal(GetTree().CreateTimer(1.6), SceneTreeTimer.SignalName.Timeout);
        await ToggleHero("召唤");
        await Until(() => State.HeroState == HeroSpawnState.Summoned);
        await MainPage(); Client.Send(new C.CallNPC { ObjectID = guide.Id, Key = "[@MANAGEHERO]" });
        var management = (Control)Client.FindChild("HeroManagement", true, false);
        await Until(() => management.Visible && State.ActiveHero?.Name == expectedName);
        await Screenshot("hero-management");
        GD.Print($"PASS native hero creation validation, class={expectedClass}, original sealed item #{seal.UniqueID}/hero#{heroId}, portrait, HP/MP, summon/rest, management and quest map coordinates.");
        await Client.Quit();
    }

    private void VerifyQuestCoordinates()
    {
        State.Apply(new S.QuestNavigation { QuestIndex = -999, Targets = [new() { Id = 0, Name = "任务目标", MapTitle = "比奇城", Location = new(123, 456) }] });
        try
        {
            var method = Client.GetType().GetMethod("QuestLinkedText", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var display = new RichTextLabel { BbcodeEnabled = true }; AddChild(display);
            foreach (var value in new[] { "123:456", "[123:456]", "(123:456)", "（123:456）", "比奇城(123:456)", "比奇城（123:456）" })
            {
                var linked = (string)method.Invoke(Client, [-999, "返回" + value + "处寻找任务目标。 [普通文字]"])!;
                display.Text = linked;
                if (!linked.Contains("url=-999:0") || display.GetParsedText() != "返回比奇城(123:456)处寻找任务目标。 [普通文字]")
                    throw new Exception("任务坐标地图名/导航链接/括号不正确：" + linked);
            }
            display.Text = (string)method.Invoke(Client, [-999, "没有坐标"])!;
            if (!display.GetParsedText().Contains("前往 任务目标 · 比奇城(123:456)")) throw new Exception("补充导航行丢失目标名。");
            display.QueueFree();
        }
        finally { State.QuestNavigation.Remove(-999); }
        GD.Print("PASS native quest coordinates: map names, original parentheses and navigation links.");
    }

    private async Task VerifyHeroHudLayout()
    {
        var controls = new[] { "HeroShortcut", "HeroHP", "HeroMP", "SummonHero", "HeroPortraitMode" }
            .Select(name => (Control)Client.FindChild(name, true, false)).ToArray();
        if (!controls.Select(control => control.Position.Y).SequenceEqual(new float[] { 516, 596, 604, 614, 614 }))
            throw new Exception("英雄头像、血蓝条及双控件没有整体上移10px。");
        var user = State.User!; var hasHero = user.HasHero;
        var refresh = Client.GetType().GetMethod("RefreshCompanionPortraits", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            // 仅改变本地显示样本，保留原服英雄及全部资产。
            user.HasHero = false; refresh.Invoke(Client, null);
            if (controls.Any(control => control.Visible)) throw new Exception("没有英雄时仍显示头像或配套控件。");
            await Screenshot("hero-absent");
            user.HasHero = hasHero; refresh.Invoke(Client, null);
            if (!hasHero || controls.Any(control => !control.Visible)) throw new Exception("恢复真实英雄状态后头像或控件没有显示。");
            await Screenshot("hero-shifted");
        }
        finally { user.HasHero = hasHero; refresh.Invoke(Client, null); }
        GD.Print("PASS native hero HUD: absent hero hidden, owned hero restored, complete region moved up 10px.");
    }

    private async Task ToggleHero(string caption)
    {
        var button = (Button)Client.FindChild("SummonHero", true, false);
        await Until(() => !button.Disabled && button.Text == caption);
        await ClickSelectionControl(button);
    }

    private async Task VerifyQuestNavigationReply()
    {
        stage = "disconnected quest map original TCP and tracker";
        await Until(() => State.Quests.Values.Any(q => q.QuestInfo?.Name == "铁匠的第一次考验"));
        var quest = State.Quests.Values.Single(q => q.QuestInfo?.Name == "铁匠的第一次考验");
        await Until(() => State.QuestNavigation.TryGetValue(quest.Id, out var route) && route.Targets.Count > 0);
        var target = State.QuestNavigation[quest.Id].Targets.Single();
        if (target.MapTitle != "比奇城" || target.Location != new System.Drawing.Point(297, 612) || target.Reachable || target.MapIndex == State.Map!.MapIndex)
            throw new Exception("不可达任务地图资料错误，或放宽了导航权限。");
        var tracker = (RichTextLabel)Client.FindChild("QuestTracker", true, false);
        await Until(() => tracker.GetParsedText().Contains("返回比奇城(297:612)处寻找铁匠李铁匠。"));
        var location = State.User!.Location;
        tracker.EmitSignal(RichTextLabel.SignalName.MetaClicked, $"{quest.Id}:0");
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        if (State.User.Location != location || Client.MiniMap.Route.Count != 0) throw new Exception("不可达任务点击后移动了角色。");
        GD.Print("PASS native disconnected quest map: original TCP map/coordinates, parsed tracker and rejected navigation.");
    }
}
