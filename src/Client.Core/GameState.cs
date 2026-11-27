using System.Drawing;
using S = ServerPackets;

namespace Crystal.Native;

public sealed class Actor
{
    public const int MoveDuration = 600;
    public int RunContinuationWindow => Math.Max(1100, LastMoveDelay + 100);
    public const int MovementBuffer = 34;
    public PlayerActionTiming PlayerTiming { get; init; } = PlayerActionTiming.Default;
    public int MoveDelay => (Kind == ObjectType.Player ? PlayerTiming.MoveMilliseconds : MoveDuration) * (Poison.HasFlag(PoisonType.Slow) ? 2 : 1);
    public int LastMoveDelay => (Kind == ObjectType.Player ? PlayerTiming.MoveMilliseconds : MoveDuration) * (movementSlowed ? 2 : 1);
    public uint Id;
    public string Name = "";
    public string NpcRole = "";
    public ObjectType Kind;
    public uint Gold;
    public Point Location, Previous;
    public MirDirection Direction;
    public MirAction Action = MirAction.Standing;
    public Spell ActionSpell;
    public byte SpellLevel;
    public bool Casting, SpellParam, MagicShield, ElementalBarrier;
    public bool IsShinsu => Kind == ObjectType.Monster && Image is (int)Monster.Shinsu or (int)Monster.Shinsu1;
    public bool Concentrating, ConcentrationInterrupted;
    public uint ElementLevel;
    public readonly Dictionary<uint, uint> Elements = new();
    public readonly HashSet<BuffType> Buffs = new();
    public long MotionTime, ActionTime;
    private bool movementSlowed;
    private System.Numerics.Vector2 motionOrigin;
    private System.Numerics.Vector2 motionCorner;
    private float motionCornerFraction;
    private bool skillMovement;
    private int remoteMoveDuration;
    private double movementPhase;
    public bool Dead, Hidden, Harvested, RidingMount;
    public int Image, Armour, Weapon = -1, Hair;
    public EquipmentGlow EquipmentGlow;
    public byte AI;
    public uint MasterObjectId;
    public PoisonType Poison;
    public MirGender Gender;
    public MirClass Class;
    public byte Health = 100;
    public int? CurrentHealth, MaximumHealth;
    public System.Drawing.Color Colour = System.Drawing.Color.White;

    public void Move(Point location, MirDirection direction, MirAction action, long? timestamp = null, bool isSkillMovement = false)
    {
        var now = timestamp ?? Environment.TickCount64;
        var dx = Math.Abs(location.X - Location.X); var dy = Math.Abs(location.Y - Location.Y);
        if (Kind == ObjectType.Player && !isSkillMovement && action is MirAction.Walking or MirAction.Running &&
            (Math.Max(dx, dy) > 3 || dx != 0 && dy != 0 && dx != dy)) action = MirAction.Standing;
        var moving = action is MirAction.Walking or MirAction.Running or MirAction.Pushed or MirAction.Jump or MirAction.DashAttack;
        if (moving && location == Location && Action == action) { Direction = direction; return; }
        var interval = now - MotionTime;
        var continuous = !isSkillMovement && !skillMovement && (Kind == ObjectType.Monster && moving || Kind == ObjectType.Player && action is MirAction.Walking or MirAction.Running) &&
            Action is MirAction.Walking or MirAction.Running && interval >= 100 && interval <= (Kind == ObjectType.Player ? MoveDelay + 250 : 2000) &&
            Math.Max(Math.Abs(location.X - Location.X), Math.Abs(location.Y - Location.Y)) <= (Kind == ObjectType.Player ? 3 : 2);
        // 回执间隔包含网络和帧调度；从当前画面接续，不回跳或在每步末尾切站立。
        var phase = continuous && Kind == ObjectType.Player ? MovementAnimationElapsed(now, MoveDuration) / (double)MoveDuration : 0;
        var cornerPending = continuous && motionCornerFraction > 0 && now - MotionTime < MovementDuration(MoveDuration) * motionCornerFraction;
        motionOrigin = continuous || action == MirAction.Pushed ? PositionAt(now, MoveDuration) : new(Location.X, Location.Y);
        // 转弯先走完已确认的一段；过密回执不积压路径，回到上一确认格重新同步。
        if (cornerPending) motionOrigin = new(Location.X, Location.Y);
        motionCorner = new(Location.X, Location.Y);
        var remaining = System.Numerics.Vector2.Abs(motionCorner - motionOrigin);
        var firstLength = Math.Max(remaining.X, remaining.Y);
        motionCornerFraction = continuous && firstLength > 0 && Math.Max(dx, dy) > 0 ? firstLength / (firstLength + Math.Max(dx, dy)) : 0;
        remoteMoveDuration = continuous && movementSlowed == Poison.HasFlag(PoisonType.Slow) ? (int)interval : 0;
        movementPhase = phase;
        Previous = Location;
        MotionTime = now;
        Location = location; Direction = direction; Action = action; ActionTime = MotionTime;
        movementSlowed = Poison.HasFlag(PoisonType.Slow);
        skillMovement = isSkillMovement;
    }

