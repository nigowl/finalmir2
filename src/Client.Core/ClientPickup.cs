using S = ServerPackets;

namespace Crystal.Native;

public sealed class ClientPickup
{
    public uint Pending { get; private set; }
    public bool Paused { get; private set; }
    private long sentAt;

    public void Reset() { Pending = 0; Paused = false; sentAt = 0; }
    public void Tick(long now) { if (Pending != 0 && now - sentAt >= 3000) Paused = true; }

    public static string Name(Actor item)
    {
        if (item.Gold > 0) return "金币";
        var name = item.Name.Trim(); var suffix = name.LastIndexOf(" (", StringComparison.Ordinal);
        return suffix > 0 && name.EndsWith(')') && uint.TryParse(name.AsSpan(suffix + 2, name.Length - suffix - 3), out var count) && count > 1 ? name[..suffix] : name;
    }

    public static bool Allowed(Actor item, ClientPreferences preferences)
    {
        var listed = preferences.PickupNames.Contains(Name(item), StringComparer.OrdinalIgnoreCase);
        return preferences.PickupWhitelist ? listed : !listed;
    }

    public Actor? Next(GameState state, ClientPreferences preferences, long now, Func<Actor, bool>? reachable = null)
    {
        Tick(now);
        if (!preferences.AutoPickup || Paused || Pending != 0 || state.User == null || state.Me?.Dead != false || state.Trading || state.StorageActive) return null;
        foreach (var item in state.Actors.Values.Where(actor => actor.Kind == ObjectType.Item && !actor.Hidden && Allowed(actor, preferences) && Functions.MaxDistance(state.User.Location, actor.Location) <= 8)
            .OrderBy(actor => Functions.MaxDistance(state.User.Location, actor.Location)).Take(16))
        {
            if (!Allowed(item, preferences)) continue;
            // 原PickUp不指定物品；混合格整格跳过，避免因归属/负重跳过首件后误拣排除项。
            if (state.Actors.Values.Any(other => other.Kind == ObjectType.Item && other.Location == item.Location && !Allowed(other, preferences))) continue;
            if (item.Gold > 0 ? (ulong)state.User.Gold + item.Gold > uint.MaxValue : state.User.Inventory.All(slot => slot != null)) continue;
            if (reachable != null && !reachable(item)) continue;
            return item;
        }
        return null;
    }

    public bool Begin(GameState state, ClientPreferences preferences, Actor item, long now)
    {
        if (item.Location != state.User?.Location || Next(state, preferences, now)?.Id != item.Id) return false;
        Pending = item.Id; sentAt = now; return true;
    }

    public void Apply(Packet packet)
    {
        if (packet is S.ObjectRemove removed && removed.ObjectID == Pending) { Pending = 0; Paused = false; }
        if (packet is S.UserInformation or S.MapChanged or S.Death or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin) Reset();
    }
}
