using Crystal.Native;
using C = ClientPackets;

public partial class Main
{
    private bool healthDisplayRequested;

    private void RequestEquipmentGlow()
    {
        if (State.User != null && Assets.SupportsItemPrices && HandshakeReady) Send(new C.ItemPricesRequest());
        if (State.User != null && Assets.SupportsEquipmentGlow && HandshakeReady) Send(new C.ClientPresentationRequest());
    }

    private void RequestHealthDisplay()
    {
        if (!preferences.ShowHealthBars || healthDisplayRequested || State.User == null || !Assets.SupportsHealthDisplay || !HandshakeReady) return;
        healthDisplayRequested = true;
        Send(new C.HealthDisplayRequest());
    }
}
