namespace Client.MirObjects;

public static partial class OriginalSoundRules
{
    public static int Attack(Monster image, byte type) => type switch
    {
        1 when (int)image >= 10000 => (int)image - 10000 + 10500,
        _ when (int)image >= 10000 => -1,
        1 => SecondAttack(image), 2 => ThirdAttack(image), 3 => FourthAttack(image), 4 => (int)image * 10 + 9, _ => (int)image * 10 + 1
    };

    public static int RangeAttack(Monster image, byte type) => type switch
    { _ when (int)image >= 10000 => -1, 1 => SecondRange(image), 2 => ThirdRange(image), _ => Range(image) };

    public static int AttackFrameSound(Monster image, MirAction action, int frame) => (image, action, frame) switch
    {
        _ when (int)image >= 10000 => -1,
        (Monster.ToxicGhoul, MirAction.Attack1, -1) => (int)image * 10 + 4,
        (_, MirAction.Attack1, 3) when image is not (Monster.DarkCaptain or Monster.EvilMir or Monster.DragonStatue) => (int)image * 10 + 4,
        (_, MirAction.Attack2, 3) when image is not (Monster.DarkCaptain or Monster.EvilMir or Monster.DragonStatue) => (int)image * 10 + 4,
        (Monster.StoningStatue, MirAction.Attack2, 10) => (int)image * 10 + 7,
        (Monster.HornedSorceror, MirAction.Attack3, 5) => (int)image * 10 + 5,
        (Monster.HornedCommander, MirAction.Attack3, 5) => 8452,
        (Monster.HornedCommander, MirAction.Attack4, 5) => 8454,
        (Monster.DragonStatue, MirAction.AttackRange1, -1) => (int)image * 10 + 6,
        (Monster.GuardianRock, MirAction.AttackRange1, 1) => (int)image * 10 + 6,
        (Monster.BlueSoul, MirAction.AttackRange1, 1) => (int)image * 10 + 7,
        (Monster.FurbolgArcher or Monster.FurbolgGuard or Monster.ZumaArcher or Monster.BoneArcher or
            Monster.RedThunderZuma or Monster.FrozenRedZuma or Monster.Yimoogi or Monster.RedYimoogi or
            Monster.Snake10 or Monster.Snake11 or Monster.Snake12 or Monster.Snake13 or Monster.Snake14 or
            Monster.Snake15 or Monster.Snake16 or Monster.Snake17 or Monster.HolyDeva or Monster.RedFoxman or
            Monster.WhiteFoxman or Monster.TrapRock or Monster.HellBolt or Monster.WitchDoctor or
            Monster.IceGuard or Monster.ElementGuard or Monster.KingHydrax, MirAction.AttackRange1, 4) => (int)image * 10 + 6,
        (Monster.FinialTurtle, MirAction.AttackRange1, 4) => 20412,
        (Monster.TrollBomber or Monster.TrollStoner, MirAction.AttackRange1, 4) => (int)image * 10 + 9,
        (Monster.OmaWitchDoctor, MirAction.AttackRange1, 5) => (int)image * 10 + 7,
        (Monster.IcePhantom or Monster.FloatingRock, MirAction.AttackRange1, 7) => (int)image * 10 + 6,
        (Monster.FurbolgArcher or Monster.RedFoxman or Monster.WhiteFoxman, MirAction.AttackRange2, 4) => (int)image * 10 + 7,
        (Monster.TrollKing, MirAction.AttackRange2, 4) => (int)image * 10 + 9,
        (Monster.AncientBringer or Monster.IceGuard or Monster.CatShaman or Monster.IcePhantom, MirAction.AttackRange2, 4) => (int)image * 10 + 6,
        _ => -1
    };

    public static int MonsterStep(Monster image, bool right) => image switch
    {
        Monster.WingedTigerLord => (int)image * 10 + 8,
        Monster.PoisonHugger or Monster.SnowWolfKing => (int)image * 10 + (right ? 9 : 8),
        Monster.Catapult or Monster.ChariotBallista when !right => (int)image * 10 + 8,
        Monster.AvengerPlant when right => (int)image * 10 + 8,
        _ => -1
    };

    public static int Summon(Monster image) => image switch
    {
        Monster.HellKnight1 or Monster.HellKnight2 or Monster.HellKnight3 or
        Monster.HellKnight4 or Monster.LightningBead or Monster.HealingBead or
        Monster.PowerUpBead => (int)image * 10,
        Monster.BoneFamiliar or Monster.Shinsu or Monster.HolyDeva => (int)image * 10 + 5,
        _ => -1
    };

