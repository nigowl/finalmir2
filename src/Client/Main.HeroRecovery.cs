using C = ClientPackets;

public partial class Main
{
    private void RequestHeroRecovery()
    {
        if (Assets.SupportsHeroRecovery) Send(new C.HeroRecoveryRequest());
    }
}
