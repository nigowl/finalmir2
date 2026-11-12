using System.Drawing;
using S = ServerPackets;

namespace Crystal.Native;

public sealed class Actor
{
    public const int MoveDuration = 600;
    public uint Id;
    public string Name = "";
    public ObjectType Kind;
    public Point Location, Previous;
    public MirDirection Direction;
    public MirAction Action = MirAction.Standing;
    public long MotionTime, ActionTime;
    private System.Numerics.Vector2 motionOrigin;
    private int remoteMoveDuration;
    public bool Dead, Hidden;
    public int Image, Armour, Weapon = -1, Hair;
    public byte AI;
    public uint MasterObjectId;
    public MirGender Gender;
    public MirClass Class;
    public byte Health = 100;
    public System.Drawing.Color Colour = System.Drawing.Color.White;

    public void Move(Point location, MirDirection direction, MirAction action, long? timestamp = null)
    {
        var now = timestamp ?? Environment.TickCount64;
        var moving = action is MirAction.Walking or MirAction.Running;
        if (moving && location == Location && Action == action) { Direction = direction; return; }
        var interval = now - MotionTime;
        var continuous = Kind == ObjectType.Monster && moving && Action is MirAction.Walking or MirAction.Running &&
            interval is >= 100 and <= 2000 && Math.Max(Math.Abs(location.X - Location.X), Math.Abs(location.Y - Location.Y)) <= 2;
        // 远端巡逻包的间隔可能不同于素材帧长；从当前画面接续，不能回跳到上一格。
        motionOrigin = continuous ? PositionAt(now, MoveDuration) : new(Location.X, Location.Y);
        remoteMoveDuration = continuous ? (int)interval : 0;
        Previous = Location;
        MotionTime = now;
        Location = location; Direction = direction; Action = action; ActionTime = MotionTime;
    }

    public int MovementDuration(int animationDuration) => remoteMoveDuration > 0 ? remoteMoveDuration : animationDuration;

    public System.Numerics.Vector2 PositionAt(long now, int duration)
    {
        var end = new System.Numerics.Vector2(Location.X, Location.Y);
        if (Action is not (MirAction.Walking or MirAction.Running)) return end;
        return System.Numerics.Vector2.Lerp(Kind == ObjectType.Monster ? motionOrigin : new(Previous.X, Previous.Y), end,
            Math.Clamp((now - MotionTime) / (float)Math.Max(1, MovementDuration(duration)), 0, 1));
    }
}

public sealed partial class GameState
{
    public S.UserInformation? User;
    public S.MapInformation? Map;
    public readonly Dictionary<uint, Actor> Actors = new();
    public readonly Dictionary<int, ItemInfo> Items = new();
    public readonly HashSet<Spell> ActiveSkills = new();
    public UserItem[] Storage = [];
    public bool StorageActive, StorageUnlocked;
    public readonly Dictionary<int, ClientQuestInfo> QuestDefinitions = new();
    public readonly Dictionary<int, ClientQuestProgress> Quests = new();
    public readonly HashSet<int> CompletedQuests = new();
    public int QuestRevision;
    public bool AllowGroup;
    public string GroupInvitation = "";
    public readonly List<string> GroupMembers = new();
    public readonly Dictionary<string, string> GroupMaps = new(StringComparer.OrdinalIgnoreCase);
    public readonly Dictionary<string, Point> GroupLocations = new(StringComparer.OrdinalIgnoreCase);
    public int GroupRevision, Ping;
    public List<SelectInfo> Characters = new();
    public int InventoryWeight => User?.Inventory.Where(item => item?.Info != null).Sum(item => item.Weight) ?? 0;
    public uint NpcId;
    public BaseStats? BaseStats;
    public int InventoryRevision;
    public event Action<string>? Message;
    public Actor? Me => User != null ? Actors.GetValueOrDefault(User.ObjectID) : null;
    public void Reset()
    {
        ClearCharacter(); Items.Clear(); QuestDefinitions.Clear(); Characters.Clear();
    }
    private void ClearCharacter()
    {
        User = null; Map = null; Actors.Clear(); ActiveSkills.Clear(); BaseStats = null; NpcId = 0;
        Storage = []; StorageActive = StorageUnlocked = false;
        Quests.Clear(); CompletedQuests.Clear();
        ClearGroup(); AllowGroup = false; GroupInvitation = ""; Ping = 0;
        ClearTrade();
        ClearGuild();
        InventoryRevision++; QuestRevision++;
    }

