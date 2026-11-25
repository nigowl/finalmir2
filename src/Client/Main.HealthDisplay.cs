using Crystal.Native;
using C = ClientPackets;

public partial class Main
{
    private bool healthDisplayRequested;

    private void RequestHealthDisplay()
    {
        if (!preferences.ShowHealthBars || healthDisplayRequested || State.User == null || !Assets.SupportsHealthDisplay || !HandshakeReady) return;
        healthDisplayRequested = true;
        Send(new C.HealthDisplayRequest());
    }
}
