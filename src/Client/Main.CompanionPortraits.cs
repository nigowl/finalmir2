using Crystal.Native;
using Godot;
using C = ClientPackets;

public partial class Main
{
    private HeroPortraitButton heroPortrait = null!;
    private readonly Button[] petPortraits = new Button[3];
    private readonly IntelligentCreatureType[] portraitPets = new IntelligentCreatureType[3];
    private readonly uint[] portraitSummons = new uint[3];
    private OptionButton hudHeroMode = null!, hudPetMode = null!;
    private Button summonHero = null!;

    private void BuildCompanionPortraits()
    {
        heroPortrait = new HeroPortraitButton { Name = "HeroShortcut", Position = new Vector2(906, 516), Size = new Vector2(108, 78) };
        heroPortrait.Pressed += () => { ToggleSocial(3); _ = PlaySound(10103); }; game.AddChild(heroPortrait);
        hudHeroMode = HudMode("HeroPortraitMode", 962, 600);
        hudHeroMode.ThemeTypeVariation = "HeroHudMode";
        foreach (var mode in new[] { HeroBehaviour.Attack, HeroBehaviour.CounterAttack, HeroBehaviour.Follow })
            hudHeroMode.AddItem(mode.ToLocalizedString(), (int)mode);
        hudHeroMode.ItemSelected += index => ChooseHeroBehaviour((HeroBehaviour)hudHeroMode.GetItemId((int)index));
        summonHero = Button(game, "召唤", 906, 600, 52, () => SetHeroSummoned(State.HeroState != HeroSpawnState.Summoned)); summonHero.Name = "SummonHero";
        summonHero.AddThemeFontSizeOverride("font_size", ClientUi.Small); summonHero.Size = new Vector2(52, 21);
        summonHero.ThemeTypeVariation = "HeroHudButton";
        heroPortrait.Visible = summonHero.Visible = hudHeroMode.Visible = false;

        for (var i = 0; i < petPortraits.Length; i++)
        {
            var slot = i;
            // 上方宠物同步避让，保留模式选择与英雄框之间的4px间距。
            petPortraits[i] = Portrait(i == 0 ? "PetShortcut" : $"PetPortrait{i + 1}", 962 - i * (ClientUi.CompanionPortraitSize + 2), 437, () =>
            {
                var same = social.Visible && socialPage == 2 && selectedPet == portraitPets[slot] && selectedSummon == portraitSummons[slot];
                if (!social.Visible || socialPage != 2 || same) ToggleSocial(2);
                if (social.Visible) { selectedPet = portraitPets[slot]; selectedSummon = portraitSummons[slot]; RenderSocial(); }
            });
        }
        hudPetMode = HudMode("PetPortraitMode", 962, 491);
        foreach (var mode in Enum.GetValues<PetMode>())
            hudPetMode.AddItem(mode switch { PetMode.Both => "攻击", PetMode.MoveOnly => "跟随", PetMode.AttackOnly => "定点", PetMode.None => "待命", _ => "集火" }, (int)mode);
        hudPetMode.TooltipText = "召唤物与英雄共用移动 / 攻击模式；集火时优先攻击主人的目标。拾取宠物不参与战斗。";
        hudPetMode.ItemSelected += index => ChoosePetMode((PetMode)hudPetMode.GetItemId((int)index));
    }

    private void SetHeroSummoned(bool summoned)
    {
        if (State.User?.HasHero != true || State.Me?.Dead != false || heroItemPending != null || Environment.TickCount64 < heroToggleAt ||
            summoned == (State.HeroState == HeroSpawnState.Summoned)) return;
        if (summoned && State.HeroState == HeroSpawnState.Dead && (!State.HeroRecoveryKnown || State.HeroRecoverySeconds > 0)) return;
        heroToggleAt = Environment.TickCount64 + 1500; Send(new C.Chat { Message = "@SUMMONHERO" });
    }

    private Button Portrait(string name, int x, int y, Action action)
    {
        var button = Button(game, "", x, y, ClientUi.CompanionPortraitSize, action); button.Name = name;
        button.ExpandIcon = true; button.Size = Vector2.One * ClientUi.CompanionPortraitSize;
        return button;
    }

    private OptionButton HudMode(string name, int x, int y)
    {
        var button = new OptionButton { Name = name, Position = new Vector2(x, y), FocusMode = FocusModeEnum.None,
            ActionMode = BaseButton.ActionModeEnum.Release, FitToLongestItem = false, ClipText = true };
        button.AddThemeFontSizeOverride("font_size", ClientUi.Small);
        game.AddChild(button); button.Size = new Vector2(ClientUi.CompanionPortraitSize, 21);
        return button;
    }