    public void Apply(Packet packet)
    {
        ApplyTrade(packet);
        ApplyGuild(packet);
        ApplyGuildStorage(packet);
        switch (packet)
        {
            case S.KeepAlive p: Ping = (int)Math.Clamp(Environment.TickCount64 - p.Time, 0, 60000); break;
            case S.SwitchGroup p:
                AllowGroup = p.AllowGroup; if (!AllowGroup) { ClearGroup(); GroupInvitation = ""; } GroupRevision++; break;
            case S.GroupInvite p: GroupInvitation = p.Name; GroupRevision++; break;
            case S.AddMember p:
                if (!GroupMembers.Contains(p.Name, StringComparer.OrdinalIgnoreCase)) GroupMembers.Add(p.Name);
                GroupInvitation = ""; GroupRevision++; break;
            case S.DeleteMember p:
                GroupMembers.RemoveAll(n => n.Equals(p.Name, StringComparison.OrdinalIgnoreCase)); GroupMaps.Remove(p.Name); GroupLocations.Remove(p.Name); GroupRevision++; break;
            case S.DeleteGroup: ClearGroup(); break;
            case S.GroupMembersMap p:
                if (GroupMaps.GetValueOrDefault(p.PlayerName) != p.PlayerMap) GroupLocations.Remove(p.PlayerName);
                GroupMaps[p.PlayerName] = p.PlayerMap; GroupRevision++; break;
            case S.SendMemberLocation p: GroupLocations[p.MemberName] = p.MemberLocation; GroupRevision++; break;
            case S.LoginSuccess p: Characters = p.Characters; break;
            case S.NewCharacterSuccess p: Characters.Add(p.CharInfo); break;
            case S.NewItemInfo p:
                Items[p.Info.Index] = p.Info;
                if (User != null) { Bind(User.Inventory); Bind(User.Equipment); Bind(User.QuestInventory); }
                Bind(Storage);
                Bind(TradeItems); Bind(GuestTradeItems);
                foreach (var entry in GuildStorage) Bind(entry?.Item);
                InventoryRevision++; break;
            case S.MapInformation p: Map = p; ResetActors(); break;
            case S.UserInformation p:
                ActiveSkills.Clear();
                Storage = []; StorageActive = StorageUnlocked = false;
                Quests.Clear(); CompletedQuests.Clear(); QuestRevision++;
                User = p; Bind(p.Inventory); Bind(p.Equipment); Bind(p.QuestInventory);
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Kind = ObjectType.Player, Name = p.Name,
                    Location = p.Location, Previous = p.Location, Direction = p.Direction, Gender = p.Gender, Class = p.Class, Hair = p.Hair, Dead = p.HP <= 0 };
                RefreshLooks(); InventoryRevision++; break;
            case S.MapChanged p:
                StorageActive = StorageUnlocked = false;
                Map = new S.MapInformation { MapIndex = p.MapIndex, FileName = p.FileName, Title = p.Title, Music = p.Music, MiniMap = p.MiniMap };
                if (User != null) { User.Location = p.Location; User.Direction = p.Direction; }
                if (Me != null) Me.Move(p.Location, p.Direction, MirAction.Standing);
                ResetActors(); break;
            case S.UserLocation p:
                if (User != null)
                {
                    var moved = User.Location != p.Location;
                    var distance = Math.Max(Math.Abs(User.Location.X - p.Location.X), Math.Abs(User.Location.Y - p.Location.Y));
                    // 攻击/施法同样回发当前位置，不能用原地回执中断尚未播完的动作。
                    if (moved) Me?.Move(p.Location, p.Direction, distance > 1 ? MirAction.Running : MirAction.Walking);
                    else if (Me != null) Me.Direction = p.Direction;
                    User.Location = p.Location; User.Direction = p.Direction;
                }
                break;
            case S.ObjectPlayer p:
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Name = p.Name, Kind = ObjectType.Player,
                    Location = p.Location, Previous = p.Location, Direction = p.Direction, Gender = p.Gender, Class = p.Class,
                    Armour = p.Armour, Weapon = p.Weapon, Hair = p.Hair, Dead = p.Dead, Hidden = p.Hidden, Colour = p.NameColour }; break;
            case S.ObjectMonster p:
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Name = p.Name, Kind = ObjectType.Monster,
                    Location = p.Location, Previous = p.Location, Direction = p.Direction, Image = (int)p.Image,
                    Dead = p.Dead, Hidden = p.Hidden, Colour = p.NameColour, AI = p.AI, MasterObjectId = p.MasterObjectId }; break;
            case S.ObjectNPC p:
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Name = p.Name, Kind = ObjectType.Merchant,
                    Location = p.Location, Previous = p.Location, Direction = p.Direction, Image = p.Image, Colour = p.NameColour }; break;
            case S.ObjectItem p:
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Name = p.Name, Kind = ObjectType.Item, Location = p.Location, Image = p.Image, Colour = p.NameColour }; break;
            case S.ObjectGold p:
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Name = $"{p.Gold} 金币", Kind = ObjectType.Item, Location = p.Location, Image = p.Gold < 100 ? 120 : p.Gold < 1000 ? 121 : 122 }; break;
            case S.ObjectRemove p: Actors.Remove(p.ObjectID); break;
            case S.ObjectTeleportOut p: Actors.Remove(p.ObjectID); break;
            case S.ObjectTurn p: Move(p.ObjectID, p.Location, p.Direction, MirAction.Standing); break;
            case S.ObjectWalk p: Move(p.ObjectID, p.Location, p.Direction, MirAction.Walking); break;
            case S.ObjectRun p: Move(p.ObjectID, p.Location, p.Direction, MirAction.Running); break;
            case S.ObjectPushed p: Move(p.ObjectID, p.Location, p.Direction, MirAction.Walking); break;
            case S.ObjectAttack p: Move(p.ObjectID, p.Location, p.Direction, MirAction.Attack1); break;
            case S.ObjectMagic p: if (p.Cast) Move(p.ObjectID, p.Location, p.Direction, MirAction.Spell); break;
            case S.ObjectDied p:
                Move(p.ObjectID, p.Location, p.Direction, MirAction.Die);
                if (Actors.TryGetValue(p.ObjectID, out var corpse)) corpse.Dead = true;
                break;
            case S.Death: if (Me != null) { Me.Dead = true; Me.Action = MirAction.Die; Me.ActionTime = Environment.TickCount64; } break;
            case S.Revived: if (Me != null) { Me.Dead = false; Me.Action = MirAction.Standing; } break;
            case S.ObjectRevived p: if (Actors.TryGetValue(p.ObjectID, out var revived)) { revived.Dead = false; revived.Action = MirAction.Standing; } break;
            case S.ObjectHealth p: if (Actors.TryGetValue(p.ObjectID, out var hp)) hp.Health = p.Percent; break;
            case S.ObjectHide p: if (Actors.TryGetValue(p.ObjectID, out var hidden)) hidden.Hidden = true; break;
            case S.ObjectShow p: if (Actors.TryGetValue(p.ObjectID, out var shown)) shown.Hidden = false; break;
            case S.PlayerUpdate p:
                if (Actors.TryGetValue(p.ObjectID, out var look)) { look.Armour = p.Armour; look.Weapon = p.Weapon; } break;
            case S.HealthChanged p: if (User != null) { User.HP = p.HP; User.MP = p.MP; } break;
            case S.BaseStatsInfo p: BaseStats = p.Stats; break;
            case S.GainExperience p: if (User != null) User.Experience += p.Amount; break;
            case S.LevelChanged p:
                if (User != null) { User.Level = p.Level; User.Experience = p.Experience; User.MaxExperience = p.MaxExperience; }
                Message?.Invoke($"等级提升至 {p.Level}"); break;
            case S.GainedGold p: if (User != null) User.Gold += p.Gold; break;
            case S.LoseGold p: if (User != null) User.Gold -= p.Gold; break;
            case S.GainedItem p:
                Bind(p.Item);
                var gainedCount = p.Item.Count;
                AddItem(p.Item);
                Message?.Invoke($"获得 {p.Item.Info?.FriendlyName ?? "物品"} ×{gainedCount}"); InventoryRevision++; break;
            case S.DeleteItem p: Remove(p.UniqueID, p.Count); break;
            case S.UseItem p:
                if (p.Success) { Message?.Invoke($"使用成功：{ItemName(p.UniqueID)}。"); Remove(p.UniqueID, 1); }
                else Message?.Invoke("使用失败，请检查等级、职业、冷却及物品要求。");
                break;
            case S.SellItem p:
                if (p.Success) { Message?.Invoke($"出售成功：{ItemName(p.UniqueID)} ×{p.Count}。"); Remove(p.UniqueID, p.Count); }
                else Message?.Invoke("出售失败，请检查物品限制、数量及 NPC 距离。");
                break;
            case S.DropItem p:
                if (p.HeroItem) break;
                if (p.Success) { Message?.Invoke($"丢弃成功：{ItemName(p.UniqueID)} ×{p.Count}。"); Remove(p.UniqueID, p.Count); }
                else Message?.Invoke("丢弃失败，请检查地图、物品绑定及角色状态。");
                break;
            case S.EquipItem p:
                if (p.Success && p.Grid == MirGridType.Inventory && User != null)
                {
                    var from = Array.FindIndex(User.Inventory, item => item?.UniqueID == p.UniqueID);
                    if (from >= 0 && p.To >= 0 && p.To < User.Equipment.Length)
                        (User.Inventory[from], User.Equipment[p.To]) = (User.Equipment[p.To], User.Inventory[from]);
                    RefreshLooks(); InventoryRevision++; Message?.Invoke($"穿戴成功：{ItemName(p.UniqueID)}。");
                }
                else Message?.Invoke("装备失败，请检查等级、职业与负重。");
                break;
            case S.RemoveItem p:
                if (p.Success && p.Grid == MirGridType.Inventory && User != null)
                {
                    var from = Array.FindIndex(User.Equipment, item => item?.UniqueID == p.UniqueID);
                    if (from >= 0 && p.To >= 0 && p.To < User.Inventory.Length)
                    { User.Inventory[p.To] = User.Equipment[from]; User.Equipment[from] = null; }
                    RefreshLooks(); InventoryRevision++; Message?.Invoke($"卸下成功：{ItemName(p.UniqueID)}。");
                }
                else Message?.Invoke("卸下失败，请检查背包空位、负重及当前状态。");
                break;
            case S.UserSlotsRefresh p:
                if (User != null)
                {
                    if (p.Inventory != null) { User.Inventory = p.Inventory; Bind(p.Inventory); }
                    if (p.Equipment != null) { User.Equipment = p.Equipment; Bind(p.Equipment); }
                    RefreshLooks(); InventoryRevision++;
                }
                break;
            case S.RefreshItem p:
                Bind(p.Item);
                if (User != null) foreach (var array in new[] { User.Inventory, User.Equipment, User.QuestInventory, Storage })
                { var index = Array.FindIndex(array, item => item?.UniqueID == p.Item.UniqueID); if (index >= 0) array[index] = p.Item; }
                InventoryRevision++; break;
            case S.DuraChanged p:
                var worn = User?.Inventory.Concat(User.Equipment).FirstOrDefault(i => i?.UniqueID == p.UniqueID);
                if (worn != null) worn.CurrentDura = p.CurrentDura;
                InventoryRevision++; break;
            case S.ItemRepaired p:
                var repaired = User?.Inventory.Concat(User.Equipment).FirstOrDefault(i => i?.UniqueID == p.UniqueID);
                if (repaired != null) { repaired.CurrentDura = p.CurrentDura; repaired.MaxDura = p.MaxDura; Message?.Invoke($"修理成功：{repaired.Info?.FriendlyName}，耐久 {p.CurrentDura / 1000f:0.###}/{p.MaxDura / 1000f:0.###}。"); }
                InventoryRevision++; break;
            case S.MoveItem p:
                var grid = p.Grid == MirGridType.Storage ? Storage : p.Grid == MirGridType.Inventory ? User?.Inventory : null;
                if (p.Success && ValidSlot(grid, p.From) && ValidSlot(grid, p.To))
                    (grid![p.From], grid[p.To]) = (grid[p.To], grid[p.From]);
                InventoryRevision++; break;
            case S.UserStorage p: Storage = p.Storage ?? []; Bind(Storage); InventoryRevision++; break;
            case S.ResizeStorage p:
                if (User != null && p.Size is >= 0 and <= 160)
                {
                    if (Storage.Length > 0) Array.Resize(ref Storage, p.Size);
                    User.HasExpandedStorage = p.HasExpandedStorage; User.ExpandedStorageExpiryTime = p.ExpiryTime;
                    InventoryRevision++;
                }
                break;
            case S.NPCStorage:
                StorageActive = true; StorageUnlocked = User?.RequireStoragePassword == false;
                if (!StorageUnlocked && User?.HasStoragePassword == true) Storage = [];
                InventoryRevision++; break;
            case S.NPCResponse: StorageActive = StorageUnlocked = false; break;
            case S.StorageUnlockResult p:
                if (User != null) User.HasStoragePassword = p.HasPassword;
                StorageUnlocked = StorageActive && p.Result is 0 or 4;
                InventoryRevision++; break;
            case S.StoragePasswordResult p:
                if (User != null) { User.HasStoragePassword = p.HasPassword; User.StoragePasswordLastSet = p.LastSetTime; }
                if (p.Result == 4) StorageUnlocked = StorageActive && !p.Removing;
                InventoryRevision++; break;
            case S.StoreItem p:
                if (p.Success) Transfer(User?.Inventory, Storage, p.From, p.To);
                else Message?.Invoke("存入失败，请检查仓库解锁、物品限制、格位及 NPC 距离。");
                InventoryRevision++; break;
            case S.TakeBackItem p:
                if (p.Success) Transfer(Storage, User?.Inventory, p.From, p.To);
                else Message?.Invoke("取出失败，请检查背包空位及 NPC 距离。");
                InventoryRevision++; break;
            case S.NewQuestInfo p:
                QuestDefinitions[p.Info.Index] = p.Info;
                if (Quests.TryGetValue(p.Info.Index, out var known)) known.QuestInfo = p.Info;
                QuestRevision++; break;
            case S.ChangeQuest p:
                if (p.QuestState == QuestState.Remove) Quests.Remove(p.Quest.Id);
                else { p.Quest.QuestInfo = QuestDefinitions.GetValueOrDefault(p.Quest.Id)!; Quests[p.Quest.Id] = p.Quest; }
                QuestRevision++; break;
            case S.CompleteQuest p:
                CompletedQuests.Clear(); CompletedQuests.UnionWith(p.CompletedQuests); QuestRevision++; break;
            case S.GainedQuestItem p:
                Bind(p.Item);
                if (User != null && p.Item.Info != null && !Stack(p.Item, User.QuestInventory))
                { var slot = Array.IndexOf(User.QuestInventory, null); if (slot >= 0) User.QuestInventory[slot] = p.Item; }
                InventoryRevision++; break;
            case S.DeleteQuestItem p:
                RemoveFrom(User?.QuestInventory, p.UniqueID, p.Count); InventoryRevision++; break;
            case S.NewMagic p:
                if (User != null && !p.Hero) { User.Magics.Add(p.Magic); Message?.Invoke($"学会技能：{p.Magic.Name}。"); }
                break;
            case S.MagicDelay p:
                var delayed = User?.Magics.FirstOrDefault(m => m.Spell == p.Spell);
                if (delayed != null) delayed.Delay = p.Delay;
                break;
            case S.MagicLeveled p:
                var magic = User?.Magics.FirstOrDefault(m => m.Spell == p.Spell);
                if (magic != null) { magic.Level = p.Level; magic.Experience = p.Experience; }
                break;
            case S.Magic p: if (Me != null && p.Cast) { Me.Action = MirAction.Spell; Me.ActionTime = Environment.TickCount64; } break;
            case S.SpellToggle p:
                if (p.ObjectID == User?.ObjectID) { if (p.CanUse) ActiveSkills.Add(p.Spell); else ActiveSkills.Remove(p.Spell); }
                break;
            case S.NPCUpdate p: NpcId = p.NPCID; break;
            case S.Chat p: Message?.Invoke(p.Message); break;
            case S.ObjectChat p: Message?.Invoke(p.Text); break;
            case S.LogOutSuccess p: ClearCharacter(); Characters = p.Characters; break;
            case S.ReturnToLogin: ClearCharacter(); Characters.Clear(); break;
            case S.Disconnect: ClearCharacter(); break;
        }
    }
    private void ClearGroup() { GroupMembers.Clear(); GroupMaps.Clear(); GroupLocations.Clear(); GroupRevision++; }

    private void Move(uint id, Point point, MirDirection direction, MirAction action) => Actors.GetValueOrDefault(id)?.Move(point, direction, action);
    private void AddItem(UserItem item)
    {
        if (User == null || item.Info == null) return;
        // Match GameScene.AddItem and HumanObject.AddItem, including partial stack overflow.
        if (Stack(item, User.Inventory)) return;
        var first = item.Info.Type == ItemType.Amulet ? 4 :
            item.Info.Type is ItemType.Potion or ItemType.Scroll || (item.Info.Type == ItemType.Script && item.Info.Effect == 1) ? 0 : 6;
        var end = first == 0 ? Math.Min(4, User.Inventory.Length) : first == 4 ? Math.Min(6, User.Inventory.Length) : User.Inventory.Length;
        var slot = Enumerable.Range(Math.Min(first, end), end - Math.Min(first, end)).FirstOrDefault(i => User.Inventory[i] == null, -1);
        if (slot < 0) slot = Array.IndexOf(User.Inventory, null);
        if (slot >= 0) User.Inventory[slot] = item;
    }
    private void ResetActors() { var me = Me; Actors.Clear(); if (me != null) Actors[me.Id] = me; }
    public void Bind(UserItem? item)
    {
        if (item == null) return;
        if (Items.TryGetValue(item.ItemIndex, out var info)) item.Info = info;
        Bind(item.Slots);
    }
    private void Bind(UserItem[]? array) { if (array != null) foreach (var item in array) Bind(item); }
    private string ItemName(ulong id) => User?.Inventory.Concat(User.Equipment).FirstOrDefault(item => item?.UniqueID == id)?.Info?.FriendlyName ?? "物品";
    private void Remove(ulong id, int count)
    {
        if (User == null) return;
        foreach (var array in new[] { User.Inventory, User.Equipment, TradeItems }) RemoveFrom(array, id, count);
        RefreshLooks(); InventoryRevision++;
    }
    private static bool ValidSlot(UserItem[]? grid, int slot) => grid != null && (uint)slot < grid.Length;
    private static void Transfer(UserItem[]? from, UserItem[]? to, int source, int destination)
    {
        if (!ValidSlot(from, source) || !ValidSlot(to, destination) || from![source] == null || to![destination] != null) return;
        to![destination] = from![source]; from[source] = null!;
    }
    private static void RemoveFrom(UserItem[]? grid, ulong id, int count)
    {
        if (grid == null || count <= 0) return;
        var index = Array.FindIndex(grid, item => item?.UniqueID == id);
        if (index < 0) return;
        if (grid[index].Count <= count) grid[index] = null!; else grid[index].Count -= (ushort)count;
    }
    private static bool Stack(UserItem item, UserItem[] grid)
    {
        if (item.Info.StackSize > 1)
            foreach (var existing in grid)
            {
                if (!item.CanStackWith(existing) || existing.Count >= existing.Info.StackSize) continue;
                var added = Math.Min(item.Count, existing.Info.StackSize - existing.Count);
                existing.Count += (ushort)added; item.Count -= (ushort)added;
                if (item.Count == 0) return true;
            }
        return false;
    }
    public bool CanAcceptQuest(ClientQuestInfo quest) => User != null && !Quests.ContainsKey(quest.Index) && !CompletedQuests.Contains(quest.Index) &&
        Quests.Count < Globals.MaxConcurrentQuests && User.Level >= quest.MinLevelNeeded && User.Level <= quest.MaxLevelNeeded &&
        (quest.ClassNeeded & (RequiredClass)(1 << (int)User.Class)) != 0 && (quest.QuestNeeded <= 0 || CompletedQuests.Contains(quest.QuestNeeded));
    public IEnumerable<ClientQuestInfo> QuestsAt(uint npc) => QuestDefinitions.Values.Where(q =>
        (q.NPCIndex == npc && (CanAcceptQuest(q) || Quests.ContainsKey(q.Index))) || (q.FinishNPCIndex == npc && Quests.ContainsKey(q.Index)))
        .OrderByDescending(q => Quests.GetValueOrDefault(q.Index)?.Completed == true).ThenBy(q => q.Index);
    private void RefreshLooks()
    {
        if (Me == null || User == null) return;
        Me.Armour = User.Equipment[(int)EquipmentSlot.Armour]?.Info?.Shape ?? 0;
        Me.Weapon = User.Equipment[(int)EquipmentSlot.Weapon]?.Info?.Shape ?? -1;
    }
    public Stats CalculateStats()
    {
        // shortcut: presentation omits full item-set/buff formulas; damage and legality stay authoritative on the original server.
        var stats = new Stats();
        if (User == null) return stats;
        foreach (var stat in BaseStats?.Stats ?? []) stats[stat.Type] = stat.Calculate(User.Class, User.Level);
        foreach (var item in User.Equipment.Where(item => item?.Info != null)) { stats.Add(item.Info.Stats); stats.Add(item.AddedStats); }
        stats[Stat.HP] += stats[Stat.HP] * stats[Stat.HPRatePercent] / 100;
        stats[Stat.MP] += stats[Stat.MP] * stats[Stat.MPRatePercent] / 100;
        return stats;
    }
}
