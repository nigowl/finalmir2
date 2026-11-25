using S = ServerPackets;

namespace Server.MirObjects;

public partial class PlayerObject
{
    private bool heroRecoveryRequested;
    public void ReadHeroRecovery()
    {
        heroRecoveryRequested = true;
        SendHeroRecovery();
    }

    public void SendHeroRecovery()
    {
        if (heroRecoveryRequested) Enqueue(new S.HeroRecovery { HeroIndex = CurrentHero?.Index ?? 0,
            Milliseconds = CurrentHero?.RecoveryRemaining(DateTime.UtcNow) ?? 0 });
    }

    public bool CanRecoverHero()
    {
        var remaining = CurrentHero?.RecoveryRemaining(DateTime.UtcNow) ?? 0;
        if (remaining == 0) return true;
        Enqueue(new S.UpdateHeroSpawnState { State = HeroSpawnState.Dead });
        SendHeroRecovery();
        ReceiveChat($"英雄已阵亡，还需等待 {(remaining + 999) / 1000} 秒才能召唤，召唤后恢复10%生命。", ChatType.Hint);
        return false;
    }
}
