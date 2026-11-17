namespace Client.MirObjects;

public readonly record struct PlayerArt(string Body, string Hair, string Weapon, string SecondWeapon, int BodyOffset, int WeaponOffset);

public static class OriginalPlayerArt
{
    public static int SelectionStart(MirClass role, MirGender gender) => role == MirClass.Archer ? (gender == MirGender.Male ? 100 : 160) :
        20 + (int)role * 20 + (gender == MirGender.Female ? 280 : 0);
    public static bool HasBow(MirClass @class, int weapon) => @class == MirClass.Archer && weapon / Globals.ClassWeaponCount == 2;
    public static MirAction Animation(MirClass @class, int weapon, MirAction action) => HasBow(@class, weapon) ? action switch
    { MirAction.Walking => MirAction.WalkingBow, MirAction.Running => MirAction.RunningBow, _ => action } : action;

    public static PlayerArt Resolve(MirClass @class, MirGender gender, int armour, int hair, int weapon, MirAction action)
    {
        var assassinWeapon = @class == MirClass.Assassin && weapon / Globals.ClassWeaponCount == 1;
        var assassin = @class == MirClass.Assassin && (assassinWeapon || weapon < 0) && action is MirAction.Standing or MirAction.Stance
            or MirAction.Walking or MirAction.Running or MirAction.Die or MirAction.Dead or MirAction.Struck or MirAction.Attack1
            or MirAction.Attack2 or MirAction.Attack3 or MirAction.Attack4 or MirAction.Sneek or MirAction.Spell or MirAction.DashAttack;
        var bow = HasBow(@class, weapon);
        var archer = @class == MirClass.Archer && (action == MirAction.Jump || bow && action is MirAction.Walking or MirAction.Running
            or MirAction.WalkingBow or MirAction.RunningBow or MirAction.AttackRange1 or MirAction.AttackRange2);
        var female = gender == MirGender.Female;
        var body = assassin ? "A" : archer ? "AR" : "C";
        var mainWeapon = weapon < 0 ? "" : assassinWeapon ? $"AWeapon/{weapon - 100:00} R" : bow ? $"ARWeapon/{weapon - 200:00}{(archer ? " S" : "")}" : $"CWeapon/{weapon:00}";
        return new($"{body}Armour/{armour:00}", $"{body}Hair/{hair:00}", mainWeapon, assassinWeapon ? $"AWeapon/{weapon - 100:00} L" : "",
            female ? assassin ? 512 : archer ? 352 : 808 : 0, female ? assassin ? 512 : archer ? 352 : 416 : 0);
    }
}
