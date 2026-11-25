using S = ServerPackets;

namespace Crystal.Native;

public sealed partial class GameState
{
    public IReadOnlyList<ClientFriend> Friends { get; private set; } = [];
    public IReadOnlyList<ClientMail> Mail { get; private set; } = [];
    private int recoveryHeroIndex;
    private long heroReviveAt;
    public bool HeroRecoveryKnown => recoveryHeroIndex > 0 && recoveryHeroIndex == ActiveHero?.Index;
    public int HeroRecoverySeconds => HeroRecoveryKnown ? (int)Math.Max(0, (heroReviveAt - Environment.TickCount64 + 999) / 1000) : 0;
    public HeroSpawnState HeroState { get; private set; }
    public S.HeroInformation? Hero { get; private set; }
    public BaseStats? HeroBaseStats { get; private set; }
    public Dictionary<BuffType, ClientBuff> HeroBuffs { get; } = new();
    public ClientHeroInformation? ActiveHero { get; private set; }
    public ClientHeroInformation[] StoredHeroes { get; private set; } = [];
    public int MaximumHeroCount { get; private set; }
    public PetMode CurrentPetMode { get; private set; } = PetMode.Both;
    public int SocialRevision { get; private set; }

    private void ResetSocial()
    {
        Friends = []; Mail = []; Hero = null; HeroBaseStats = null; ActiveHero = null; StoredHeroes = []; MaximumHeroCount = 0;
        HeroBuffs.Clear();
        recoveryHeroIndex = 0; heroReviveAt = 0;
        HeroState = HeroSpawnState.None; CurrentPetMode = PetMode.Both; SocialRevision++;
    }

    private bool ApplySocial(Packet packet)
    {
        switch (packet)
        {
            case S.FriendUpdate p: Friends = p.Friends; break;
            case S.ReceiveMail p:
                Mail = p.Mail;
                foreach (var mail in Mail) foreach (var item in mail.Items) Bind(item);
                break;
            case S.NewIntelligentCreature p when User != null:
                User.IntelligentCreatures.RemoveAll(pet => pet.PetType == p.Creature.PetType);
                User.IntelligentCreatures.Add(p.Creature); break;
            case S.UpdateIntelligentCreatureList p when User != null:
                User.IntelligentCreatures = p.CreatureList; User.SummonedCreatureType = p.SummonedCreatureType;
                User.CreatureSummoned = p.CreatureSummoned; break;
            // 英雄信息继承角色信息，但绝不能覆盖当前玩家。
            case S.HeroRecovery p:
                recoveryHeroIndex = p.HeroIndex; heroReviveAt = Environment.TickCount64 + p.Milliseconds; break;
            case S.HeroInformation p:
                Hero = p; HeroBuffs.Clear(); Hero.Inventory = User?.Inventory ?? [];
                foreach (var magic in p.Magics) magic.CastTime += Environment.TickCount64;
                Bind(p.Equipment); break;
            case S.HeroBaseStatsInfo p: HeroBaseStats = p.Stats; break;
            case S.ManageHeroes p:
                ActiveHero = p.CurrentHero; MaximumHeroCount = p.MaximumCount;
                if (p.Heroes != null) StoredHeroes = p.Heroes;
                if (User != null) User.HasHero = ActiveHero != null;
                break;
            case S.NewHeroInfo p when (uint)p.StorageIndex < StoredHeroes.Length: StoredHeroes[p.StorageIndex] = p.Info; break;
            case S.ChangeHero p when (uint)p.FromIndex < StoredHeroes.Length:
                (ActiveHero, StoredHeroes[p.FromIndex]) = (StoredHeroes[p.FromIndex], ActiveHero!); Hero = null; HeroBaseStats = null; HeroBuffs.Clear(); break;
            case S.ChangePMode p when Enum.IsDefined(p.Mode): CurrentPetMode = p.Mode; break;
            case S.UpdateHeroSpawnState p when User != null:
                HeroState = p.State; User.HasHero = p.State != HeroSpawnState.None;
                if (p.State == HeroSpawnState.None) { Hero = null; HeroBaseStats = null; HeroBuffs.Clear(); }
                break;
            case S.SetHeroBehaviour p when User != null: User.HeroBehaviour = p.Behaviour; break;
            case S.HeroLevelChanged p when Hero != null: Hero.Level = p.Level; Hero.Experience = p.Experience; Hero.MaxExperience = p.MaxExperience; break;
            case S.GainHeroExperience p when Hero != null: Hero.Experience += p.Amount; break;
            case S.HeroHealthChanged p when Hero != null: Hero.HP = p.HP; Hero.MP = p.MP; break;
            case S.TransferHeroItem: case S.TakeBackHeroItem: break;
            case S.EquipItem { Grid: MirGridType.HeroInventory } p:
                MoveHeroItem(User?.Inventory, Hero?.Equipment, HeroItemSlot(User?.Inventory, p.UniqueID), p.To, p.Success, "英雄穿戴", true); break;
            case S.RemoveItem { Grid: MirGridType.HeroInventory } p:
                MoveHeroItem(Hero?.Equipment, User?.Inventory, HeroItemSlot(Hero?.Equipment, p.UniqueID), p.To, p.Success, "英雄卸下"); break;
            case S.NewMagic { Hero: true } p when Hero != null:
                p.Magic.CastTime += Environment.TickCount64;
                Hero.Magics.RemoveAll(magic => magic.Spell == p.Magic.Spell); Hero.Magics.Add(p.Magic);
                NotifyMessage($"英雄学会技能：{p.Magic.Name}。", ChatType.System2); break;
            case S.MagicLeveled p when Hero != null && p.ObjectID == Hero.ObjectID:
                var learned = Hero.Magics.FirstOrDefault(magic => magic.Spell == p.Spell);
                if (learned != null) { learned.Level = p.Level; learned.Experience = p.Experience; }
                break;
            case S.MagicDelay p when Hero != null && p.ObjectID == Hero.ObjectID:
                var delayed = Hero.Magics.FirstOrDefault(magic => magic.Spell == p.Spell);
                if (delayed != null) delayed.Delay = p.Delay;
                break;
            default: return false;
        }
        SocialRevision++; return true;
    }

