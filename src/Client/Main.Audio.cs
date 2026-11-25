using Client.MirObjects;
using Crystal.Native;
using Godot;
using S = ServerPackets;

public partial class Main
{
    private AudioStreamPlayer music = null!;
    private AudioStreamPlayer[] effectPlayers = [];
    private int nextEffect;
    private int effectGeneration;
    private readonly Dictionary<int, long> soundPlayedAt = new();
    private long lastStepMotion;
    private int lastStepPhase = -1;
    internal event Action<int>? EffectPlayed;

    private void BuildAudio()
    {
        AudioServer.AddBus();
        var effectsBus = AudioServer.BusCount - 1;
        AudioServer.SetBusName(effectsBus, "Effects");
        AudioServer.AddBusEffect(effectsBus, new AudioEffectHardLimiter { CeilingDb = -1 });
        music = new AudioStreamPlayer { VolumeDb = -18 }; AddChild(music);
        music.Finished += () => music.Play();
        effectPlayers = Enumerable.Range(0, 24).Select(_ => new AudioStreamPlayer { VolumeDb = -3, Bus = "Effects" }).ToArray();
        foreach (var player in effectPlayers) AddChild(player);
    }

    internal async Task PlaySound(int id, bool background = false)
    {
        if (id < 0 || !background && (!preferences.SoundEnabled || preferences.Volume == 0)) return;
        var generation = background ? ++musicGeneration : effectGeneration;
        var requestedAt = Environment.TickCount64;
        try
        {
            var audio = await Assets.Sound(id);
            if (audio == null || !IsInsideTree() || generation != (background ? musicGeneration : effectGeneration) || !background && (!preferences.SoundEnabled || preferences.Volume == 0)) return;
            if (!background)
            {
                // 冷缓存不能把过时战斗声集中补播；同帧同类怪物也只混一次。
                var now = Environment.TickCount64;
                if (now - requestedAt > 1500 || soundPlayedAt.TryGetValue(id, out var last) && now - last < 45) return;
                soundPlayedAt[id] = now;
            }
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
        var duration = me.MovementDuration(Actor.MoveDuration);
        if (elapsed < duration / 6 || elapsed >= duration) return;
        var phase = elapsed >= duration * 2 / 3 ? 1 : 0;
        if (lastStepMotion == me.MotionTime && lastStepPhase == phase) return;
        lastStepMotion = me.MotionTime; lastStepPhase = phase;
        var x = Math.Clamp(me.Location.X & ~1, 0, World.Map.Width - 1);
        var y = Math.Clamp(me.Location.Y & ~1, 0, World.Map.Height - 1);
        _ = PlaySound(OriginalSoundRules.Step(World.Map.MapCells[x, y], me.Action == MirAction.Running, phase == 1));
    }

    internal void AudioPacket(Packet packet)
    {
        switch (packet)
        {
            case S.ObjectAttack attack when attack.ObjectID != State.User?.ObjectID && State.Actors.TryGetValue(attack.ObjectID, out var actor):
                ActorSound(actor, actor.Kind == ObjectType.Monster ? OriginalSoundRules.Attack((Monster)actor.Image, attack.Type) : OriginalSoundRules.Swing(actor.Weapon, actor.Class == MirClass.Assassin));
                if (actor.Kind == ObjectType.Player) PlayMeleeSound(attack.Spell, actor.Gender, actor);
                else if (actor.Kind == ObjectType.Monster) World.QueueMonsterSounds(actor);
                break;
            case S.ObjectRangeAttack attack when State.Actors.TryGetValue(attack.ObjectID, out var actor) && actor.Kind == ObjectType.Monster:
                ActorSound(actor, OriginalSoundRules.RangeAttack((Monster)actor.Image, attack.Type)); World.QueueMonsterSounds(actor); break;
            case S.ObjectStruck struck when State.Actors.TryGetValue(struck.ObjectID, out var actor):
                ActorSound(actor, actor.Kind == ObjectType.Monster ? actor.Image * 10 + 2 : actor.Gender == MirGender.Male ? 10138 : 10139);
                if (State.Actors.GetValueOrDefault(struck.AttackerID) is { Kind: ObjectType.Player } attacker)
                    ActorSound(actor, actor.Image == (int)Monster.EvilMir && actor.Kind == ObjectType.Monster ? 10090 : OriginalSoundRules.Struck(attacker.Weapon));
                break;
            case S.Struck: _ = PlaySound(State.Me?.Gender == MirGender.Female ? 10139 : 10138); break;
            case S.ObjectDied died when State.Actors.TryGetValue(died.ObjectID, out var actor):
                ActorSound(actor, actor.Kind == ObjectType.Monster ? actor.Image * 10 + 3 : actor.Gender == MirGender.Male ? 10144 : 10145);
                if (actor.Kind == ObjectType.Monster) World.QueueMonsterDeath(actor); break;
            case S.ObjectShow show when State.Actors.GetValueOrDefault(show.ObjectID) is { Kind: ObjectType.Monster } actor:
                ActorSound(actor, OriginalSoundRules.Popup((Monster)actor.Image)); break;
            case S.ObjectWalk walk when State.Actors.GetValueOrDefault(walk.ObjectID) is { Kind: ObjectType.Monster } actor:
                World.QueueMonsterFootsteps(actor); break;
            case S.ObjectRun run when State.Actors.GetValueOrDefault(run.ObjectID) is { Kind: ObjectType.Monster, Image: (int)Monster.HardenRhino } actor:
                ActorSound(actor, actor.Image * 10 + 8); break;
            case S.ObjectRevived revived when State.Actors.TryGetValue(revived.ObjectID, out var actor):
                ActorSound(actor, actor.Kind == ObjectType.Player ? 20791 : actor.Image is (int)Monster.ClZombie or (int)Monster.NdZombie or (int)Monster.CrawlerZombie ? 705 : -1); break;
            case S.Revived: _ = PlaySound(20791); break;
            case S.PlaySound sound: _ = PlaySound(sound.Sound); break;
            case S.TeleportIn: _ = PlaySound(10110); break;
            case S.ObjectTeleportIn teleport when State.Actors.TryGetValue(teleport.ObjectID, out var actor): ActorSound(actor, 10110); break;
            case S.Death: _ = PlaySound(State.Me?.Gender == MirGender.Female ? 10145 : 10144); break;
            case S.MapChanged: _ = PlaySound(10110); break;
        }
    }

    private void ActorSound(Actor actor, int sound)
    {
        if (State.Me is { } me && Functions.MaxDistance(me.Location, actor.Location) <= Globals.DataRange && (!actor.Hidden || actor.Id == me.Id))
            _ = PlaySound(sound);
    }

    private void PlayMeleeSound(Spell spell, MirGender gender, Actor? source = null)
    {
        if (OriginalSoundRules.MeleeSound(spell, gender) is > 0 and var sound) _ = PlaySound(sound);
        if (spell == Spell.DoubleSlash && (source ?? State.Me) is { } actor) World.QueueSecondSlash(actor);
    }
}
