using Client.MirObjects;
using Godot;
using S = ServerPackets;
using System.Text.Json;
using System.Reflection;
using C = ClientPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyAudioOnly()
    {
        stage = "audio resource decoding and real mixer output";
        await Until(() => Client.HandshakeReady);
        var directory = Argument("--audio-assets");
        var decoded = 0;
        if (directory.Length > 0)
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(path => Path.GetExtension(path).ToLowerInvariant() is ".wav" or ".mp3"))
        {
            using AudioStream stream = Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase)
                ? AudioStreamMP3.LoadFromBuffer(File.ReadAllBytes(path)) : AudioStreamWav.LoadFromBuffer(OriginalSoundRules.WaveBytes(File.ReadAllBytes(path)));
            if (stream == null || stream.GetLength() <= 0) throw new Exception($"声音无法解码：{path}");
            decoded++;
            if (decoded % 100 == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        var capture = new AudioEffectCapture { BufferLength = 2 };
        var bus = AudioServer.GetBusIndex("Effects"); AudioServer.AddBusEffect(bus, capture);
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var preferences = (ClientPreferences)typeof(Main).GetField("preferences", flags)!.GetValue(Client)!;
        void Volume(int value) { preferences.Volume = value; typeof(Main).GetMethod("ApplyVolume", flags)!.Invoke(Client, [value]); }
        var players = Client.GetChildren().OfType<AudioStreamPlayer>().Where(player => player.Bus == "Effects").ToArray();
        void StopEffects() { foreach (var player in players) player.Stop(); }
        var volume = preferences.Volume; Volume(100);
        if (await Client.Assets.Sound(10109) is not AudioStreamMP3) throw new Exception("109.wav 缺失时未加载实际的 MP3 背景音乐。");
        var levels = new Dictionary<int, double>();
        foreach (var id in new[] { 41, 61, 133, 143, 153, 163, 611, 785, 1391, 10003, 10050, 10106, 10107, 10111, 10118, 20020, 20081, 20310, 20360, 20430, 20630, 20640, 20650, 20780, 21210 })
        {
            stage = $"audio mixer {id}";
            if (await Client.Assets.Sound(id) == null) throw new Exception($"现有资源未解析到声音 {id}");
            StopEffects();
            await ToSignal(GetTree().CreateTimer(.08), SceneTreeTimer.SignalName.Timeout);
            capture.ClearBuffer();
            await Client.PlaySound(id);
            await ToSignal(GetTree().CreateTimer(.22), SceneTreeTimer.SignalName.Timeout);
            var samples = capture.GetBuffer(capture.GetFramesAvailable());
            var rms = samples.Length == 0 ? 0 : Math.Sqrt(samples.Average(sample => (sample.X * sample.X + sample.Y * sample.Y) / 2d));
            if (rms < .00001 || samples.Any(sample => Math.Abs(sample.X) > 1.001 || Math.Abs(sample.Y) > 1.001))
                throw new Exception($"实际混音静音或削波：{id}, frames={samples.Length}, rms={rms}");
            levels[id] = rms;
        }
        stage = "audio mute, music race, overlapping effects and successful receipts";
        StopEffects(); Volume(0);
        await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
        capture.ClearBuffer();
        effectsPlayed.Clear(); await Client.PlaySound(10107);
        await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
        if (effectsPlayed.Count != 0 || capture.GetBuffer(capture.GetFramesAvailable()).Any(sample => sample.LengthSquared() > .0000001))
            throw new Exception("音量为0时仍播放音效。");
        Volume(100);
        effectsPlayed.Clear();
        var effect = Client.PlaySound(20360); var music = Client.PlaySound(10147, true);
        await Task.WhenAll(effect, music);
        if (!effectsPlayed.Contains(20360)) throw new Exception("切换背景音乐吞掉普通音效。");
        await Task.WhenAll(new[] { 20310, 20630, 20020, 10106, 10111, 10107, 20640, 20430 }.Select(id => Client.PlaySound(id)));
        if (players.Count(player => player.Playing) < 7) throw new Exception("战斗/道具并发音效仍被六路播放器截断。");
        await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
        if (capture.GetBuffer(capture.GetFramesAvailable()).Any(sample => Math.Abs(sample.X) > 1.001 || Math.Abs(sample.Y) > 1.001))
            throw new Exception("并发音效超过混音限幅。");
        var previous = State.User;
        var potion = new UserItem(new ItemInfo { Type = ItemType.Potion }) { UniqueID = 9001 };
        State.User = new S.UserInformation { Inventory = [potion], Equipment = [], QuestInventory = [] };
        foreach (var packet in new Packet[] { new S.UseItem { UniqueID = potion.UniqueID, Success = false }, new S.DropItem { UniqueID = potion.UniqueID, Success = false }, new S.EquipItem { UniqueID = potion.UniqueID, Success = false } })
        {
            effectsPlayed.Clear(); Client.AudioItemPacket(packet);
            if (effectsPlayed.Count != 0) throw new Exception("失败回执错误播放成功声音。");
        }
        await ToSignal(GetTree().CreateTimer(.05), SceneTreeTimer.SignalName.Timeout);
        effectsPlayed.Clear(); Client.AudioItemPacket(new S.UseItem { UniqueID = potion.UniqueID, Success = true });
        await Until(() => effectsPlayed.Contains(10107));
        State.User = previous;
        Volume(volume);
        AudioServer.RemoveBusEffect(bus, AudioServer.GetBusEffectCount(bus) - 1); capture.Dispose();
        GD.Print($"PASS native audio: {decoded} files decoded; {levels.Count} real nonzero mixer outputs; mute/restore, independent music/effects, 8 simultaneous voices, limiter and success-only item receipts. RMS={JsonSerializer.Serialize(levels)}");
        if (OS.GetCmdlineUserArgs().Contains("--audio-live")) await VerifyAudioReceipts();
        GetTree().Quit();
    }

    private async Task VerifyAudioReceipts()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-audio.", StringComparison.Ordinal))
            throw new InvalidOperationException("真实音效回执检查必须使用隔离版本。");
        stage = "actual TCP equipment, consumable, drop, pickup and magic audio";
        Client.Login("NativeWizard", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single().Index });
        await Until(() => State.User != null && !Client.World.Loading && State.Actors.Values.Any(actor => actor.Kind == ObjectType.Monster));
        var potion = State.User!.Inventory.First(item => item?.Info.Type == ItemType.Potion);
        effectsPlayed.Clear(); Client.UseItem(potion.UniqueID);
        if (!(await Packet<S.UseItem>()).Success) throw new Exception("音效检查道具使用失败。");
        await Until(() => effectsPlayed.Contains(10107));
        var weapon = State.User!.Equipment[0];
        var free = Array.FindIndex(State.User.Inventory, item => item == null);
        effectsPlayed.Clear(); Client.Send(new C.RemoveItem { Grid = MirGridType.Inventory, UniqueID = weapon.UniqueID, To = free });
        if (!(await Packet<S.RemoveItem>()).Success) throw new Exception("音效检查卸装失败。");
        await Until(() => effectsPlayed.Contains(10111));
        await ToSignal(GetTree().CreateTimer(.08), SceneTreeTimer.SignalName.Timeout);
        effectsPlayed.Clear(); Client.Send(new C.EquipItem { Grid = MirGridType.Inventory, UniqueID = weapon.UniqueID, To = 0 });
        if (!(await Packet<S.EquipItem>()).Success) throw new Exception("音效检查穿戴失败。");
        await Until(() => effectsPlayed.Contains(10111));
        var amulet = State.User.Inventory.First(item => item?.Info.Type == ItemType.Amulet && item.Info.Shape == 0);
        var before = State.User.Inventory.Where(item => item?.ItemIndex == amulet.ItemIndex).Sum(item => item.Count);
        effectsPlayed.Clear(); Client.Send(new C.DropItem { UniqueID = amulet.UniqueID, Count = 1 });
        if (!(await Packet<S.DropItem>()).Success) throw new Exception("音效检查丢弃失败。");
        await Until(() => effectsPlayed.Contains(10118));
        await Until(() => State.Actors.Values.Any(actor => actor.Kind == ObjectType.Item));
        var loot = State.Actors.Values.First(actor => actor.Kind == ObjectType.Item);
        stage = "actual TCP pickup audio at original drop location";
        Client.SetDestination(loot.Location, true);
        await Until(() => State.User.Location == loot.Location);
        await ToSignal(GetTree().CreateTimer(.08), SceneTreeTimer.SignalName.Timeout);
        effectsPlayed.Clear(); Client.Send(new C.PickUp());
        await Packet<S.GainedItem>(); await Until(() => effectsPlayed.Contains(10118));
        if (State.User.Inventory.Where(item => item?.ItemIndex == amulet.ItemIndex).Sum(item => item.Count) != before)
            throw new Exception("声音检查拾取后数量不一致。");
        var target = State.Actors.Values.First(actor => actor.Kind == ObjectType.Monster && !actor.Dead);
        await Client.Assets.WarmSounds(new[] { 20310, 20311, 20312 });
        stage = "actual TCP FireBall cast launch and impact audio";
        effectsPlayed.Clear(); Client.World.Selected = target.Id;
        Client.Cast(State.User.Magics.FindIndex(magic => magic.Spell == Spell.FireBall));
        if (!(await Packet<S.Magic>()).Cast) throw new Exception("音效检查火球被拒绝。");
        await Until(() => new[] { 20310, 20311, 20312 }.All(effectsPlayed.Contains));
        // 按原协议包覆盖远程、多段、受击和死亡路由，不修改服务器怪物。
        await Client.Assets.WarmSounds(new[] { 611, 616, 612, 613, 1391 });
        var sample = new Actor { Id = uint.MaxValue, Kind = ObjectType.Monster, Image = 61, Location = State.User.Location };
        State.Actors[sample.Id] = sample;
        foreach (var entry in new (Packet Packet, int Sound)[]
        {
            (new S.ObjectAttack { ObjectID = sample.Id }, 611),
            (new S.ObjectAttack { ObjectID = sample.Id, Type = 1 }, 616),
            (new S.ObjectRangeAttack { ObjectID = sample.Id }, 611),
            (new S.ObjectStruck { ObjectID = sample.Id }, 612),
            (new S.ObjectDied { ObjectID = sample.Id }, 613)
        })
        {
            await ToSignal(GetTree().CreateTimer(.06), SceneTreeTimer.SignalName.Timeout);
            effectsPlayed.Clear(); Client.AudioPacket(entry.Packet); await Until(() => effectsPlayed.Contains(entry.Sound));
        }
        State.Actors.Remove(sample.Id);
        GD.Print("PASS native audio receipts: actual TCP unequip/equip/use/drop/pickup, preserved item count, FireBall cast/launch/impact; monster melee/second/ranged/struck/death packet routes.");
        await Client.Quit();
    }
}