    public int MovementDuration(int animationDuration)
    {
        if (Action == MirAction.Pushed) return Kind == ObjectType.Player ? PlayerTiming.Scale(500) : 500;
        var step = !skillMovement && Action is MirAction.Walking or MirAction.Running;
        var duration = remoteMoveDuration > 0 ? remoteMoveDuration :
            (Kind == ObjectType.Player ? step ? animationDuration * PlayerTiming.MoveMilliseconds / MoveDuration : PlayerTiming.Scale(animationDuration) : animationDuration) *
            (step && movementSlowed ? 2 : 1);
        // 留两帧显示余量吸收回执抖动；坐标始终不越过已确认终点。
        return duration + (Kind == ObjectType.Player && step ? MovementBuffer : 0);
    }

    public long MovementAnimationElapsed(long now, int duration) => (long)((movementPhase +
        Math.Max(0, now - MotionTime) / (double)Math.Max(1, MovementDuration(MoveDuration))) * duration);

    public System.Numerics.Vector2 PositionAt(long now, int duration)
    {
        var end = new System.Numerics.Vector2(Location.X, Location.Y);
        if (Action is not (MirAction.Walking or MirAction.Running or MirAction.Pushed or MirAction.Jump or MirAction.DashAttack)) return end;
        var progress = Math.Clamp((now - MotionTime) / (float)Math.Max(1, MovementDuration(duration)), 0, 1);
        if (motionCornerFraction > 0)
            return progress < motionCornerFraction
                ? System.Numerics.Vector2.Lerp(motionOrigin, motionCorner, progress / motionCornerFraction)
                : System.Numerics.Vector2.Lerp(motionCorner, end, (progress - motionCornerFraction) / (1 - motionCornerFraction));
        return System.Numerics.Vector2.Lerp(motionOrigin, end, progress);
    }
}

public sealed partial class GameState
{
    public PlayerActionTiming PlayerTiming { get; set; } = PlayerActionTiming.Default;
    public S.UserInformation? User;
    public S.MapInformation? Map;
    public readonly Dictionary<uint, Actor> Actors = new();
    public readonly HashSet<uint> ManuallyDropped = new();
    public bool ManualDropHistoryFull { get; private set; }
    public readonly Dictionary<int, ItemInfo> Items = new();
    public readonly HashSet<string> MaterialItemNames = new(StringComparer.Ordinal);
    public readonly HashSet<Spell> ActiveSkills = new();
    public UserItem[] Storage = [];
    public bool StorageActive, StorageUnlocked;
    public int StorageCapacity => Mir2.StorageRules.Capacity(Storage.Length,User?.HasExpandedStorage==true);
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
    public AttackMode CurrentAttackMode { get; private set; } = AttackMode.Peace;
    public List<SelectInfo> Characters = new();
    public decimal InventoryWeight => User?.Inventory.Where(item => item?.Info != null).Sum(item => item.Weight) ?? 0;
    public uint NpcId;
    public BaseStats? BaseStats;
    private readonly Dictionary<int, ItemPrice> itemPrices = new();
    public int InventoryRevision;
    public event Action<string>? Message;
    public event Action<string, ChatType>? ChatMessage;
    public event Action<string, ChatType>? ItemMessage;
    private void NotifyMessage(string text, ChatType type = ChatType.System)
    { Message?.Invoke(text); ChatMessage?.Invoke(text, type); }
    private void NotifyItemMessage(string text, ChatType type = ChatType.System2)
    { Message?.Invoke(text); ItemMessage?.Invoke(text, type); }
    public Actor? Me => User != null ? Actors.GetValueOrDefault(User.ObjectID) : null;
    public void Reset()
    {
        PlayerTiming = PlayerActionTiming.Default;
        ClearCharacter(); itemPrices.Clear(); Items.Clear(); MaterialItemNames.Clear(); QuestDefinitions.Clear(); Characters.Clear();
    }
    private void ClearCharacter()
    {
        ResetItemOwnerNames();
        InTrapRock = false; OpenDoors.Clear();
        PublicShop = null; PublicShopRevision++; QuestNavigation.Clear();
        User = null; Map = null; Actors.Clear(); ActiveSkills.Clear(); DisabledSkills.Clear(); UserBuffs.Clear(); BaseStats = null; NpcId = 0;
        ManuallyDropped.Clear(); ManualDropHistoryFull = false;
        Storage = []; StorageActive = StorageUnlocked = false;
        Quests.Clear(); CompletedQuests.Clear();
        ClearGroup(); AllowGroup = false; GroupInvitation = ""; Ping = 0;
        CurrentAttackMode = AttackMode.Peace;
        ClearTrade();
        ClearGuild();
        ResetSocial();
        InventoryRevision++; QuestRevision++;
    }

