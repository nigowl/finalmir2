namespace Server;

public static partial class Settings
{
    public static bool CanCreateClass(MirClass role) => role switch
    {
        MirClass.Warrior => AllowCreateWarrior,
        MirClass.Wizard => AllowCreateWizard,
        MirClass.Taoist => AllowCreateTaoist,
        MirClass.Assassin => AllowCreateAssassin,
        MirClass.Archer => AllowCreateArcher,
        _ => false
    };
}
