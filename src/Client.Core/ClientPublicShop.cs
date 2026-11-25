using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public sealed class ClientPublicShop
{
    private int sequence, pending;
    private long sentAt;
    public bool Pending => pending != 0;
    public bool TimedOut(long now) => Pending && now - sentAt >= 5000;
    public C.PublicShopBuy? Buy(GameState state, PublicShopEntry offer, ushort count, long now)
    {
        if (Pending || state.User == null || state.Me?.Dead != false || state.Trading || state.StorageActive || count == 0 || count > offer.MaxCount ||
            state.PublicShop?.Items.Contains(offer) != true || (ulong)offer.Price * count > state.User.Gold) return null;
        pending = ++sequence; sentAt = now;
        return new C.PublicShopBuy { RequestId = pending, ItemIndex = offer.ItemIndex, UnitPrice = offer.Price, Count = count };
    }
    public void Reset() => pending = 0;
    public void Observe(Packet packet)
    {
        if (packet is S.PublicShopResult result && result.RequestId == pending) pending = 0;
        // 切图、死亡及关闭弹窗不能证明交易失败，未知结果保留锁直到回执或断线。
        if (packet is S.Disconnect or S.ReturnToLogin or S.LogOutSuccess) Reset();
    }
}