    public Stats CalculateHeroStats()
    {
        // shortcut: 血蓝上限复用基础公式、装备和Buff；特殊套装/被动须在完整属性计算接入后对齐。
        var stats = new Stats(); if (Hero == null) return stats;
        foreach (var stat in HeroBaseStats?.Stats ?? []) stats[stat.Type] = stat.Calculate(Hero.Class, Hero.Level);
        foreach (var item in Hero.Equipment.Where(item => item?.Info != null)) { stats.Add(item.Info.Stats); stats.Add(item.AddedStats); }
        foreach (var buff in HeroBuffs.Values) stats.Add(buff.Stats);
        stats[Stat.HP] += stats[Stat.HP] * stats[Stat.HPRatePercent] / 100;
        stats[Stat.MP] += stats[Stat.MP] * stats[Stat.MPRatePercent] / 100;
        return stats;
    }

    private static int HeroItemSlot(UserItem[]? slots, ulong id) => slots == null ? -1 : Array.FindIndex(slots, item => item?.UniqueID == id);

    private void MoveHeroItem(UserItem[]? source, UserItem[]? target, int from, int to, bool success, string action, bool swap = false)
    {
        if (!success) { NotifyMessage($"{action}失败，请检查英雄状态、负重和装备条件。", ChatType.Hint); return; }
        if (source == null || target == null || (uint)from >= source.Length || (uint)to >= target.Length || source[from] is not { } item || (!swap && target[to] != null)) return;
        (source[from], target[to]) = (target[to], item);
        InventoryRevision++; NotifyItemMessage($"{action}：{ItemPresentation.Name(item)}。", ChatType.System2);
    }
}
