using System.Text.Json;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyObjectNames()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-object-name.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("名称增量检查只允许独立版本。");
        Actor? original = null; string? atReceipt = null; bool released = false, ownedPortrait = false;
        void Observe(Packet packet)
        {
            if (packet is S.ObjectMonster monster)
            {
                if (original == null && monster.MasterObjectId == State.User?.ObjectID) original = State.Actors[monster.ObjectID];
                else if (monster.ObjectID == original?.Id && monster.MasterObjectId == 0)
                {
                    if (!ReferenceEquals(original, State.Actors[monster.ObjectID])) throw new Exception("归属刷新替换了已知宠物对象。");
                    released = true;
                }
                // 登录到期包可能同帧到达，逐包刷新并检查真实HUD，不靠帧间巧合。
                typeof(Main).GetMethod("RefreshCompanionPortraits", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(Client, null);
                if (monster.MasterObjectId == State.User?.ObjectID)
                    ownedPortrait = Client.FindChild("PetShortcut", true, false) is Button { Visible: true } &&
                        Client.FindChild("PetPortraitMode", true, false) is OptionButton { Visible: true };
            }
            if (packet is S.ObjectName name && name.ObjectID == original?.Id) atReceipt = State.Actors[name.ObjectID].Name;
        }
        Client.PacketReceived += Observe;
        try
        {
            stage = "object name original pet login and expiry";
            await Until(() => Client.HandshakeReady); Client.Login("NameObserver", "Native123");
            Client.Send(new C.StartGame { CharacterIndex = (await Packet<S.LoginSuccess>()).Characters.Single().Index });
            await Until(() => State.User != null && !Client.World.Loading);
            var user = State.User!;
            string Snapshot() => JsonSerializer.Serialize(new { user.RealId, user.Name, user.Gold,
                Items = user.Inventory.Concat(user.Equipment).Where(item => item != null).Select(item => new { item.UniqueID, item.Count, item.CurrentDura }) });
            var restored = OS.GetCmdlineUserArgs().Contains("--verify-object-names-save");
            if (restored)
            {
                var time = Environment.TickCount64; Client.Send(new C.KeepAlive { Time = time });
                await Until(() => packets.OfType<S.KeepAlive>().Any(packet => packet.Time == time));
                if (original != null || Snapshot() != File.ReadAllText(Argument("--object-name-record")))
                    throw new Exception("完整重启后恢复了已到期宠物，或改变本人身份/金币/物品。");
                GD.Print("PASS native object names saved: original expiry cleanup, identity, gold and item IDs/counts/durability survive complete restart.");
            }
            else
            {
                await Until(() => original != null && atReceipt != null && released);
                var actor = original!; var spawn = packets.OfType<S.ObjectMonster>().First(packet => packet.ObjectID == actor.Id);
                var rename = packets.OfType<S.ObjectName>().First(packet => packet.ObjectID == actor.Id);
                if (spawn.Name != "到期召唤兽(名字同步验证)" || rename.Name != "到期召唤兽" || atReceipt != rename.Name ||
                    !ReferenceEquals(State.Actors[actor.Id], actor) || actor.Name != rename.Name || user.Name != "名字同步验证" ||
                    actor.MasterObjectId != 0 || !ownedPortrait ||
                    Client.FindChild("PetShortcut", true, false) is not Button { Visible: false } ||
                    Client.FindChild("PetPortraitMode", true, false) is not OptionButton { Visible: false })
                    throw new Exception("原宠物到期改名包未立即更新同一场景对象，或误改本人身份。");
                Client.World.Selected = actor.Id; Client.World.ShowMonsterNames = true;
                await VerifyNamePixels(actor, actor.Colour); await Screenshot("expired-pet-name");
                File.WriteAllText(Argument("--object-name-record"), Snapshot());
                GD.Print($"PASS native object names: original TCP {spawn.Name} -> {rename.Name}, same object #{actor.Id}, visible name pixels; authoritative MasterObjectId {spawn.MasterObjectId} -> {actor.MasterObjectId}, pet portrait/mode shown then hidden.");
            }
            Client.Send(new C.LogOut()); await Packet<S.LogOutSuccess>(); await Client.Quit();
        }
        finally { Client.PacketReceived -= Observe; }
    }
}
