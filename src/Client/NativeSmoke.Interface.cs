using Client.MirObjects;
using Godot;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyHud()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var vitals = (Label)Client.FindChild("PlayerVitals", true, false);
        if (!vitals.Text.Contains($"{State.User!.HP}/") || !vitals.Text.Contains($"{State.User.MP}/") || !vitals.Text.Contains('\n'))
            throw new Exception("Player health/mana labels omitted their current values.");
        var dialogs = Client.FindChildren("*", "AcceptDialog", true, false).OfType<AcceptDialog>().ToArray();
        if (dialogs.Length < 5 || dialogs.Any(dialog => dialog.Name == "DeathConfirmation"
            ? dialog.GetOkButton().Text != "回城复活" || ((ConfirmationDialog)dialog).GetCancelButton().Text != "暂不复活"
            : dialog.GetOkButton().Text != "确定" || dialog is ConfirmationDialog confirm && confirm.GetCancelButton().Text != "取消"))
            throw new Exception("系统确认弹窗仍含英文按钮。");
        if (vitals.GetRect().End.X > 104 || vitals.GetRect().Position.Y < 646 || vitals.GetRect().End.Y > 726)
            throw new Exception("HP/MP labels escaped the original orb.");
        var header = (Label)Client.FindChild("WorldStatus", true, false);
        var tracker = (RichTextLabel)Client.FindChild("QuestTracker", true, false);
        if (tracker.Position != new Vector2(8, 34) || tracker.MouseFilter != Control.MouseFilterEnum.Ignore || tracker.GetRect().Position.Y <= header.GetRect().End.Y)
            throw new Exception("Quest tracker did not move to the upper-left below the status line.");
        if (header.Text.Contains('\n') || !header.Text.Contains($"[{State.Map!.FileName} #{State.Map.MapIndex}]") || !header.Text.Contains($"{State.User!.Location.X},{State.User.Location.Y}"))
            throw new Exception("The one-line header omitted the map code, coordinates or cache status.");
        var chat = Client.FindChildren("*", "LineEdit", true, false).Cast<LineEdit>().Single(edit => edit.MaxLength == Globals.MaxChatLength);
        var weight = (Label)Client.FindChild("HudWeight", true, false); var free = (Label)Client.FindChild("HudFree", true, false);
        if (weight.Text != (State.CalculateStats()[Stat.BagWeight] - State.InventoryWeight).ToString("0.##") || free.Text != State.User.Inventory.Count(item => item == null).ToString() ||
            weight.GetRect().End.X > 993 || free.GetRect().End.X > 1020 || weight.Position.Y != 717 || free.Position.Y != 717)
            throw new Exception("Lower-right HUD omitted original remaining weight/free cells or escaped their fields.");
        if (chat.GetRect().End.X > 858 || chat.GetRect().End.Y > 735 || chat.Position.X != 231 || chat.Size.Y > 19)
            throw new Exception("Chat input overlaps the gold counter or experience bar.");
        await Until(() => Client.World.PrefetchedCells > 0);
        if (Client.World.PrefetchQueueCount > 1260) throw new Exception("Terrain prefetch grew beyond the bounded six-cell ring.");
        var monster = State.Actors.Values.First(actor => actor.Kind == ObjectType.Monster && !actor.Dead);
        var hover = Client.World.Screen(monster.Location) + new Vector2(24, -25);
        Client.World.UpdateHover(hover, false);
        if (Client.World.Hovered != monster.Id || !Client.World.NameVisible(monster)) throw new Exception("Hovering the monster did not select its name for rendering.");
        Client.World.UpdateHover(hover, true);
        if (Client.World.Hovered != 0) throw new Exception("A dialog hover leaked through into the world.");
        var npc = State.Actors.Values.First(actor => actor.Kind == ObjectType.Merchant);
        Client.World.ShowNpcNames = false; Client.World.UpdateHover(Client.World.Screen(npc.Location) + new Vector2(24, -25), false);
        if (!Client.World.NameVisible(npc)) throw new Exception("Disabled NPC name display also hid its hover name.");
        Client.World.UpdateHover(Vector2.Zero, true); Client.World.ShowNpcNames = true;
        await VerifyActorNameCentres(npc, monster);
        var belt = Client.FindChildren("*", "Button", true, false).OfType<OriginalItemButton>().Where(button => button.SlotAssets != null).ToArray();
        if (belt.Length != 6 || belt.Any(button => button.Size != new Vector2(32, 32)) || belt.Select(button => button.GetRect()).Any(rect => rect.Position.Y != 629 || rect.End.Y != 661 || rect.End.X > 454))
            throw new Exception($"Quick item hitboxes do not match the original belt: {string.Join(", ", belt.Select(button => button.GetRect()))}.");
        var quickBars = (Control)Client.FindChild("QuickBars", true, false);
        var squares = quickBars.GetChildren().OfType<Button>().OrderBy(button => button.Position.X).ToArray();
        if (squares.Length != 18 || squares.Any(button => button.Size != new Vector2(32, 32) || button.Position.Y != 629) ||
            squares.SelectMany(button => button.GetChildren().OfType<Label>()).Any(label => label.Position != new Vector2(1, 0) || label.Size.Y != 14 || label.GetThemeFontSize("font_size") != 9))
            throw new Exception("物品/技能快捷格或数字未使用相同尺寸、基线和字体：" + string.Join("；", squares.Select(button =>
                $"{button.Name} {button.GetRect()} " + string.Join(",", button.GetChildren().OfType<Label>().Select(label => $"{label.Text}/{label.GetRect()}/{label.GetThemeFontSize("font_size")}")))));
        for (var index = 1; index < squares.Length; index++)
            if (squares[index].Position.X - squares[index - 1].GetRect().End.X != (index == 6 ? 8 : 2))
                throw new Exception("快捷格间距未按整数像素对齐。");
        for (var number = 1; number <= 6; number++)
            if (!belt.Any(button => button.Position == OriginalArt.QuickSlotPosition(number - 1) && button.SlotAssets != null &&
                button.GetChildren().OfType<Label>().Any(label => label.Text == number.ToString() && label.Position == new Vector2(1, 0))))
                throw new Exception("物品快捷栏数字未对齐原版格子。");
        if (quickBars.GetChildren().OfType<TextureRect>().Any()) throw new Exception("快捷栏仍保留无效拖条或旋转/关闭装饰。");
        await Until(() => Client.Assets.Sprite("Prguse", 1933) != null);
        using var equipment = new InputEventKey { PhysicalKeycode = Key.C, Pressed = true };
        Client._UnhandledInput(equipment);
        await Screenshot("presentation-equipment");
        Client._UnhandledInput(equipment);
        await VerifyEquipmentTabs();
        await VerifyMapFrameFill();
        if (Argument("--screenshot").Length > 0)
        {
            var route = Enumerable.Range(0, 8).Select(direction => Functions.PointMove(State.User!.Location, (MirDirection)direction, 60))
                .Where(Client.World.Walkable).Select(Client.World.PathTo).First(path => path.Length > 25);
            Client.MiniMap.Route = route;
            await Screenshot("route-minimap"); Client.MiniMap.SetExpanded(true);
            await Screenshot("route-large"); Client.MiniMap.SetExpanded(false); Client.MiniMap.Route = [];
        }
        GD.Print("PASS native HUD: HP/MP, chat bounds, six belt cells and single-line map/cache/coordinate header.");
        GD.Print($"PASS native terrain/hover: {Client.World.PrefetchedCells} prepared offscreen cells, textures={Client.Assets.TextureCount}, evictions={Client.Assets.TextureEvictions}, bounded queue, NPC/monster hover names and UI occlusion.");
    }

    private async Task VerifyMapAndMovement(Point start)
    {
        stage = "expanded map and movement cadence";
        await VerifyMapTeleportDenied(start);
        var map = Client.MiniMap;
        var revision = map.Revision;
        if (map.Position.Y != 8 || map.GetRect().End.X != 1016 || map.ImageRect.End.Y > map.Size.Y - 18 || map.GetNode<Label>("MapCaption").Text != State.Map!.Title ||
            map.GetNode<Label>("MapCaption").Position.X < 24 || map.GetNode<Label>("MapCaption").GetRect().End.X > map.Size.X - 24)
            throw new Exception("The small map lost its 8px top/right inset or caption strip.");
        using (var mouse = new InputEventMouseButton { Position = map.ImageRect.GetCenter(), ButtonIndex = MouseButton.Left, Pressed = true }) map._GuiInput(mouse);
        if (!map.Expanded) throw new Exception("Clicking the small map did not expand it.");
        using (var mouse = new InputEventMouseButton { Position = Vector2.One, ButtonIndex = MouseButton.Left, Pressed = true }) map._GuiInput(mouse);
        if (map.Expanded) throw new Exception("Clicking the large map's letterbox did not collapse it.");
        using var key = new InputEventKey { PhysicalKeycode = Key.M, Pressed = true };
        Client._UnhandledInput(key);
        if (!map.Expanded || !map.GetRect().HasPoint(new Vector2(512, 308))) throw new Exception("M did not open the centered map.");
        var rect = map.ImageRect;
        var width = Client.World.Map!.Width; var height = Client.World.Map.Height;
        if (Math.Abs(rect.Size.X / rect.Size.Y - width * 48f / (height * 32)) > .01f || map.CellAt(Vector2.Zero) != null)
            throw new Exception("Expanded map lost the 48x32 ratio or accepted a letterbox click.");
        if (map.CellAt(rect.Position + Vector2.One * .01f) != Point.Empty || map.CellAt(rect.End - Vector2.One * .01f) != new Point(width - 1, height - 1))
            throw new Exception("Map edge clicks selected the wrong tile.");
        await Screenshot("large-map");
        Client._UnhandledInput(key); Client._UnhandledInput(key);
        if (!map.Expanded || map.Revision != revision) throw new Exception("Map toggling reset the shared thumbnail cache.");
        var target = new Point(start.X, start.Y - 3);
        var click = rect.Position + new Vector2((target.X + .5f) / width, (target.Y + .5f) / height) * rect.Size;
        if (map.CellAt(click) != target) throw new Exception("Map click does not round-trip the destination coordinate.");
        var accepted = new List<(Point point, long time)>(); var previous = State.User!.Location;
        void Observe(Packet packet)
        {
            if (packet is not S.UserLocation location || location.Location == previous) return;
            previous = location.Location; accepted.Add((previous, Environment.TickCount64));
        }
        Client.PacketReceived += Observe;
        using (var mouse = new InputEventMouseButton { Position = click, ButtonIndex = MouseButton.Left, Pressed = true }) map._GuiInput(mouse);
        if (map.Expanded) throw new Exception("Map selection failed to start navigation.");
        if (map.Route.Count < 2 || map.Route[0] != start || map.Route[^1] != target)
            throw new Exception("The small-map route differs from the selected navigation path.");
        var interpolated = false; var lastFrame = Environment.TickCount64; long maxFrame = 0;
        try
        {
            await Until(() =>
            {
                var now = Environment.TickCount64; maxFrame = Math.Max(maxFrame, now - lastFrame); lastFrame = now;
                var point = Client.World.Visual(State.Me!);
                var tile = Client.World.Screen(start);
                if (tile != tile.Round() || Client.World.Screen(new Point(start.X + 1, start.Y)) - tile != new Vector2(48, 0) || Client.World.Screen(new Point(start.X, start.Y + 1)) - tile != new Vector2(0, 32))
                    throw new Exception("Moving camera split adjacent tiles across inconsistent pixel positions.");
                for (var sample = 0; sample < 1000; sample++) if (Client.World.Screen(start) != tile) throw new Exception("The camera changed inside one render frame.");
                interpolated |= Math.Abs(point.Y - MathF.Round(point.Y)) > .05f;
                return State.User.Location == target;
            });
        }
        finally { Client.PacketReceived -= Observe; }
        if (!interpolated || accepted.Count != 2 || accepted[0].point != new Point(start.X, start.Y - 1))
            throw new Exception("Map navigation did not interpolate a walking step followed by running.");
        var interval = accepted[1].time - accepted[0].time;
        if (interval < 550 || interval > 900) throw new Exception($"Walk/run cadence has an idle gap: {interval}ms.");
        await Until(() => Environment.TickCount64 - State.Me!.MotionTime >= Actor.MoveDuration);
        if (!effectsPlayed.Any(id => id is >= 10001 and <= 10039)) throw new Exception("Walking/running never played an original footstep stream.");
        if (Client.World.Visual(State.Me!) != new Vector2(target.X, target.Y)) throw new Exception("Navigation stopped before the visual destination.");
        GD.Print($"PASS native map/navigation: M toggle, shared revision, letterbox/edge/cell conversion, walk/run interval={interval}ms, max sampled frame={maxFrame}ms.");
        await VerifyRingMapTeleport();
        await VerifyFullAttack();
    }

    private async Task VerifyFullAttack()
    {
        stage = "full six-frame attack and location acknowledgement";
        var me = State.Me!; var before = me.ActionTime;
        Client.Attack(MirDirection.Up);
        await Until(() => me.Action == MirAction.Attack1 && me.ActionTime != before);
        var frame = FrameSet.Player[MirAction.Attack1]; var seen = new HashSet<int>();
        await Until(() =>
        {
            var elapsed = Environment.TickCount64 - me.ActionTime;
            if (me.Action != MirAction.Attack1) throw new Exception("The attack acknowledgement cut the animation short.");
            var index = frame.IndexAt(elapsed); seen.Add(index);
            return elapsed >= frame.Duration;
        });
        if (seen.Count != frame.Count) throw new Exception($"The rendered client skipped an attack frame: {string.Join(",", seen)}.");
        await Until(() => effectsPlayed.Any(id => id is >= 10050 and <= 10056));
        GD.Print("PASS native audio: original cached footstep and weapon-swing streams reach independent effect players.");
        GD.Print("PASS native attack: all six original frames remain active through the server's unchanged location acknowledgement.");
        if (Argument("--screenshot") is { Length: > 0 } screenshot)
        {
            await Until(() => Environment.TickCount64 - me.ActionTime >= 1500);
            var actionTime = me.ActionTime; Client.Attack(MirDirection.Up);
            await Until(() => me.ActionTime != actionTime && Environment.TickCount64 - me.ActionTime >= 250);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Capture(Path.ChangeExtension(screenshot, "attack-mid.png"));
        }
    }

    private async Task VerifySelection()
    {
        stage = "server-authoritative character selection location";
        if (!Client.Assets.SupportsCharacterLocation) throw new Exception("The disposable server did not advertise character-location support.");
        var selected=Client.FindChildren("*","Control",true,false).OfType<OriginalArt>().Single(art=>art.Page=="select").Character!.Index;
        await Until(()=>packets.OfType<S.CharacterLocation>().Any(packet=>packet.CharacterIndex==selected));
        var location = packets.OfType<S.CharacterLocation>().Last(packet=>packet.CharacterIndex==selected);
        packets.Remove(location);
        var expected = location.Found ? $"{location.MapTitle} · {location.Location.X},{location.Location.Y}" : "新角色 · 尚未进入地图";
        var label = Client.FindChildren("*", "Label", true, false).Cast<Label>().Single(label => label.Text == expected);
        if (!new Rect2(198, 613, 176, 19).Encloses(label.GetRect())) throw new Exception("The last map/coordinate label escaped the original gold field.");
        var row = Client.FindChildren("*", "VBoxContainer", true, false).Cast<VBoxContainer>().Single(box => box.Position == new Vector2(637, 194));
        await VerifyCharacterCardArtwork(row);
        if (row.FindChildren("*", "Button", true, false).Cast<Button>().Any(button => button.GetThemeStylebox("hover") is not StyleBoxEmpty)) throw new Exception("Character selection retained the additional rectangular border.");
        await Screenshot("selection-location");
        GD.Print("PASS native selection: optional authenticated location response, selected-character label and no extra row border.");
    }

    private async Task VerifyEquipmentTabs()
    {
        using var key = new InputEventKey { PhysicalKeycode = Key.K, Pressed = true };
        Client._UnhandledInput(key);
        var button = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Single(button => button.TooltipText == "技能 K");
        var tabs = Client.FindChildren("*", "Button", true, false).OfType<OriginalButton>().Where(button => button.Text is "人物" or "属性" or "状态" or "技能").ToArray();
        var panel = tabs[0].GetParent<Control>();
        if (!panel.Visible || tabs.Single(tab => tab.TooltipText == "技能").Index != 503 || tabs.Count(tab => tab.Index >= 500) != 1) throw new Exception("The skill page lacks a single original selected tab.");
        Client._UnhandledInput(key); if (panel.Visible) throw new Exception("Pressing K twice did not close the skill dialog.");
        button.EmitSignal(BaseButton.SignalName.Pressed); if (!panel.Visible) throw new Exception("The skill icon failed to open the dialog.");
        button.EmitSignal(BaseButton.SignalName.Pressed); if (panel.Visible) throw new Exception("Clicking the skill icon twice did not close the dialog.");
        foreach (var tab in tabs)
        {
            tab.EmitSignal(BaseButton.SignalName.Pressed);
            if (tabs.Count(item => item.Index >= 500) != 1 || tab.Index != tab.PressedIndex) throw new Exception("Equipment tab highlighting did not follow the selected page.");
            var columns = new[] { panel.GetNode<Label>("PlayerAttributes"), panel.GetNode<Label>("PlayerAttributesRight") };
            var experience = panel.GetNode<Label>("PlayerExperience");
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (columns.Any(label => label.Visible != (tab.TooltipText is "属性" or "状态")) || experience.Visible != columns[0].Visible)
                throw new Exception("Attribute columns leaked into the equipment/skill pages.");
            if (!columns[0].Visible) continue;
            if (columns.Any(label => label.GetThemeFontSize("font_size") != 11 || label.GetLineCount() > 9 || label.GetRect().End.Y > experience.Position.Y || label.GetRect().End.X > 246) ||
                columns[0].GetRect().Intersects(columns[1].GetRect()) ||
                experience.GetRect().End.Y > panel.Size.Y - 24 || !experience.Text.Contains($"{State.User.Experience}/{State.User.MaxExperience}"))
                throw new Exception("The two attribute columns overlap, overflow or omit vital/stat/experience values.");
            if (tab.TooltipText == "属性")
            {
                if (!columns[0].Text.Contains($"生命 {State.User.HP}/") || !columns[1].Text.Contains($"魔力 {State.User.MP}/") || !columns[0].Text.Contains(Stat.AttackSpeed.ToLocalizedString()))
                    throw new Exception("属性页缺少战斗属性。");
                await Screenshot("attributes-two-columns");
            }
            else
            {
                if (!columns[0].Text.Contains($"背包负重 {State.InventoryWeight:0.##}/") || !columns[1].Text.Contains(Stat.PoisonRecovery.ToLocalizedString()) || columns[0].Text.Contains("最低攻击"))
                    throw new Exception("状态页仍与属性页相同或未显示负重和抗性。");
                await Screenshot("status-two-columns");
            }
        }
        panel.Hide(); GD.Print("PASS native equipment: K/icon toggles, distinct combat/status pages, bounded columns and live full experience.");
    }

    private async Task UseQuickPotion(bool mouse)
    {
        var item = State.User!.Inventory.First(i => i?.Info?.Type == ItemType.Potion && i.Info.Stats[Stat.HP] > 0);
        var slot = Array.FindIndex(State.User.Inventory, i => i?.UniqueID == item.UniqueID);
        if (slot >= 6)
        {
            Client.Send(new C.MoveItem { Grid = MirGridType.Inventory, From = slot, To = 0 });
            if (!(await Packet<S.MoveItem>()).Success) throw new Exception("Could not move a potion into the quick belt.");
            slot = 0;
        }
        var before = State.User.Inventory.Where(i => i?.Info?.Type == ItemType.Potion).Sum(i => i.Count);
        if (mouse)
        {
            Client.SetDestination(State.User.Location);
            await Until(() => Environment.TickCount64 - State.Me!.MotionTime >= Actor.MoveDuration);
            var point = ((Control)Client.FindChild($"ItemShortcut{slot + 1}", true, false)).GetGlobalRect().GetCenter();
            using var motion = new InputEventMouseMotion { Position = point, GlobalPosition = point };
            GetViewport().PushInput(motion, true);
            foreach (var pressed in new[] { true, false })
            {
                using var input = new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = pressed };
                GetViewport().PushInput(input, true);
            }
        }
        else
        {
            using var input = new InputEventKey { PhysicalKeycode = (Key)((int)Key.Key1 + slot), Pressed = true };
            Client._UnhandledInput(input);
        }
        if (!(await Packet<S.UseItem>()).Success || State.User.Inventory.Where(i => i?.Info?.Type == ItemType.Potion).Sum(i => i.Count) != before - 1)
            throw new Exception("Quick potion use did not consume exactly one item after the server acknowledgement.");
        GD.Print($"PASS native quick potion: {(mouse ? "single click" : "number key")} consumes exactly one server-confirmed item.");
    }
}