    public static int Appear(Monster image, bool stoned = false) => image switch
    {
        _ when (int)image >= 10000 => -1,
        // 骷髅召唤走078-5；已在场的召唤物和静止场景对象无需另播出场声。
        Monster.BoneFamiliar or Monster.ChestnutTree or Monster.EbonyTree or Monster.LargeMushroom or
        Monster.CherryTree or Monster.ArcherGuard or Monster.ArcherGuard2 or Monster.ArcherGuard3 or Monster.KatanaGuard => -1,
        Monster.CannibalPlant or Monster.WaterDragon or Monster.EvilCentipede or
        Monster.CreeperPlant => -1,
        Monster.ZumaArcher or Monster.ZumaStatue or Monster.ZumaGuardian or
        Monster.RedThunderZuma or Monster.FrozenRedZuma or Monster.FrozenZumaStatue or
        Monster.FrozenZumaGuardian or Monster.ZumaTaurus or Monster.DemonGuard or
        Monster.Turtlegrass or Monster.ManTree or Monster.EarthGolem or
        Monster.AssassinScroll or Monster.WarriorScroll or Monster.TaoistScroll or
        Monster.WizardScroll or Monster.PurpleFaeFlower => stoned ? -1 : (int)image * 10,
        Monster.DragonStatue => (int)image * 10 + 6,
        _ => (int)image * 10
    };

    public static int Popup(Monster image) => image switch
    {
        Monster.ZumaTaurus or Monster.DigOutZombie or Monster.Armadillo or
        Monster.ArmadilloElder => (int)image * 10 + 5,
        Monster.Shinsu => (int)image * 10 + 6,
        _ => (int)image * 10
    };

    public static int SecondAttack(Monster image) => (int)image * 10 + 6;

    public static int ThirdAttack(Monster image) => image switch
    {
        Monster.DarkCaptain or Monster.HornedSorceror or Monster.HornedCommander => -1,
        _ => (int)image * 10 + 7
    };

    public static int FourthAttack(Monster image) => image switch
    {
        Monster.HornedCommander => -1,
        Monster.SnowWolfKing => (int)image * 10 + 5,
        _ => (int)image * 10 + 8
    };

    public static int Range(Monster image) => image switch
    {
        Monster.RedThunderZuma or Monster.FrozenRedZuma or Monster.KingScorpion or
        Monster.DarkDevil or Monster.Khazard or Monster.BoneLord or
        Monster.LeftGuard or Monster.RightGuard or Monster.FrostTiger or
        Monster.GreatFoxSpirit or Monster.BoneSpearman or Monster.MinotaurKing or
        Monster.WingedTigerLord or Monster.ManectricClaw or Monster.ManectricKing or
        Monster.HellBolt or Monster.WitchDoctor or Monster.FlameSpear or
        Monster.FlameMage or Monster.FlameScythe or Monster.FlameAssassin or
        Monster.FlameQueen or Monster.DarkDevourer or Monster.DreamDevourer or
        Monster.FlyingStatue or Monster.IceGuard or Monster.ElementGuard or
        Monster.KingGuard or Monster.Yimoogi or Monster.RedYimoogi or
        Monster.Snake10 or Monster.Snake11 or Monster.Snake12 or
        Monster.Snake13 or Monster.Snake14 or Monster.Snake15 or
        Monster.Snake16 or Monster.Snake17 or Monster.BurningZombie or
        Monster.MudZombie or Monster.FrozenZombie or Monster.UndeadWolf or
        Monster.CatShaman or Monster.CannibalTentacles or Monster.SwampWarrior or
        Monster.GeneralMeowMeow or Monster.RhinoPriest or Monster.HardenRhino or
        Monster.TreeGuardian or Monster.OmaCannibal or Monster.OmaMage or
        Monster.OmaWitchDoctor or Monster.CreeperPlant or Monster.AvengingSpirit or
        Monster.AvengingWarrior or Monster.PeacockSpider or Monster.FlamingMutant or
        Monster.KingHydrax or Monster.DarkCaptain or Monster.DarkOmaKing or
        Monster.HornedMage or Monster.FrozenKnight or Monster.IcePhantom or
        Monster.WaterDragon or Monster.BlackTortoise or Monster.EvilMir or
        Monster.DragonStatue => (int)image * 10 + 5,
        Monster.AncientBringer or Monster.SeedingsGeneral => (int)image * 10 + 7,
        Monster.RestlessJar => (int)image * 10 + 8,
        Monster.TucsonGeneral => -1,
        _ => (int)image * 10 + 1
    };

    public static int SecondRange(Monster image) => image switch
    {
        Monster.TucsonGeneral => (int)image * 10 + 5,
        Monster.TurtleKing => -1,
        Monster.KingGuard or Monster.TreeGuardian or Monster.DarkCaptain or
        Monster.HornedCommander => (int)image * 10 + 7,
        Monster.AncientBringer or Monster.SeedingsGeneral => (int)image * 10 + 8,
        _ => (int)image * 10 + 6
    };

    public static int ThirdRange(Monster image) => image switch
    {
        Monster.TucsonGeneral => (int)image * 10 + 7,
        _ => ThirdAttack(image)
    };

    public static int Dead(Monster image) => image switch
    {
        Monster.CaveBat or Monster.HellKnight1 or Monster.HellKnight2 or
        Monster.HellKnight3 or Monster.HellKnight4 or Monster.CyanoGhast or
        Monster.WoodBox => (int)image * 10 + 5,
        _ => -1
    };

}
