namespace Crystal.Native;

public sealed class ClientPreferences
{
    public int Volume { get; set; } = 100;
    public bool DefaultRun { get; set; }
    public bool ShowNpcNames { get; set; } = true;
    public bool ShowMonsterNames { get; set; }
    public bool ShowItemNames { get; set; }
    public bool CrtFilter { get; set; }
    public int CrtStrength { get; set; } = 3;
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
    public Spell SingleSkill { get; set; }
    public Spell AreaSkill { get; set; }
    public bool AutoSwapAmulet { get; set; }
    public bool SmartHalfMoon { get; set; }
    public bool SmartThrusting { get; set; }
    public bool AutoPoison { get; set; }
    public bool AutoSummon { get; set; }
    public Spell SummonSkill { get; set; }
    public bool AutoHeal { get; set; }
    public int HealPercent { get; set; } = 70;
    public HashSet<Spell> AutoSkills { get; set; } = new();

    public void Normalize()
    {
        Volume = Math.Clamp(Volume, 0, 100);
        CrtStrength = Math.Clamp(CrtStrength, 1, 5);
        HpPercent = Math.Clamp(HpPercent, 1, 95);
        MpPercent = Math.Clamp(MpPercent, 1, 95);
        RandomPercent = Math.Clamp(RandomPercent, 1, 95);
        TownPercent = Math.Clamp(TownPercent, 1, 95);
        HealPercent = Math.Clamp(HealPercent, 1, 95);
        AutoSkills ??= new(); AutoSkills.RemoveWhere(spell => !ClassSkillRules.CanMaintain(spell));
        if (SummonSkill != Spell.None && !ClassSkillRules.Summons(MirClass.Taoist).Contains(SummonSkill)) SummonSkill = Spell.None;
        if (!ClientAutomation.IsSingleSkill(SingleSkill)) SingleSkill = Spell.None;
        if (!ClientAutomation.IsAreaSkill(AreaSkill)) AreaSkill = Spell.None;
    }
}
