using Crystal.Native;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;
using S = ServerPackets;

internal static class HeroRecoveryChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }
    private sealed class Hero(HeroInfo info, Player owner) : HeroObject(info, owner)
    {
        protected override void Load(CharacterInfo info, Server.MirNetwork.MirConnection connection)
        { Info = info; HInfo = (HeroInfo)info; }
    }
    public static void Run(Action<bool, string> check)
    {
        check(Globals.MaxCharacterCount == 5, "角色上限不是5");
        var account = new AccountInfo();
        for (var i = 0; i < 5; i++) account.Characters.Add(new CharacterInfo { Index = i + 1, Name = "选角" + i });
        check(account.GetSelectInfo().Count == 5 && account.GetSelectInfo()[4].Index == 5, "选角列表漏掉第五角色");
        account.Characters[0].Deleted = true;
        check(account.GetSelectInfo().Count == 4 && account.GetSelectInfo()[3].Index == 5, "已删除角色占用选角位置");
        var now = DateTime.UtcNow;
        var hero = new HeroInfo(new ClientPackets.NewHero { Name = "恢复检查" }) { Index = 70, HP = 0, ReviveAtUtc = now.AddSeconds(60) };
        check(hero.RecoveryRemaining(now) == 60000 && hero.RecoveryRemaining(now.AddSeconds(59)) == 1000, "英雄恢复计时不满60秒");
        check(hero.RecoveryRemaining(now.AddSeconds(60)) == 0 && hero.RecoveryRemaining(now.AddSeconds(61)) == 0, "恢复边界未开放或出现负倒计时");
        hero.HP = 1; check(hero.RecoveryRemaining(now) == 0, "存活英雄被旧死亡时间阻挡"); hero.HP = 0;
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true)) hero.Save(writer);
        stream.Position = 0;
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, true);
        var restored = new HeroInfo(reader, Envir.Version, Envir.CustomVersion);
        check(restored.ReviveAtUtc == hero.ReviveAtUtc && stream.Position == stream.Length, "死亡时间存档往返改变或截断");
        using var legacy = new MemoryStream(stream.ToArray()[..^8]); using var oldReader = new BinaryReader(legacy);
        var old = new HeroInfo(oldReader, Envir.Version, 2);
        check(old.HP == 0 && old.RecoveryRemaining(DateTime.UtcNow) is > 59000 and <= 60000 && legacy.Position == legacy.Length,
            "旧死亡英雄存档不能完整读取或升级绕过恢复期");
        var owner = new Player { Info = new CharacterInfo { Heroes = [hero] } }; owner.CurrentHero = hero;
        new Hero(hero, owner) { Dead = true }.Die();
        check(hero.ReviveAtUtc == now.AddSeconds(60) && owner.Packets.Count == 0, "重复死亡重置恢复期或重复结算");
        owner.ReadHeroRecovery();
        check(!owner.CanRecoverHero() && owner.Packets.OfType<S.HeroRecovery>().Last().Milliseconds > 0 &&
            owner.Packets.OfType<S.UpdateHeroSpawnState>().Last().State == HeroSpawnState.Dead, "服务端未拒绝冷却召唤或未同步死亡状态");
        hero.ReviveAtUtc = now.AddSeconds(-1); check(owner.CanRecoverHero(), "冷却结束后仍不能召唤");
        var state = new GameState(); state.Apply(new S.UserInformation { Inventory = [], Equipment = new UserItem[14] });
        state.Apply(new S.ManageHeroes { CurrentHero = hero.ClientInformation, Heroes = [] });
        state.Apply(new S.HeroRecovery { HeroIndex = hero.Index, Milliseconds = 60000 });
        state.Apply(new S.UpdateHeroSpawnState { State = HeroSpawnState.Dead });
        check(state.HeroRecoveryKnown && state.HeroRecoverySeconds == 60 && state.HeroState == HeroSpawnState.Dead, "微端未归约英雄冷却");
        state.Apply(new S.HeroRecovery { HeroIndex = hero.Index + 1, Milliseconds = 60000 });
        check(!state.HeroRecoveryKnown, "其他英雄的冷却串到当前英雄");
        state.Apply(new S.LogOutSuccess()); check(!state.HeroRecoveryKnown, "冷却跨角色残留");
        Packet.IsServer = false;
        var packet = new S.HeroRecovery { HeroIndex = 70, Milliseconds = 45000 };
        var decoded = (S.HeroRecovery)Packet.ReceivePacket(packet.GetPacketBytes().ToArray(), out var remaining);
        check(decoded.HeroIndex == 70 && decoded.Milliseconds == 45000 && remaining.Length == 0, "恢复追加协议往返失败");
        check(new S.UpdateHeroSpawnState().GetPacketBytes().Count() == 5, "改变了既有英雄状态包布局");
    }
}
