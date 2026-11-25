namespace Crystal.Native;

public sealed class ClientPreferences
{
    public int Volume { get; set; } = 100;
    public bool SoundEnabled { get; set; } = true;
    public bool DefaultRun { get; set; }
    public bool ShowHealthBars { get; set; }
    public bool ShowNpcNames { get; set; } = true;
    public bool ShowMonsterNames { get; set; }
    public bool ShowItemNames { get; set; }
    public bool CrtFilter { get; set; }
    public int CrtStrength { get; set; } = 3;
    public int CrtCurvature { get; set; } = 3;
    public bool AutoHp { get; set; }
    public int HpPercent { get; set; } = 50;
    public bool AutoMp { get; set; }
    public int MpPercent { get; set; } = 30;
    public bool AutoRandom { get; set; }
    public int RandomPercent { get; set; } = 30;
    public bool AutoTown { get; set; }
    public int TownPercent { get; set; } = 20;
    public bool NoMonsterRandom { get; set; }
    public bool AutoRoam { get; set; } = true;
    public bool AutoPickup { get; set; }
    public bool PickupWhitelist { get; set; }
    public string[] PickupNames { get; set; } = [];
    public bool AutoSell { get; set; }
    public bool SellWhitelist { get; set; } = true;
    public string[] SellNames { get; set; } = [];
    public Dictionary<string, ItemActions> ItemRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public ItemActions ForItem(string name)
    {
        if (ItemRules.TryGetValue(name, out var actions)) return actions;
        var pickup = PickupNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        var recycle = SellNames.Contains(name, StringComparer.OrdinalIgnoreCase);
        return (ShowItemNames ? ItemActions.Display : 0) | ((PickupWhitelist ? pickup : !pickup) ? ItemActions.Pickup : 0) |
            ((SellWhitelist ? recycle : SellNames.Length > 0 && !recycle) ? ItemActions.Recycle : 0);
    }
    public bool SmartBattle { get; set; } = true;
    public bool AutoSwapAmulet { get; set; }
    public bool SmartHalfMoon { get; set; }
    public bool SmartThrusting { get; set; }
    public bool AutoPoison { get; set; }
    public bool AutoSummon { get; set; }
    public bool AutoMirroring { get; set; }
    public Spell SummonSkill { get; set; }
    public bool AutoHeal { get; set; }
    public int HealPercent { get; set; } = 70;
    public HashSet<Spell> AutoSkills { get; set; } = new();

    public void Normalize()
    {
        Volume = Math.Clamp(Volume, 0, 100);
        CrtStrength = Math.Clamp(CrtStrength, 1, 5);
        CrtCurvature = Math.Clamp(CrtCurvature, 0, 5);
        HpPercent = Math.Clamp(HpPercent, 1, 95);
        MpPercent = Math.Clamp(MpPercent, 1, 95);
        RandomPercent = Math.Clamp(RandomPercent, 1, 95);
        TownPercent = Math.Clamp(TownPercent, 1, 95);
        HealPercent = Math.Clamp(HealPercent, 1, 95);
        PickupNames = NormalizeNames(PickupNames);
        SellNames = NormalizeNames(SellNames);
        ItemRules = (ItemRules ?? []).Where(pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Key.Length <= 80)
            .GroupBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Take(4096)
            .ToDictionary(group => group.Key, group => group.Last().Value & (ItemActions.Display | ItemActions.Pickup | ItemActions.Recycle), StringComparer.OrdinalIgnoreCase);
        AutoSkills ??= new(); AutoSkills.RemoveWhere(spell => !ClassSkillRules.CanMaintain(spell));
        if (SummonSkill != Spell.None && !ClassSkillRules.Summons(MirClass.Taoist).Contains(SummonSkill)) SummonSkill = Spell.None;
    }

    private static string[] NormalizeNames(string[]? names) => (names ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim())
        .Where(name => name.Length <= 40).Distinct(StringComparer.OrdinalIgnoreCase).Take(64).ToArray();
}
