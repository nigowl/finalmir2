using Godot;
using System.Net.Http.Headers;
using System.Text.Json;
using C = ClientPackets;
using S = ServerPackets;
using Point = System.Drawing.Point;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task ReadGuildStorage(bool open = false)
    {
        Client.OpenGuild(); Client.SelectGuildPage(3); await Until(() => State.GuildStorageLoaded);
        if (!open) { using var close = new InputEventKey { PhysicalKeycode = Key.H, Pressed = true }; Client._UnhandledInput(close); }
    }
    private async Task VerifyGuildStorage(GameConnection bot, GameState other, List<Packet> received, Func<Func<bool>, Task> botUntil)
    {
        stage = "native guild bank initial synchronization";
        Client.SelectGuildPage(3); await Until(() => State.GuildStorageLoaded);
        await bot.Send(new C.GuildStorageItemChange { Type = 3 }); await botUntil(() => other.GuildStorageLoaded);
        // Keep a separate safe tile so the two logged-in peers do not block the return path.
        var start = new Point(other.User!.Location.X, other.User.Location.Y - 1);
        Client.SetDestination(start); await Until(() => State.User!.Location == start);
        var item = State.User.Inventory.First(i => i?.Info?.Type == ItemType.Potion && i.Info.Stats[Stat.HP] >= 0)!;
        var from = Array.IndexOf(State.User.Inventory, item);
        stage = "native guild bank outside safezone rejection";
        Client.SetDestination(new Point(start.X, start.Y - 1)); await Until(() => State.User.Location == new Point(start.X, start.Y - 1));
        packets.RemoveAll(p => p is S.GuildStorageItemChange);
        Client.StoreGuildItem(from, 0);
        if ((await Packet<S.GuildStorageItemChange>()).Type != 3 || State.User.Inventory[from]?.UniqueID != item.UniqueID || State.GuildStorage[0] != null) throw new Exception("Unsafe guild deposit changed assets or lacked its failure acknowledgement.");
        Client.SetDestination(start); await Until(() => State.User.Location == start);
        stage = "native guild bank item offer and permission rejection";
        Client.StoreGuildItem(from, 0); await Until(() => State.GuildStorage[0]?.Item.UniqueID == item.UniqueID && State.User.Inventory[from] == null);
        await botUntil(() => other.GuildStorage[0]?.Item.UniqueID == item.UniqueID);
        var peerSlot = Array.IndexOf(other.User!.Inventory, null);
        received.RemoveAll(p => p is S.GuildStorageItemChange);
        await bot.Send(new C.GuildStorageItemChange { Type = 1, From = 0, To = peerSlot });
        await botUntil(() => received.OfType<S.GuildStorageItemChange>().Any(p => p.Type == 4));
        if (other.GuildStorage[0]?.Item.UniqueID != item.UniqueID || other.User.Inventory[peerSlot] != null) throw new Exception("Member without bank permissions withdrew an item.");
        Client.SetGuildRankPermission(1, 3, true); Client.SetGuildRankPermission(1, 4, true);
        await botUntil(() => other.HasGuildPermission(GuildRankOptions.CanStoreItem) && other.HasGuildPermission(GuildRankOptions.CanRetrieveItem));
        stage = "native guild bank scrolling, swap and two-player retrieval";
        var weapon = other.User.Inventory.First(i => i?.Info?.Type == ItemType.Weapon)!; var weaponSlot = Array.IndexOf(other.User.Inventory, weapon);
        await bot.Send(new C.GuildStorageItemChange { Type = 0, From = weaponSlot, To = 111 });
        await botUntil(() => other.GuildStorage[111]?.Item.UniqueID == weapon.UniqueID);
        await Until(() => State.GuildStorage[111]?.Item.UniqueID == weapon.UniqueID);
        Client.SetGuildStorageRow(6); await Screenshot("guild-bank-last-row");
        Client.MoveGuildItem(0, 111); await Until(() => State.GuildStorage[0]?.Item.UniqueID == weapon.UniqueID && State.GuildStorage[111]?.Item.UniqueID == item.UniqueID);
        await botUntil(() => other.GuildStorage[0]?.Item.UniqueID == weapon.UniqueID && other.GuildStorage[111]?.Item.UniqueID == item.UniqueID);
        Client.MoveGuildItem(111, 0); await Until(() => State.GuildStorage[0]?.Item.UniqueID == item.UniqueID);
        await botUntil(() => other.GuildStorage[111]?.Item.UniqueID == weapon.UniqueID);
        var ownPotions = State.User.Inventory.Where(i => i?.ItemIndex == item.ItemIndex).Sum(i => i.Count);
        var peerPotions = other.User.Inventory.Where(i => i?.ItemIndex == item.ItemIndex).Sum(i => i.Count);
        Client.RetrieveGuildItem(0, from); await bot.Send(new C.GuildStorageItemChange { Type = 1, From = 0, To = peerSlot });
        await Until(() => State.GuildStorage[0] == null); await botUntil(() => other.GuildStorage[0] == null && received.OfType<S.GuildStorageItemChange>().Any(p => p.Type is 1 or 4));
        var inOwnBag = State.User.Inventory.Any(i => i?.UniqueID == item.UniqueID); var inOtherBag = other.User.Inventory.Any(i => i?.UniqueID == item.UniqueID);
        if (inOwnBag == inOtherBag || State.User.Inventory.Where(i => i?.ItemIndex == item.ItemIndex).Sum(i => i.Count) + other.User.Inventory.Where(i => i?.ItemIndex == item.ItemIndex).Sum(i => i.Count) != ownPotions + peerPotions + item.Count) throw new Exception("Competing guild retrieval duplicated or lost an item.");
        // Return the test potion to its original owner for the subsequent trading regression.
        if (inOtherBag)
        {
            var slot = Array.FindIndex(other.User.Inventory, i => i?.UniqueID == item.UniqueID);
            await bot.Send(new C.GuildStorageItemChange { Type = 0, From = slot, To = 0 }); await Until(() => State.GuildStorage[0]?.Item.UniqueID == item.UniqueID);
            Client.RetrieveGuildItem(0, from); await Until(() => State.User.Inventory[from]?.UniqueID == item.UniqueID);
        }
        await bot.Send(new C.GuildStorageItemChange { Type = 1, From = 111, To = weaponSlot });
        await botUntil(() => other.User.Inventory[weaponSlot]?.UniqueID == weapon.UniqueID);
        await Until(() => State.GuildStorage[111] == null);
        stage = "native guild fund donation, withdrawal and leader permission";
        var gold = State.User.Gold; var guildGold = State.Guild!.Gold;
        Client.ChangeGuildGold(false, 17); await Until(() => State.User.Gold == gold - 17 && State.Guild.Gold == guildGold + 17);
        await botUntil(() => other.Guild!.Gold == guildGold + 17);
        received.RemoveAll(p => p is S.GuildStatus);
        await bot.Send(new C.GuildStorageGoldChange { Type = 1, Amount = 1 }); await botUntil(() => received.OfType<S.GuildStatus>().Any());
        if (other.Guild!.Gold != guildGold + 17) throw new Exception("Non-leader withdrew guild funds.");
        Client.ChangeGuildGold(true, 5); await Until(() => State.User.Gold == gold - 12 && State.Guild.Gold == guildGold + 12);
        await botUntil(() => other.Guild!.Gold == guildGold + 12);
        await Screenshot("guild-bank");
        using (var http = new System.Net.Http.HttpClient())
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", File.ReadAllText(Path.Combine(Environment.GetEnvironmentVariable("MIR2_RUNTIME_ROOT")!, "admin.key")).Trim());
            using var list = JsonDocument.Parse(await http.GetStringAsync(Argument("--server").TrimEnd('/') + "/management/admin/guilds"));
            var guild = list.RootElement.EnumerateArray().Single(g => g.GetProperty("name").GetString() == "NativeGuild");
            using var details = JsonDocument.Parse(await http.GetStringAsync(Argument("--server").TrimEnd('/') + "/management/admin/guilds/" + guild.GetProperty("id").GetInt32()));
            if (details.RootElement.GetProperty("gold").GetUInt32() != State.Guild.Gold || details.RootElement.GetProperty("storage").GetArrayLength() != 0) throw new Exception("Management guild funds/storage differ from the native bank.");
        }
        Client.SelectGuildPage(0);
        GD.Print("PASS native guild bank: original 112-slot scroll, safezone and permission rejection, acknowledgements, two-player deposits/swaps/competing retrieval, item-count uniqueness, donation/leader withdrawal conservation, management funds/storage agreement.");
    }
}
