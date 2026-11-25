using Godot;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyHeroRecovery()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-heroes.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("英雄恢复检查仅允许隔离版本。");
        stage = "hero recovery login";
        await Until(() => Client.HandshakeReady); Client.Login("TradeLeft", "Trade123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && State.HeroRecoveryKnown && !Client.World.Loading);
        var user = State.User!;
        var action = Argument("--hero-recovery-stage"); var record = Argument("--hero-recovery-record");
        string Assets() => JsonSerializer.Serialize(new { user.RealId, user.Gold, Hero = State.ActiveHero!.Index,
            Items = user.Inventory.Select(i => i == null ? "-" : $"{i.UniqueID}:{i.ItemIndex}:{i.Count}"),
            Equipment = user.Equipment.Select(i => i?.UniqueID ?? 0) });
        var summon = (Button)Client.FindChild("SummonHero", true, false);
        var hpBar = (ProgressBar)Client.FindChild("HeroHP", true, false);
        var mode = (OptionButton)Client.FindChild("HeroPortraitMode", true, false);
        async Task Barrier()
        {
            var time = Environment.TickCount64; Client.Send(new C.KeepAlive { Time = time });
            await Until(() => packets.OfType<S.KeepAlive>().Any(p => p.Time == time));
        }
        async Task Kill()
        {
            stage = "original shared hero lethal potion";
            var window = GameArt("关闭角色").GetParent<Control>();
            if (!window.Visible) await ClickSelectionControl((Control)Client.FindChild("HeroShortcut", true, false));
            await Until(() => window.Visible);
            var potion = user.Inventory.Single(i => i?.Info.Name == "隔离英雄阵亡药");
            packets.RemoveAll(p => p is S.UseItem); Client.UseItem(potion.UniqueID);
            if (!(await Packet<S.UseItem>()).Success) throw new Exception("隔离原UseItem未能作用于英雄。");
            await Until(() => State.HeroState == HeroSpawnState.Dead && State.HeroRecoverySeconds > 0);
            await Until(() => summon.Disabled && hpBar.Value == 0 && mode.Disabled);
            if (State.Hero!.HP != 0 || State.HeroRecoverySeconds < 55 || summon.Text == "休息") throw new Exception("阵亡状态/生命/60秒禁用不同步。");
        }
        async Task RejectEarly(bool scroll)
        {
            packets.RemoveAll(p => p is S.HeroInformation or S.UseItem);
            if (scroll)
            {
                var item = user.Inventory.Single(i => i?.Info.Name == "隔离英雄复活卷"); var count = item.Count;
                Client.UseItem(item.UniqueID);
                if ((await Packet<S.UseItem>()).Success || item.Count != count) throw new Exception("冷却内复活卷成功或被扣除。");
            }
            Client.Send(new C.Chat { Message = "@SUMMONHERO" }); await Barrier();
            if (State.HeroState != HeroSpawnState.Dead || packets.OfType<S.HeroInformation>().Any()) throw new Exception("命令绕过英雄死亡冷却。");
        }
        async Task WaitRecovery()
        {
            stage = "real sixty second hero recovery";
            var deadline = Environment.TickCount64 + 65000;
            while (State.HeroRecoverySeconds > 0)
            {
                if (Environment.TickCount64 > deadline) throw new TimeoutException(stage);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            await Until(() => !summon.Disabled && summon.Text == "召唤");
            if (State.HeroState != HeroSpawnState.Dead) throw new Exception("冷却结束后未经点击自动召唤。");
        }
        if (action == "death")
        {
            await Until(() => State.HeroState == HeroSpawnState.Summoned && State.Hero?.HP > 0 && hpBar.Value > 0);
            if (State.Hero!.HP != State.CalculateHeroStats()[Stat.HP]) throw new Exception("初次召唤未发送已初始化的生命。");
            await Screenshot("hero-initial-health"); await Kill(); await RejectEarly(true);
            File.WriteAllText(record, Assets()); File.WriteAllText(record + ".death", DateTime.UtcNow.ToString("O"));
            await Screenshot("hero-death-countdown");
        }
        else if (action == "recover")
        {
            var elapsed = (DateTime.UtcNow - DateTime.Parse(File.ReadAllText(record + ".death"), null, System.Globalization.DateTimeStyles.RoundtripKind)).TotalSeconds;
            if (Assets() != File.ReadAllText(record) || State.HeroState != HeroSpawnState.Dead || State.HeroRecoverySeconds <= 0 ||
                Math.Abs(State.HeroRecoverySeconds - (60 - elapsed)) > 4) throw new Exception("完整重启/重登改变资产或重置了死亡等待。");
            await RejectEarly(false); await WaitRecovery();
            packets.RemoveAll(p => p is S.HeroInformation);
            int initialHP = -1;
            void CaptureHealth(Packet p) { if (p is S.HeroInformation hero && initialHP < 0) initialHP = hero.HP; }
            Client.PacketReceived += CaptureHealth;
            try
            {
                await ClickSelectionControl(summon);
                await Until(() => State.HeroState == HeroSpawnState.Summoned && State.Hero != null && hpBar.Value > 0 && !mode.Disabled);
            }
            finally { Client.PacketReceived -= CaptureHealth; }
            if (initialHP != Math.Max(1, State.CalculateHeroStats()[Stat.HP] / 10) ||
                State.Actors.Values.Count(a => a.Name == State.Hero!.Name && !a.Dead) != 1 || !State.Actors.ContainsKey(State.Hero!.ObjectID))
                throw new Exception($"复活生命或对象数量错误：{initialHP}/{State.CalculateHeroStats()[Stat.HP]}。");
            await Screenshot("hero-recovered-ten-percent");
            stage = "rest and resummon preserves partial hero HP";
            await Until(() => !summon.Disabled && summon.Text == "休息"); await ClickSelectionControl(summon);
            await Until(() => State.HeroState == HeroSpawnState.Unsummoned && !summon.Disabled && summon.Text == "召唤");
            await ClickSelectionControl(summon); await Until(() => State.HeroState == HeroSpawnState.Summoned);
            if (State.Hero!.HP >= State.CalculateHeroStats()[Stat.HP] || Assets() != File.ReadAllText(record)) throw new Exception("休息重召无条件回满生命或改变资产。");
            await Kill(); await RejectEarly(true); await WaitRecovery();
            var scroll = user.Inventory.Single(i => i?.Info.Name == "隔离英雄复活卷");
            packets.RemoveAll(p => p is S.UseItem); Client.UseItem(scroll.UniqueID);
            if (!(await Packet<S.UseItem>()).Success) throw new Exception("冷却后原复活卷仍被拒绝。");
            await Until(() => State.HeroState == HeroSpawnState.Summoned && State.Hero!.HP == State.CalculateHeroStats()[Stat.HP]);
            if (scroll.Count != 1) throw new Exception("原复活卷未精确消耗一张。");
            File.WriteAllText(record, Assets());
        }
        else if (action == "saved")
        {
            await Until(() => State.HeroState == HeroSpawnState.Summoned && State.Hero?.HP > 0 && hpBar.Value > 0);
            if (Assets() != File.ReadAllText(record)) throw new Exception("原复活卷/共享背包/英雄召唤意愿未跨重启保存。");
        }
        else throw new InvalidOperationException("未知英雄恢复验证阶段。");
        GD.Print($"PASS native hero recovery {action}: original TCP, authoritative HP/state, persistent cooldown and exact shared assets.");
        await Client.Quit();
    }
}
