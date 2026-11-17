namespace Server;

public static partial class Settings
{
    public static bool AutoLearnSkills;
    public static Spell[] AutoLearnWarriorSkills = [], AutoLearnWizardSkills = [], AutoLearnTaoistSkills = [],
        AutoLearnAssassinSkills = [], AutoLearnArcherSkills = [];

    public static Spell[] AutoLearnFor(MirClass role) => role switch
    {
        MirClass.Warrior => AutoLearnWarriorSkills,
        MirClass.Wizard => AutoLearnWizardSkills,
        MirClass.Taoist => AutoLearnTaoistSkills,
        MirClass.Assassin => AutoLearnAssassinSkills,
        MirClass.Archer => AutoLearnArcherSkills,
        _ => []
    };

    public static void LoadAutoLearnSkills(InIReader reader)
    {
        Spell[] Read(MirClass role)
        {
            var raw = reader.ReadString("AutoLearnSkills", role.ToString(), "", false);
            if (raw.Length > 4096) throw new InvalidDataException("自动学习技能配置过长：" + role);
            var values = raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var skills = new List<Spell>();
            foreach (var value in values)
            {
                if (!byte.TryParse(value, out var id) || !Enum.IsDefined((Spell)id) || (Spell)id == Spell.None || skills.Contains((Spell)id))
                    throw new InvalidDataException("自动学习技能配置无效：" + role + "/" + value);
                skills.Add((Spell)id);
            }
            return skills.ToArray();
        }
        var warrior = Read(MirClass.Warrior); var wizard = Read(MirClass.Wizard); var taoist = Read(MirClass.Taoist);
        var assassin = Read(MirClass.Assassin); var archer = Read(MirClass.Archer);
        AutoLearnSkills = reader.ReadBoolean("AutoLearnSkills", "Enabled", false, false);
        AutoLearnWarriorSkills = warrior; AutoLearnWizardSkills = wizard; AutoLearnTaoistSkills = taoist;
        AutoLearnAssassinSkills = assassin; AutoLearnArcherSkills = archer;
    }

    private static void SaveAutoLearnSkills()
    {
        Reader.Write("AutoLearnSkills", "Enabled", AutoLearnSkills);
        foreach (var role in Enum.GetValues<MirClass>())
            Reader.Write("AutoLearnSkills", role.ToString(), string.Join(',', AutoLearnFor(role).Select(spell => (byte)spell)));
    }
}