    public void Apply(Packet packet)
    {
        if (ApplyMovement(packet) || ApplySocial(packet) || ApplyPublicServices(packet)) return;
        ApplyTrade(packet);
        ApplyGuild(packet);
        ApplyGuildStorage(packet);
        switch (packet)
        {
            case S.ChangeAMode p:
                if (Enum.IsDefined(p.Mode)) { CurrentAttackMode = p.Mode; NotifyMessage($"攻击模式：{p.Mode.ToLocalizedString()}。", ChatType.System); }
                break;
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
            case S.DeleteCharacterSuccess p:
                Characters.RemoveAll(character => character.Index == p.CharacterIndex); break;
            case S.ItemPriceChanged p:
                if (p.Configured) itemPrices[p.Prices.ItemIndex] = p.Prices;
                else itemPrices.Remove(p.Prices.ItemIndex);
                if (Items.TryGetValue(p.Prices.ItemIndex, out var pricedInfo)) pricedInfo.TradingPrices = p.Configured ? p.Prices : null;
                InventoryRevision++; break;
            case S.NewItemInfo p:
                p.Info.TradingPrices = itemPrices.GetValueOrDefault(p.Info.Index);
                Items[p.Info.Index] = p.Info;
                if (User != null) { Bind(User.Inventory); Bind(User.Equipment); Bind(User.QuestInventory); }
                if (Hero != null) { Bind(Hero.Inventory); Bind(Hero.Equipment); SocialRevision++; }
                Bind(Storage);
                Bind(TradeItems); Bind(GuestTradeItems);
                foreach (var mail in Mail) foreach (var item in mail.Items) Bind(item);
                foreach (var entry in GuildStorage) Bind(entry?.Item);
                InventoryRevision++; break;
            case S.MapInformation p: Map = p; ResetActors(); break;
            case S.UserInformation p:
                ResetItemOwnerNames();
                ActiveSkills.Clear(); DisabledSkills.Clear();
                Storage = []; StorageActive = StorageUnlocked = false;
                Quests.Clear(); CompletedQuests.Clear(); QuestRevision++;
                User = p; Bind(p.Inventory); Bind(p.Equipment); Bind(p.QuestInventory);
                foreach (var skill in p.Magics) skill.CastTime += Environment.TickCount64;
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Kind = ObjectType.Player, PlayerTiming = PlayerTiming, Name = p.Name,
                    Location = p.Location, Previous = p.Location, Direction = p.Direction, Gender = p.Gender, Class = p.Class, Hair = p.Hair, Dead = p.HP <= 0, Colour = p.NameColour };
                RefreshLooks(); InventoryRevision++; break;
            case S.ColourChanged p when User != null:
                User.NameColour = p.NameColour;
                if (Me != null) Me.Colour = p.NameColour;
                break;
            case S.ObjectColourChanged p when p.ObjectID != User?.ObjectID:
                if (Actors.TryGetValue(p.ObjectID, out var recoloured)) recoloured.Colour = p.NameColour;
                break;
            case S.ObjectName p when p.ObjectID != User?.ObjectID:
                if (Actors.TryGetValue(p.ObjectID, out var renamed)) renamed.Name = p.Name;
                break;
            case S.UserName p: ItemOwnerNameReceived(p); break;
            case S.ResizeInventory p when User != null && p.Size > User.Inventory.Length && p.Size <= Globals.MaxInventorySize:
                Array.Resize(ref User.Inventory, p.Size); if (Hero != null && User != null) Hero.Inventory = User.Inventory;
                InventoryRevision++; break;
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
                Actors.TryGetValue(p.ObjectID, out var previousPlayer);
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Name = p.Name, Kind = ObjectType.Player, PlayerTiming = PlayerTiming,
                    Location = p.Location, Previous = p.Location, Direction = p.Direction, Gender = p.Gender, Class = p.Class,
                    Armour = p.Armour, Weapon = p.Weapon, Hair = p.Hair, Dead = p.Dead, Hidden = p.Hidden, Colour = p.NameColour, Poison = p.Poison, RidingMount = p.RidingMount,
                    CurrentHealth = previousPlayer?.Kind == ObjectType.Player ? previousPlayer.CurrentHealth : null,
                    EquipmentGlow = previousPlayer?.Kind == ObjectType.Player ? previousPlayer.EquipmentGlow : EquipmentGlow.None,
                    MaximumHealth = previousPlayer?.Kind == ObjectType.Player ? previousPlayer.MaximumHealth : null }; break;
            case S.MountUpdate p:
                if (Actors.TryGetValue(p.ObjectID, out var mounted)) mounted.RidingMount = p.RidingMount;
                break;
            case S.ObjectMonster p:
                var newMonster = !Actors.TryGetValue(p.ObjectID, out var monster) || monster.Kind != ObjectType.Monster;
                if (newMonster) Actors[p.ObjectID] = monster = new Actor { Id = p.ObjectID, Kind = ObjectType.Monster, Location = p.Location, Previous = p.Location };
                // 归属刷新复用原对象；只有位置、造型或生死变化才丢弃旧动作插值。
                if (monster!.Location != p.Location || monster.Image != (int)p.Image || monster.Dead != p.Dead)
                { monster.Move(p.Location, p.Direction, MirAction.Standing); monster.Previous = p.Location; monster.Casting = false; }
                monster.Name = p.Name; monster.Direction = p.Direction; monster.Image = (int)p.Image;
                monster.Dead = p.Dead; monster.Hidden = p.Hidden; monster.Harvested = p.Skeleton;
                monster.Colour = p.NameColour; monster.AI = p.AI; monster.MasterObjectId = p.MasterObjectId; monster.Poison = p.Poison;
                monster.Buffs.Clear(); monster.Buffs.UnionWith(p.Buffs);
                if (newMonster && !p.Dead && p.Image == Monster.Shinsu && !p.Extra) Move(p.ObjectID, p.Location, p.Direction, MirAction.Appear);
                break;
            case S.Poisoned p: if (Me != null) Me.Poison = p.Poison; break;
            case S.ObjectPoisoned p: if (Actors.TryGetValue(p.ObjectID, out var poisoned)) poisoned.Poison = p.Poison; break;
            case S.ObjectNPC p:
                var npcLabel = NpcLabels.Read(p.Name);
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Name = npcLabel.Name, NpcRole = npcLabel.Role, Kind = ObjectType.Merchant,
                    Location = p.Location, Previous = p.Location, Direction = p.Direction, Image = p.Image, Colour = p.NameColour }; break;
            case S.ObjectItem p:
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Name = p.Name, Kind = ObjectType.Item, Location = p.Location, Image = p.Image, Colour = p.NameColour }; break;
            case S.ObjectGold p:
                Actors[p.ObjectID] = new Actor { Id = p.ObjectID, Name = $"{p.Gold} 金币", Kind = ObjectType.Item, Gold = p.Gold, Location = p.Location, Image = ItemPresentation.GroundGoldImage(p.Gold) }; break;
            case S.ObjectRemove p: Actors.Remove(p.ObjectID); break;
            case S.ObjectTeleportOut p: Actors.Remove(p.ObjectID); break;
            case S.ObjectTurn p: Move(p.ObjectID, p.Location, p.Direction, MirAction.Standing); break;
            case S.ObjectWalk p: Move(p.ObjectID, p.Location, p.Direction, MirAction.Walking); break;
            case S.ObjectRun p: Move(p.ObjectID, p.Location, p.Direction, MirAction.Running); break;
            case S.ObjectDied p:
                Move(p.ObjectID, p.Location, p.Direction, MirAction.Die);
                if (Actors.TryGetValue(p.ObjectID, out var corpse)) corpse.Dead = true;
                break;
            case S.ObjectHarvested p: if (Actors.TryGetValue(p.ObjectID, out var harvested)) harvested.Harvested = true; break;
            case S.Death: if (Me != null) { Me.Dead = true; Me.Action = MirAction.Die; Me.ActionTime = Environment.TickCount64; } break;
            case S.Revived: if (Me != null) { Me.Dead = false; Me.Action = MirAction.Standing; } break;
            case S.ObjectRevived p: if (Actors.TryGetValue(p.ObjectID, out var revived)) { revived.Dead = false; revived.Action = MirAction.Standing; } break;
            case S.ObjectHealth p: if (Actors.TryGetValue(p.ObjectID, out var hp)) hp.Health = p.Percent; break;
            case S.ObjectVitals p:
                if (p.MaxHP > 0 && p.HP >= 0 && p.HP <= p.MaxHP && Actors.TryGetValue(p.ObjectID, out var vital) && vital.Kind is ObjectType.Player or ObjectType.Monster)
                { vital.CurrentHealth = p.HP; vital.MaximumHealth = p.MaxHP; vital.Health = (byte)((long)p.HP * 100 / p.MaxHP); }
                break;
            case S.ObjectHide p: SetActorShown(p.ObjectID, false); break;
            case S.ObjectShow p: SetActorShown(p.ObjectID, true); break;
            case S.PlayerUpdate p:
                if (Actors.TryGetValue(p.ObjectID, out var look)) { look.Armour = p.Armour; look.Weapon = p.Weapon; } break;
            case S.ObjectEquipmentGlow p:
                if ((p.Glow & ~(EquipmentGlow.Weapon | EquipmentGlow.Equipment)) == 0 && Actors.TryGetValue(p.ObjectID, out var glowing) && glowing.Kind == ObjectType.Player)
                    glowing.EquipmentGlow = p.Glow;
                break;
            case S.ManualItemDropped p when p.ObjectID != 0:
                if (ManuallyDropped.Contains(p.ObjectID)) break;
                if (ManuallyDropped.Count < 4096) ManuallyDropped.Add(p.ObjectID);
                else if (!ManualDropHistoryFull)
                { ManualDropHistoryFull = true; NotifyMessage("手动丢弃记录已达上限，自动拣取暂停；请重新登录。", ChatType.Hint); }
                break;
            case S.HealthChanged p: if (User != null) { User.HP = p.HP; User.MP = p.MP; } break;
            case S.BaseStatsInfo p: BaseStats = p.Stats; break;
            case S.GainExperience p:
                if (User != null) { User.Experience += p.Amount; if (p.Amount > 0) NotifyMessage($"获得经验 +{p.Amount:N0}", ChatType.System2); }
                break;
            case S.LevelChanged p:
                if (User != null) { User.Level = p.Level; User.Experience = p.Experience; User.MaxExperience = p.MaxExperience; }
                NotifyMessage($"等级提升至 {p.Level}", ChatType.LevelUp); break;
            case S.GainedGold p: if (User != null) { User.Gold += p.Gold; NotifyItemMessage($"获得金币 ×{p.Gold:N0}", ChatType.System2); } break;
            case S.LoseGold p: if (User != null) { User.Gold -= p.Gold; NotifyItemMessage($"消耗金币 ×{p.Gold:N0}", ChatType.System2); } break;
            case S.GainedItem p:
                Bind(p.Item);
                var gainedCount = p.Item.Count;
                AddItem(p.Item);
                NotifyItemMessage($"获得 {p.Item.Info?.FriendlyName ?? "物品"} ×{gainedCount}", ChatType.System2); InventoryRevision++; break;
            case S.DeleteItem p:
                if (p.Count > 0 && User?.Inventory.Concat(User.QuestInventory).Concat(User.Equipment).Any(item => item?.UniqueID == p.UniqueID) == true)
                    NotifyItemMessage($"扣除物品：{ItemName(p.UniqueID)} ×{p.Count}。", ChatType.System2);
                Remove(p.UniqueID, p.Count); break;
            case S.UseItem p:
                if (p.Success) { NotifyItemMessage($"使用成功：{ItemName(p.UniqueID)}。", ChatType.System2); Remove(p.UniqueID, 1); }
                else NotifyMessage("使用失败，请检查等级、职业、冷却及物品要求。", ChatType.Hint);
                break;
            case S.SellItem p:
                if (p.Success) { NotifyItemMessage($"出售成功：{ItemName(p.UniqueID)} ×{p.Count}。", ChatType.System2); Remove(p.UniqueID, p.Count); }
                else NotifyMessage("出售失败，请检查物品限制、数量及 NPC 距离。", ChatType.Hint);
                break;
            case S.DropItem p:
                if (p.Success) { NotifyItemMessage($"丢弃成功：{ItemName(p.UniqueID)} ×{p.Count}。", ChatType.System2); Remove(p.UniqueID, p.Count); }
                else NotifyMessage("丢弃失败，请检查地图、物品绑定及角色状态。", ChatType.Hint);
                break;
            case S.EquipItem p:
                if (p.Success && p.Grid == MirGridType.Inventory && User != null)
                {
                    var from = Array.FindIndex(User.Inventory, item => item?.UniqueID == p.UniqueID);
                    if (from >= 0 && p.To >= 0 && p.To < User.Equipment.Length)
                        (User.Inventory[from], User.Equipment[p.To]) = (User.Equipment[p.To], User.Inventory[from]);
                    RefreshLooks(); InventoryRevision++; NotifyItemMessage($"穿戴成功：{ItemName(p.UniqueID)}。", ChatType.System2);
                }
                else NotifyMessage("装备失败，请检查等级、职业与负重。", ChatType.Hint);
                break;
            case S.RemoveItem p:
                if (p.Success && p.Grid == MirGridType.Inventory && User != null)
                {
                    var from = Array.FindIndex(User.Equipment, item => item?.UniqueID == p.UniqueID);
                    if (from >= 0 && p.To >= 0 && p.To < User.Inventory.Length)
                    { User.Inventory[p.To] = User.Equipment[from]; User.Equipment[from] = null; }
                    RefreshLooks(); InventoryRevision++; NotifyItemMessage($"卸下成功：{ItemName(p.UniqueID)}。", ChatType.System2);
                }
                else NotifyMessage("卸下失败，请检查背包空位、负重及当前状态。", ChatType.Hint);
                break;
            case S.UserSlotsRefresh p:
                if (User != null)
                {
                    if (p.Inventory != null) { User.Inventory = p.Inventory; if (Hero != null) Hero.Inventory = User.Inventory; Bind(p.Inventory); }
                    if (p.Equipment != null) { User.Equipment = p.Equipment; Bind(p.Equipment); }
                    RefreshLooks(); InventoryRevision++;
                }
                break;
            case S.RefreshItem p:
                Bind(p.Item);
                if (User != null) foreach (var array in new[] { User.Inventory, User.Equipment, User.QuestInventory, Storage })
                { var index = Array.FindIndex(array, item => item?.UniqueID == p.Item.UniqueID); if (index >= 0) array[index] = p.Item; }
                if (Hero != null) foreach (var array in new[] { Hero.Inventory, Hero.Equipment })
                { var index = Array.FindIndex(array, item => item?.UniqueID == p.Item.UniqueID); if (index >= 0) { array[index] = p.Item; SocialRevision++; } }
                InventoryRevision++; break;
            case S.DuraChanged p:
                var worn = User?.Inventory.Concat(User.QuestInventory).Concat(User.Equipment).Concat(Hero?.Equipment ?? []).FirstOrDefault(i => i?.UniqueID == p.UniqueID);
                if (worn != null) worn.CurrentDura = p.CurrentDura;
                InventoryRevision++; break;
            case S.ItemRepaired p:
                var repaired = User?.Inventory.Concat(User.QuestInventory).Concat(User.Equipment).Concat(Hero?.Equipment ?? []).FirstOrDefault(i => i?.UniqueID == p.UniqueID);
                if (repaired != null) { repaired.CurrentDura = p.CurrentDura; repaired.MaxDura = p.MaxDura; NotifyItemMessage($"修理成功：{repaired.Info?.FriendlyName}，耐久 {p.CurrentDura / 1000f:0.###}/{p.MaxDura / 1000f:0.###}。", ChatType.System2); }
                InventoryRevision++; break;
            case S.MoveItem p:
                var grid = p.Grid == MirGridType.Storage ? Storage : p.Grid == MirGridType.Inventory ? User?.Inventory : null;
                if (p.Success && ValidSlot(grid, p.From) && ValidSlot(grid, p.To))
                    (grid![p.From], grid[p.To]) = (grid[p.To], grid[p.From]);
                InventoryRevision++; break;
            case S.MergeItem p: ApplyMerge(p); break;
            case S.SplitItem1 p when p.Grid is MirGridType.Storage or MirGridType.Inventory:
                var split=(p.Grid == MirGridType.Storage ? Storage : User?.Inventory)?.FirstOrDefault(item=>item?.UniqueID==p.UniqueID);
                if(p.Success && split!=null && p.Count>0 && p.Count<split.Count)split.Count-=p.Count;
                InventoryRevision++;break;
            case S.SplitItem p when p.Grid is MirGridType.Storage or MirGridType.Inventory:
                Bind(p.Item);
                var splitGrid = p.Grid == MirGridType.Storage ? Storage : User?.Inventory;
                if(p.Item!=null && splitGrid != null && !splitGrid.Any(item=>item?.UniqueID==p.Item.UniqueID))
                {
                    var capacity = p.Grid == MirGridType.Storage ? StorageCapacity : splitGrid.Length;
                    // 拆分不能调用AddItem重新合堆，按原服护符/药品快捷槽优先，再找普通格。
                    var belt = p.Grid != MirGridType.Inventory ? -1 : p.Item.Info?.Type == ItemType.Amulet ? 4 :
                        p.Item.Info?.Type is ItemType.Potion or ItemType.Scroll || p.Item.Info?.Type == ItemType.Script && p.Item.Info.Effect == 1 ? 0 : -1;
                    var slot = belt < 0 ? -1 : Enumerable.Range(belt, Math.Max(0, Math.Min(belt == 0 ? 4 : 6, capacity) - belt)).FirstOrDefault(i => splitGrid[i] == null, -1);
                    if(slot<0)slot=Enumerable.Range(0,capacity).OrderBy(i=>i<6).FirstOrDefault(i=>splitGrid[i]==null,-1);
                    if(slot>=0)splitGrid[slot]=p.Item;
                }
                InventoryRevision++;break;
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
                if (p.Success) Transfer(p.From < 0 ? User?.QuestInventory : User?.Inventory, Storage, p.From < 0 ? ~p.From : p.From, p.To);
                else NotifyMessage("存入失败，请检查仓库解锁、物品限制及格位。", ChatType.Hint);
                InventoryRevision++; break;
            case S.TakeBackItem p:
                if (p.Success && p.To == -1 && ValidSlot(Storage, p.From)) Storage[p.From] = null!;
                else if (p.Success) Transfer(Storage, User?.Inventory, p.From, p.To);
                else NotifyMessage("取出失败，请检查仓库解锁、背包空位及负重。", ChatType.Hint);
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
                var questCount = p.Item.Count;
                if (User != null && p.Item.Info != null && !Stack(p.Item, User.QuestInventory))
                { var slot = Array.IndexOf(User.QuestInventory, null); if (slot >= 0) User.QuestInventory[slot] = p.Item; }
                NotifyItemMessage($"材料物品：{p.Item.Info?.FriendlyName ?? "物品"} ×{questCount}，已放入材料物品栏。", ChatType.System2);
                InventoryRevision++; break;
            case S.DeleteQuestItem p:
                RemoveFrom(User?.QuestInventory, p.UniqueID, p.Count); InventoryRevision++; break;
            case S.NewMagic p:
                if (User != null && !p.Hero) { p.Magic.CastTime += Environment.TickCount64; User.Magics.Add(p.Magic); NotifyMessage($"学会技能：{p.Magic.Name}。", ChatType.System2); }
                break;
            case S.RemoveMagic p when User != null && (uint)p.PlaceId < User.Magics.Count:
                var removedSpell = User.Magics[p.PlaceId].Spell;
                User.Magics.RemoveAt(p.PlaceId); ActiveSkills.Remove(removedSpell); DisabledSkills.Remove(removedSpell);
                break;
            case S.MagicDelay p:
                if (p.ObjectID != User?.ObjectID) break;
                var delayed = User?.Magics.FirstOrDefault(m => m.Spell == p.Spell);
                if (delayed != null) delayed.Delay = p.Delay;
                break;
            case S.MagicLeveled p:
                if (p.ObjectID != User?.ObjectID) break;
                var magic = User?.Magics.FirstOrDefault(m => m.Spell == p.Spell);
                if (magic != null) { magic.Level = p.Level; magic.Experience = p.Experience; }
                break;
            case S.SpellToggle p:
                if (p.ObjectID == User?.ObjectID) { if (p.CanUse) ActiveSkills.Add(p.Spell); else ActiveSkills.Remove(p.Spell); }
                break;
            case S.NPCUpdate p: NpcId = p.NPCID; break;
            case S.Chat p:
                if (p.Type is ChatType.System or ChatType.System2 && p.Message.StartsWith("购买成功：", StringComparison.Ordinal)) NotifyItemMessage(p.Message);
                else NotifyMessage(p.Message, p.Type); break;
            case S.ObjectChat p: NotifyMessage(p.Text, p.Type); break;
            case S.LogOutSuccess p: ClearCharacter(); Characters = p.Characters; break;
            case S.ReturnToLogin: ClearCharacter(); Characters.Clear(); break;
            case S.Disconnect: ClearCharacter(); break;
        }
        ApplySpellState(packet);
    }
    private void ClearGroup() { GroupMembers.Clear(); GroupMaps.Clear(); GroupLocations.Clear(); GroupRevision++; }

    private void Move(uint id, Point point, MirDirection direction, MirAction action) => Actors.GetValueOrDefault(id)?.Move(point, direction, action);
    private void SetActorShown(uint id, bool show)
    {
        if (!Actors.TryGetValue(id, out var actor)) return;
        if (!actor.IsShinsu) { actor.Hidden = !show; return; }
        if (actor.Dead) return;
        actor.Image = (int)(show ? Monster.Shinsu1 : Monster.Shinsu);
        actor.Move(actor.Location, actor.Direction, show ? MirAction.Show : MirAction.Hide);
    }
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
    private string ItemName(ulong id) => User?.Inventory.Concat(User.QuestInventory).Concat(User.Equipment).Concat(Hero?.Equipment ?? []).FirstOrDefault(item => item?.UniqueID == id)?.Info?.FriendlyName ?? "物品";
    private void Remove(ulong id, int count)
    {
        if (User == null) return;
        foreach (var array in new[] { User.Inventory, User.QuestInventory, User.Equipment, Hero?.Equipment, TradeItems }) RemoveFrom(array, id, count);
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
        (!ClientQuestInfo.IsMengchongDaily(quest.Type, quest.Group) || !QuestDefinitions.Values.Any(q =>
            ClientQuestInfo.IsMengchongDaily(q.Type, q.Group) && q.Group == quest.Group && (Quests.ContainsKey(q.Index) || CompletedQuests.Contains(q.Index)))) &&
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
        // shortcut: 展示仍省略套装和部分被动公式；伤害与穿戴合法性由原服务端决定。
        var stats = new Stats();
        if (User == null) return stats;
        foreach (var stat in BaseStats?.Stats ?? []) stats[stat.Type] = stat.Calculate(User.Class, User.Level);
        foreach (var item in User.Equipment.Where(item => item?.Info != null)) { stats.Add(item.Info.Stats); stats.Add(item.AddedStats); }
        stats[Stat.MagicBlood] = 0; // 储量属于物品，不计入人物战斗属性。
        foreach (var buff in UserBuffs.Values.Where(buff => !buff.Paused && buff.Stats != null)) stats.Add(buff.Stats);
        stats[Stat.HP] += stats[Stat.HP] * stats[Stat.HPRatePercent] / 100;
        stats[Stat.MP] += stats[Stat.MP] * stats[Stat.MPRatePercent] / 100;
        return stats;
    }
}
