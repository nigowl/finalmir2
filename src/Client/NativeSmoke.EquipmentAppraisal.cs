using Godot;
using System.Reflection;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyEquipmentAppraisal()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-heroes.appraisal", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("装备鉴定实测仅允许隔离版本。");
        stage = "appraisal original TCP login";
        await Until(() => Client.HandshakeReady); Client.Login("TradeRight", "Trade123");
        Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
        await Until(() => State.User != null && State.Hero != null && !Client.World.Loading);
        string Snapshot() => JsonSerializer.Serialize(new { State.User!.Gold,
            Items = State.User.Inventory.Where(item => item != null).OrderBy(item => item.UniqueID).Select(item => new { item.UniqueID, item.ItemIndex, item.Count, item.EquipmentQuality, item.Identified, Stats = item.AddedStats.Values }),
            Equipment = State.User.Equipment.Select(item => item?.UniqueID ?? 0) });
        if (OS.GetCmdlineUserArgs().Contains("--verify-appraisal-saved"))
        {
            if (Snapshot() != File.ReadAllText(Argument("--appraisal-record"))) throw new Exception("鉴定金币、品质、旧强化或唯一编号没有完整保存恢复。");
            GD.Print("PASS native appraisal restart: exact grades, added stats, original item IDs/counts and gold retained.");
        }
        else
        {
            await VerifyUnidentifiedDrop(); await VerifyAppraisalNpc(); VerifyAppraisalUnknownLock();
            File.WriteAllText(Argument("--appraisal-record"), Snapshot());
        }
        await VerifyQualityStatColours();
        var portrait = (HeroPortraitButton)Client.FindChild("HeroShortcut", true, false);
        await Until(() => portrait.FrameSource != null && portrait.PortraitReady);
        if (((Button)Client.FindChild("SummonHero", true, false)).GetThemeStylebox("normal") is not StyleBoxTexture)
            throw new Exception("英雄按钮没有复用原棕金素材。");
        ((Control)Client.FindChild("NpcDialog", true, false)).Hide();
        GameArt("关闭背包").GetParent<Control>().Show();
        var rare = State.User!.Inventory.First(item => item?.EquipmentQuality == EquipmentGrade.Supreme);
        var cell = await VisibleBagCell(Array.IndexOf(State.User.Inventory, rare));
        await MoveMouse(cell.GetGlobalRect().GetCenter()); await Screenshot("appraisal-quality-and-hero");
        stage = "normal original logout after appraisal combat";
        var deadline = Environment.TickCount64 + 30000;
        while (true)
        {
            packets.RemoveAll(packet => packet is S.LogOutSuccess or S.LogOutFailed);
            Client.Send(new C.LogOut()); await Until(() => packets.Any(packet => packet is S.LogOutSuccess or S.LogOutFailed));
            if (packets.Any(packet => packet is S.LogOutSuccess)) break;
            if (Environment.TickCount64 >= deadline) throw new TimeoutException("原战斗退出保护未在预算内结束。");
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        }
        await Client.Quit();
    }

    private async Task VerifyUnidentifiedDrop()
    {
        stage = "original unidentified equipment wear and monster kill/drop/pickup";
        var first = State.User!.Inventory.First(item => EquipmentAppraisal.Pending(item)); var id = first.UniqueID;
        Client.UseItem(id); if (!(await Packet<S.EquipItem>()).Success) throw new Exception("未鉴定装备不能正常穿戴。");
        await Until(() => State.User.Equipment[0]?.UniqueID == id);
        if (State.User.Equipment[0].Identified || State.User.Equipment[0].GetTotal(Stat.MaxDC) != 5) throw new Exception("穿戴自动鉴定或提前加属性。");
        var to = Array.FindIndex(State.User.Inventory, 6, item => item == null);
        Client.Send(new C.RemoveItem { Grid = MirGridType.Inventory, UniqueID = id, To = to });
        if (!(await Packet<S.RemoveItem>()).Success) throw new Exception("未鉴定装备无法卸回共用背包。");
        await Until(() => State.User.Equipment[0] == null);
        var originalIds = State.User.Inventory.Where(item => item != null).Select(item => item.UniqueID).ToHashSet();
        await Until(() => State.Actors.Values.Any(actor => actor.Name == "隔离鉴定兽" && !actor.Dead));
        var monster = State.Actors.Values.Single(actor => actor.Name == "隔离鉴定兽" && !actor.Dead);
        Client.SelectTarget(monster.Id); await Until(() => monster.Dead);
        await Until(() => State.Actors.Values.Any(actor => actor.Kind == ObjectType.Item && actor.Name == "鉴定试炼剑"));
        var ground = State.Actors.Values.First(actor => actor.Kind == ObjectType.Item && actor.Name == "鉴定试炼剑");
        Client.SelectTarget(ground.Id);
        await Until(() => State.User.Inventory.Any(item => item != null && !originalIds.Contains(item.UniqueID)));
        var gained = State.User.Inventory.Single(item => item != null && !originalIds.Contains(item.UniqueID));
        if (!EquipmentAppraisal.Pending(gained) || gained.AddedStats.Count != 0 || gained.GetTotal(Stat.MaxDC) != 5)
            throw new Exception("原怪物实际掉落/拾取不是未鉴定普通属性。");
        GD.Print("PASS native original TCP: unidentified gear equips without revelation; real monster killed, new gear dropped/picked up with fixed hidden grade and base stats.");
    }

    private async Task VerifyAppraisalNpc()
    {
        stage = "two-line appraiser, licensed selection/cancel/confirmation and five grades";
        var npc = State.Actors.Values.Single(actor => actor.Name == "玄鉴先生");
        if (npc.NpcRole != "鉴定大师" || State.Actors.Values.Where(actor => actor.Kind == ObjectType.Merchant).Any(actor => actor.Name.Contains('\n') || string.IsNullOrWhiteSpace(actor.NpcRole)))
            throw new Exception("NPC姓名与第二行分类没有分开。");
        var before = State.User!.Gold; var first = State.User.Inventory.First(item => EquipmentAppraisal.Pending(item));
        Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = $"[@APPRAISECONFIRM_{first.UniqueID}]" });
        var barrier = Environment.TickCount64; Client.Send(new C.KeepAlive { Time = barrier });
        await Until(() => packets.OfType<S.KeepAlive>().Any(reply => reply.Time == barrier));
        if (State.User.Gold != before || first.Identified) throw new Exception("伪造NPC鉴定链接改变了资产。");
        Client.SelectTarget(npc.Id);
        await ClickAppraisalLink("鉴定装备"); await ClickAppraisalLink("鉴定试炼剑"); await Screenshot("appraisal-confirmation");
        await ClickAppraisalLink("取消");
        if (State.User.Gold != before || first.Identified) throw new Exception("取消鉴定改变了金币或装备。");
        var pending = State.User.Inventory.Where(item => EquipmentAppraisal.Pending(item)).Select(item => item.UniqueID).ToArray();
        var seen = new HashSet<EquipmentGrade>();
        foreach (var id in pending)
        {
            await ClickAppraisalLink("鉴定试炼剑"); packets.RemoveAll(packet => packet is S.EquipmentAppraisalResult);
            var gold = State.User.Gold; await ClickAppraisalLink("确认鉴定"); var result = await Packet<S.EquipmentAppraisalResult>();
            await Until(() => State.User.Inventory.Any(item => item?.UniqueID == result.UniqueID && item.Identified));
            var item = State.User.Inventory.Single(item => item?.UniqueID == result.UniqueID);
            if (!result.Success || !pending.Contains(result.UniqueID) || State.User.Gold != gold - EquipmentAppraisal.Price(item) ||
                item.GetTotal(Stat.MinDC) != 3 + EquipmentAppraisal.Bonuses(item)[Stat.MinDC] ||
                item.GetTotal(Stat.MaxDC) != 5 + EquipmentAppraisal.Bonuses(item)[Stat.MaxDC]) throw new Exception("鉴定结果、真实扣费或属性加成不一致。");
            seen.Add(item.EquipmentQuality);
            Client.Send(new C.CallNPC { ObjectID = npc.Id, Key = $"[@APPRAISECONFIRM_{result.UniqueID}]" });
            barrier = Environment.TickCount64; Client.Send(new C.KeepAlive { Time = barrier });
            await Until(() => packets.OfType<S.KeepAlive>().Any(reply => reply.Time == barrier));
            if (State.User.Gold != gold - EquipmentAppraisal.Price(item)) throw new Exception("重放确认重复扣费。");
        }
        if (seen.Count != 5 || State.User.Inventory.Single(item => item?.EquipmentQuality == EquipmentGrade.Legacy).AddedStats[Stat.MaxDC] != 7)
            throw new Exception("未覆盖五档品质或改变了旧装备强化。");
        GD.Print("PASS native appraiser: two-line NPC, forged link refused, real click selection/cancel/confirmation, five grades and stat/colour metadata, exact fees, replay refusal and legacy stats.");
    }

    private async Task VerifyQualityStatColours()
    {
        stage = "quality bonus text and actual tooltip colour pixels";
        ((Control)Client.FindChild("NpcDialog", true, false)).Hide();
        await MoveMouse(new Vector2(700, 420));
        foreach (var grade in new[] { EquipmentGrade.Normal, EquipmentGrade.Fine, EquipmentGrade.Superior, EquipmentGrade.Excellent, EquipmentGrade.Supreme })
        {
            var item = State.User!.Inventory.First(item => item?.EquipmentQuality == grade);
            var panel = new PanelContainer { Position = new Vector2(650, 150), Theme = Client.Theme, MouseFilter = Control.MouseFilterEnum.Ignore };
            panel.AddThemeStyleboxOverride("panel", Client.GetThemeStylebox("panel", "GameItemTooltip"));
            panel.AddChild(Client.BuildItemTooltip(item)); Client.AddChild(panel);
            try
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var label = (RichTextLabel)panel.FindChild("ItemDetails", true, false);
                var expected = ItemPresentation.Stats(item).First();
                if (label.GetParsedText().Split('\n')[3] != expected || grade == EquipmentGrade.Supreme && expected != "攻击：3(+1) - 5(+2)")
                    throw new Exception("真实装备详情没有分别显示基础值与品质加成。");
                using var screen = GetViewport().GetTexture().GetImage(); var scale = screen.GetWidth() / 1024f;
                var source = EquipmentAppraisal.Colour(item); var quality = new Color(source.R / 255f, source.G / 255f, source.B / 255f);
                var normal = Colors.White; var qualityPixels = 0; var normalPixels = 0;
                for (var y = (int)((label.GlobalPosition.Y + label.GetLineOffset(3)) * scale); y < (label.GlobalPosition.Y + label.GetLineOffset(4)) * scale; y++)
                for (var x = (int)(label.GlobalPosition.X * scale); x < (label.GlobalPosition.X + label.Size.X) * scale; x++)
                {
                    var pixel = screen.GetPixel(x, y);
                    if (new Vector3(pixel.R - quality.R, pixel.G - quality.G, pixel.B - quality.B).Length() < .08f) qualityPixels++;
                    if (new Vector3(pixel.R - normal.R, pixel.G - normal.G, pixel.B - normal.B).Length() < .08f) normalPixels++;
                }
                if (normalPixels == 0 || grade > EquipmentGrade.Normal && qualityPixels == 0) throw new Exception($"属性基础色或{grade}括号品质色未实际绘制。");
                await Screenshot("appraisal-bonus-" + grade);
            }
            finally { panel.QueueFree(); }
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        GD.Print("PASS native tooltip: base attack 3 - 5, Supreme 3(+1) - 5(+2), four real grade-colour bonus pixel samples and base-text colour, repeated after complete restart.");
    }

    private async Task ClickAppraisalLink(string prefix)
    {
        LinkButton? link = null; var content = Client.FindChild("NpcText", true, false);
        await Until(() => (link = content.FindChildren("*", "LinkButton", true, false).OfType<LinkButton>().FirstOrDefault(button => button.Text.StartsWith(prefix, StringComparison.Ordinal))) != null);
        var scroll = (ScrollContainer)Client.FindChild("NpcScroll", true, false);
        scroll.EnsureControlVisible(link!); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ClickSelectionControl(link!);
    }

    private void VerifyAppraisalUnknownLock()
    {
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        FieldInfo Field(string name) => typeof(Main).GetField(name, flags)!;
        void Call(string name, params object[] args) => typeof(Main).GetMethod(name, flags)!.Invoke(Client, args);
        var request = new C.CallNPC { Key = "[@APPRAISECONFIRM_1]" };
        Field("appraisalPending").SetValue(Client, request); Field("appraisalItem").SetValue(Client, 1UL);
        Field("appraisalSent").SetValue(Client, Environment.TickCount64 - 6000);
        try
        {
            Call("ProcessAppraisal"); Call("AppraisalPacket", new S.MapChanged()); Call("AppraisalPacket", new S.Death());
            Call("AppraisalPacket", new S.EquipmentAppraisalResult { UniqueID = 2 });
            if (Field("appraisalPending").GetValue(Client) != request || !(bool)Field("appraisalUnknown").GetValue(Client)!) throw new Exception("超时、切图、死亡或其他编号回执解除了鉴定锁。");
            Call("AppraisalPacket", new S.EquipmentAppraisalResult { UniqueID = 1, Success = false, Message = "隔离拒绝样本" });
            if (Field("appraisalPending").GetValue(Client) != null) throw new Exception("明确鉴定拒绝未解除对应锁。");
        }
        finally { Field("appraisalPending").SetValue(Client, null); Field("appraisalUnknown").SetValue(Client, false); }
        GD.Print("PASS native appraisal lock: timeout/map/death/unrelated replies preserve request; only matching result clears it, no retry.");
    }
}