    private void RefreshCompanionPortraits()
    {
        if (State.User is not { } user) return;
        heroPortrait.Visible = summonHero.Visible = hudHeroMode.Visible = user.HasHero;
        var hero = State.Hero;
        var job = hero?.Class ?? State.ActiveHero?.Class; var gender = hero?.Gender ?? State.ActiveHero?.Gender;
        heroPortrait.Portrait = !user.HasHero || job == null ? null : Assets.Sprite("Prguse", 1400 + (int)job + 10 * (int)gender!)?.Texture;
        heroPortrait.FrameSource = Assets.Sprite("Prguse", 1)?.Texture;
        heroPortrait.Level = hero?.Level ?? State.ActiveHero?.Level ?? 0; heroPortrait.SpawnState = State.HeroState;
        heroPortrait.TooltipText = hero != null ? $"{hero.Name} · {hero.Level}级{hero.Class.ToLocalizedString()} · 点击查看英雄" :
            State.ActiveHero is { } saved ? $"{saved.Name} · {saved.Level}级{saved.Class.ToLocalizedString()} · 休息中" : "暂无英雄资料 · 前往盟重英雄使者管理英雄";
        var stats = State.CalculateHeroStats(); var active = State.HeroState == HeroSpawnState.Summoned;
        heroPortrait.MaximumHealth = Math.Max(1, stats[Stat.HP]); heroPortrait.Health = active ? hero?.HP ?? 0 : 0;
        heroPortrait.MaximumMana = Math.Max(1, stats[Stat.MP]); heroPortrait.Mana = active ? hero?.MP ?? 0 : 0;
        if (active) heroPortrait.TooltipText += $"\n生命 {heroPortrait.Health}/{heroPortrait.MaximumHealth} · 魔力 {heroPortrait.Mana}/{heroPortrait.MaximumMana}";
        var toggling = Environment.TickCount64 < heroToggleAt;
        var recovering = State.HeroState == HeroSpawnState.Dead;
        var recoveryWait = recovering && (!State.HeroRecoveryKnown || State.HeroRecoverySeconds > 0);
        summonHero.Text = active ? "休息" : recoveryWait ? State.HeroRecoveryKnown ? $"{State.HeroRecoverySeconds}秒" : "恢复中" : "召唤";
        summonHero.TooltipText = recovering ? "英雄已阵亡，死亡后60秒可召唤，恢复10%生命。" : active ? "收回英雄，须脱离战斗。" : "召唤英雄。";
        if (recovering) heroPortrait.TooltipText = $"{State.ActiveHero?.Name ?? hero?.Name} · 已阵亡\n" + (recoveryWait ? "等待恢复后可召唤" : "可重新召唤，恢复10%生命");
        summonHero.Disabled = !user.HasHero || heroItemPending != null || State.Me?.Dead != false || toggling || recoveryWait;
        // 素材主题异步就绪后，收回默认主题曾撑大的按钮高度。
        if (summonHero.Size != hudHeroMode.Size) summonHero.Size = hudHeroMode.Size;
        hudHeroMode.Select(hudHeroMode.GetItemIndex((int)user.HeroBehaviour));
        hudHeroMode.Disabled = State.HeroState != HeroSpawnState.Summoned;
        var assigned = user.IntelligentCreatures.FirstOrDefault(pet => user.CreatureSummoned && pet.PetType == user.SummonedCreatureType);
        var summons = State.Actors.Values.Where(actor => actor.Kind == ObjectType.Monster && !actor.Dead && !actor.Hidden && actor.MasterObjectId == user.ObjectID && actor.Image < 10000)
            .OrderBy(actor => actor.Image).ThenBy(actor => actor.Id).Take(petPortraits.Length).ToArray();
        var offset = assigned != null ? 1 : 0;
        var rosterChanged = false;
        for (var i = 0; i < petPortraits.Length; i++)
        {
            var button = petPortraits[i]; var summon = i >= offset ? summons.ElementAtOrDefault(i - offset) : null;
            var creature = i < offset ? assigned : null;
            rosterChanged |= portraitSummons[i] != (summon?.Id ?? 0);
            portraitSummons[i] = summon?.Id ?? 0;
            button.Visible = creature != null || summon != null;
            if (!button.Visible) continue;
            portraitPets[i] = creature?.PetType ?? IntelligentCreatureType.None;
            var frame = summon != null ? SummonPortrait(summon) : null;
            button.Icon = (frame ?? Assets.Sprite("Prguse2", creature?.Icon ?? 431))?.Texture;
            button.TooltipText = (summon?.Name ?? creature?.CustomName ?? "宠物") + " · 点击查看详情";
        }
        hudPetMode.Select(hudPetMode.GetItemIndex((int)State.CurrentPetMode));
        hudPetMode.Visible = petPortraits.Any(button => button.Visible);
        if (social.Visible && socialPage == 2) { if (rosterChanged) RenderSocial(); else UpdateSocialDetail(); }
    }

    private SpriteImage? SummonPortrait(Actor actor)
    {
        var library = $"Monster/{actor.Image:000}";
        var frames = Assets.Frames(library)?.Frames;
        if (frames?.TryGetValue(MirAction.Standing, out var standing) != true) return null;
        return Assets.Sprite(library, standing.Start + (int)MirDirection.Down * (standing.Count + standing.Skip));
    }
}
