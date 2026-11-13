using Crystal.Native;
using Server.Helpers;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class AttackModeChecks
{
    public static void Run(Action<bool, string> check)
    {
        var state = new GameState();
        state.Apply(new S.ChangeAMode { Mode = AttackMode.Mentor });
        check(state.CurrentAttackMode == AttackMode.Mentor, "攻击模式回执未更新客户端");
        state.Apply(new S.ChangeAMode { Mode = (AttackMode)255 });
        check(state.CurrentAttackMode == AttackMode.Mentor, "未知攻击模式覆盖了客户端状态");
        state.Reset(); check(state.CurrentAttackMode == AttackMode.Peace, "攻击模式跨角色泄漏");
        check((byte)AttackMode.All == 5 && (byte)AttackMode.Couple == 6 && (byte)AttackMode.Mentor == 7, "追加攻击模式改变了原编号");
        var map = new Map(new MapInfo { Index = 900, Title = "模式检查" });
        PlayerObject Player(int id) => new() { Info = new CharacterInfo { Index = id }, CurrentMap = map };
        var attacker = Player(1); var partner = Player(2); var stranger = Player(3);
        attacker.Node = new LinkedListNode<MapObject>(attacker);
        attacker.AMode = AttackMode.Couple; attacker.Info.Married = 2;
        check(!partner.IsAttackTarget(attacker) && stranger.IsAttackTarget(attacker), "夫妻模式误伤配偶或保护了无关玩家");
        check(partner.IsFriendlyTarget(attacker) && !stranger.IsFriendlyTarget(attacker), "夫妻模式友方技能目标错误");
        attacker.AMode = AttackMode.Mentor; attacker.Info.Mentor = 2; partner.Info.Mentor = 1;
        check(!partner.IsAttackTarget(attacker) && stranger.IsAttackTarget(attacker), "师徒模式未保护实际关系");
        var pet = new MonsterObject(new MonsterInfo { AI = 0 }) { Master = partner };
        check(!pet.IsAttackTarget(attacker) && pet.IsFriendlyTarget(attacker), "师徒宠物未沿用关系保护");
        pet.Master = stranger;
        check(pet.IsAttackTarget(attacker) && !pet.IsFriendlyTarget(attacker), "关系模式保护了陌生玩家宠物");
        var attackingPet = new MonsterObject(new MonsterInfo { AI = 0 }) { Master = attacker, Node = new LinkedListNode<MapObject>(attacker) };
        partner.LastHitter = attacker;
        check(!partner.IsAttackTarget(attackingPet), "宠物攻击绕过了主人的师徒模式");
        attacker.AMode = AttackMode.All;
        check(partner.IsAttackTarget(attacker) && pet.IsAttackTarget(attacker), "全体模式被关系保护意外改变");
        attacker.InSafeZone = true;
        check(!stranger.IsAttackTarget(attacker), "攻击模式绕过了原安全区保护");
        attacker.InSafeZone = false; map.Info.NoFight = true;
        check(!stranger.IsAttackTarget(attacker), "攻击模式绕过了原地图禁战");
    }
}
