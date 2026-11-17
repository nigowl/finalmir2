#nullable enable
namespace Mir2.Management;

public sealed partial record ServerSettings
{
    public Spell[] SkillsFor(MirClass role) => role switch
    {
        MirClass.Warrior => AutoLearnWarriorSkills,
        MirClass.Wizard => AutoLearnWizardSkills,
        MirClass.Taoist => AutoLearnTaoistSkills,
        MirClass.Assassin => AutoLearnAssassinSkills,
        MirClass.Archer => AutoLearnArcherSkills,
        _ => []
    };

    public void SetSkills(MirClass role, Spell[] skills)
    {
        switch (role)
        {
            case MirClass.Warrior: AutoLearnWarriorSkills = skills; break;
            case MirClass.Wizard: AutoLearnWizardSkills = skills; break;
            case MirClass.Taoist: AutoLearnTaoistSkills = skills; break;
            case MirClass.Assassin: AutoLearnAssassinSkills = skills; break;
            case MirClass.Archer: AutoLearnArcherSkills = skills; break;
        }
    }
}
