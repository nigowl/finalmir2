using Client.MirObjects;
using Crystal.Native;
using Godot;
using S = ServerPackets;

public partial class Main
{
    private AudioStreamPlayer music = null!;
    private AudioStreamPlayer[] effectPlayers = [];
    private int nextEffect;
    private long lastStepMotion;
    private int lastStepPhase = -1;
    internal event Action<int>? EffectPlayed;

    private void BuildAudio()
    {
        music = new AudioStreamPlayer { VolumeDb = -20 }; AddChild(music);
        music.Finished += () => music.Play();
        effectPlayers = Enumerable.Range(0, 6).Select(_ => new AudioStreamPlayer { VolumeDb = -12 }).ToArray();
        foreach (var player in effectPlayers) AddChild(player);
    }

    private async Task PlaySound(int id, bool background = false)
    {
        var generation = musicGeneration;
        if (background) generation = ++musicGeneration;
        try
        {
            var audio = await Assets.Sound(id);
            if (audio == null || !IsInsideTree() || generation != musicGeneration) return;
            var player = background ? music : effectPlayers.FirstOrDefault(effect => !effect.Playing) ?? effectPlayers[nextEffect++ % effectPlayers.Length];
            player.Stop(); player.Stream = audio; player.Play();
            if (!background) EffectPlayed?.Invoke(id);
        }
        catch (Exception error) { GD.PrintErr($"声音 {id}：{error.Message}"); }
    }

    private void ProcessFootsteps()
    {
        var me = State.Me;
        if (me == null || me.Dead || World.Map == null || me.Action is not (MirAction.Walking or MirAction.Running)) return;
        var elapsed = Environment.TickCount64 - me.MotionTime;
        if (elapsed < 100 || elapsed >= Actor.MoveDuration) return;
        var phase = elapsed >= 400 ? 1 : 0;
        if (lastStepMotion == me.MotionTime && lastStepPhase == phase) return;
        lastStepMotion = me.MotionTime; lastStepPhase = phase;
        var x = Math.Clamp(me.Location.X & ~1, 0, World.Map.Width - 1);
        var y = Math.Clamp(me.Location.Y & ~1, 0, World.Map.Height - 1);
        _ = PlaySound(OriginalSoundRules.Step(World.Map.MapCells[x, y], me.Action == MirAction.Running, phase == 1));
    }

    private void AudioPacket(Packet packet)
    {
        switch (packet)
        {
            case S.ObjectAttack attack when State.Actors.TryGetValue(attack.ObjectID, out var actor):
                _ = PlaySound(actor.Kind == ObjectType.Monster ? actor.Image * 10 + 1 : OriginalSoundRules.Swing(actor.Weapon, actor.Class == MirClass.Assassin)); break;
            case S.ObjectStruck struck when State.Actors.TryGetValue(struck.ObjectID, out var actor):
                _ = PlaySound(actor.Kind == ObjectType.Monster ? actor.Image * 10 + 2 : actor.Gender == MirGender.Male ? 10138 : 10139);
                if (struck.AttackerID == State.User?.ObjectID) _ = PlaySound(10070);
                break;
            case S.Struck: _ = PlaySound(State.Me?.Gender == MirGender.Female ? 10139 : 10138); break;
            case S.ObjectDied died when State.Actors.TryGetValue(died.ObjectID, out var actor):
                _ = PlaySound(actor.Kind == ObjectType.Monster ? actor.Image * 10 + 3 : actor.Gender == MirGender.Male ? 10144 : 10145); break;
            case S.Death: _ = PlaySound(State.Me?.Gender == MirGender.Female ? 10145 : 10144); break;
            case S.MapChanged: _ = PlaySound(10110); break;
            case S.ObjectMagic magic when magic.Cast && (magic.ObjectID != State.User?.ObjectID || magic.SelfBroadcast):
                _ = PlaySound(20000 + (int)magic.Spell * 10); break;
        }
    }
}
