using Crystal.Native;
using Mir2.Companions;
using C = ClientPackets;
using S = ServerPackets;

namespace Mir2.Companion;

public sealed partial class CompanionSession
{
    private bool[]? heroClasses;
    private bool heroCreated, heroCreationBlocked;
    private int heroNameAttempts;
    private long heroRecoveryRequested;
    private HeroBehaviour requestedHeroMode;
    private readonly HashSet<ulong> heroRefused = [];

    private async Task HeroPacket(Packet packet)
    {
        if(pendingAsset=="hero-mode"&&packet is S.UpdateHeroSpawnState{State:not HeroSpawnState.Summoned})
        {pendingAsset=null;await report(new("hero-mode-cancelled",Profile.Id,Reason:"英雄已退场，取消本次战斗模式等待"));}
        if (packet is S.HeroCreateRequest request && Knowledge?.Goal is { } goal &&
            goal.Key.StartsWith("hero-create/", StringComparison.Ordinal) && goal.NpcId == npcId)
        { heroClasses = request.CanCreateClass; pendingNpc = 0; }
        if (packet is S.HeroLevelChanged) heroRefused.Clear();
        if (packet is not S.NewHero result || pendingAsset != "hero-create") return;
        pendingAsset = null;
        if (result.Result == 10)
        {
            heroCreated = true;
            await report(new("asset", Profile.Id, Text: "hero-create:成功"));
            return;
        }
        await report(new("hero-rejected", Profile.Id, Reason: $"原服拒绝创建英雄（{result.Result}）"));
        // 只有原服明确重名才换名重试，未知结果和其他拒绝不重复创建。
        if (result.Result == 5 && heroNameAttempts < 3) return;
        heroCreationBlocked = true;
        if (Knowledge?.Goal is { } rejected)
            await report(new("goal-blocked", Profile.Id, Reason: $"英雄创建被拒绝（{result.Result}），保留角色并继续其他玩法", Text: rejected.Key));
    }

    private async Task<bool> MaintainHero(long now)
    {
        if (Knowledge == null || state.Trading || state.StorageActive || repairItem != 0 || preparedSkill != null || pendingSkill != Spell.None ||
            now - monsterAttackedAt < 5000 || now < aggressorUntil) return false;
        var user = state.User!;
        if (!user.HasHero && state.ActiveHero == null && state.Hero == null) return await CreateFirstHero();
        if (Knowledge.NoHero) return false;
        if (state.ActiveHero != null && !state.HeroRecoveryKnown)
        {
            if (heroRecoveryRequested == 0)
            { heroRecoveryRequested = now; await connection.Send(new C.HeroRecoveryRequest()); return true; }
            if (now - heroRecoveryRequested > 5000) throw new TimeoutException("英雄恢复状态未确认，停止自动召唤。");
        }
        if (state.HeroState is HeroSpawnState.None or HeroSpawnState.Unsummoned or HeroSpawnState.Dead && state.HeroRecoveryKnown && state.HeroRecoverySeconds == 0)
        {
            await Status("召唤英雄", "恢复等待已结束，通过原命令召唤英雄");
            await Asset(new C.Chat { Message = "@SUMMONHERO" }, "hero-summon", 0);
            return true;
        }
        if (state.HeroState != HeroSpawnState.Summoned || state.Hero is not { HP: > 0 } hero) return false;
        var mode=CompanionHeroRules.CombatMode(hero.HP,state.CalculateHeroStats()[Stat.HP],user.HeroBehaviour);
        if(mode!=user.HeroBehaviour)
        {
            requestedHeroMode=mode;
            await Status("管理英雄",mode==HeroBehaviour.Follow?"英雄生命不足，先跟随恢复":"英雄状态恢复，参与正常战斗");
            await Asset(new C.SetHeroBehaviour{Behaviour=mode},"hero-mode",0);return true;
        }
        foreach (var item in user.Inventory.Where(item => item?.Info != null && !heroRefused.Contains(item.UniqueID)))
        {
            if (!CompanionHeroRules.Usable(item, hero.Class, hero.Gender, hero.Level) ||
                item.UniqueID==Knowledge.Goal?.AppraisalItemId && EquipmentAppraisal.Pending(item)) continue;
            var slot = CompanionInventoryRules.UpgradeSlot(item, hero.Equipment, hero.Class, CompanionInventoryRules.Caster(hero.Magics.Select(magic => magic.Spell)));
            if (slot >= 0 && item.CurrentDura > 0)
            {
                await Asset(new C.EquipItem { Grid = MirGridType.HeroInventory, UniqueID = item.UniqueID, To = slot }, "hero-equip", item.UniqueID);
                return true;
            }
            if (item.Info.Type == ItemType.Book && !hero.Magics.Any(magic => (int)magic.Spell == item.Info.Shape))
            {
                await Asset(new C.UseItem { Grid = MirGridType.HeroInventory, UniqueID = item.UniqueID }, "hero-use", item.UniqueID);
                return true;
            }
        }
        return false;
    }

    private async Task<bool> CreateFirstHero()
    {
        var user = state.User!;
        var seal = user.Inventory.FirstOrDefault(item => item?.Info.Type == ItemType.SealedHero &&
            item.AddedStats[Stat.Hero] > 0 && !heroRefused.Contains(item.UniqueID) && Usable(item.Info));
        if (seal != null)
        {
            await Status("英雄激活中", "使用共用背包中的封印英雄，等待原服确认");
            await Asset(new C.UseItem { Grid = MirGridType.Inventory, UniqueID = seal.UniqueID }, "hero-seal", seal.UniqueID);
            return true;
        }
        if (!heroCreated && !heroCreationBlocked && heroClasses != null &&
            Knowledge?.Goal is { } goal && goal.Key.StartsWith("hero-create/", StringComparison.Ordinal) && goal.NpcId == npcId &&
            state.Actors.GetValueOrDefault(npcId) is { Hidden: false } npc && Distance(user.Location, npc.Location) <= 4 &&
            !user.Inventory.Any(item => item?.Info.Type == ItemType.SealedHero))
        {
            var jobs = Enum.GetValues<MirClass>().Where(job => (int)job < heroClasses.Length && heroClasses[(int)job]).ToArray();
            if (jobs.Length == 0) { heroCreationBlocked = true; return false; }
            var job = jobs.Contains(user.Class) ? user.Class : jobs[0];
            var suffix = "英" + random.Next(100, 1000);
            var name = user.Name[..Math.Min(user.Name.Length, Globals.MaxCharacterNameLength - suffix.Length)] + suffix;
            heroNameAttempts++;
            await Status("英雄创建中", $"通过当前英雄使者创建{job.ToLocalizedString()}英雄");
            await Asset(new C.NewHero { Name = name, Class = job, Gender = user.Gender }, "hero-create", 0);
            return true;
        }
        return false;
    }

    private void ProtectHeroItems(ISet<int> protectedItems)
    {
        if (state.Hero is { } hero)
            protectedItems.UnionWith(CompanionHeroRules.ProtectedItems(state.User!.Inventory, hero.Class, hero.Gender, hero.Level,
                hero.Equipment, hero.Magics.Select(magic => magic.Spell).ToHashSet()));
        else if (state.ActiveHero is { } active)
            protectedItems.UnionWith(CompanionHeroRules.ProtectedItems(state.User!.Inventory, active.Class, active.Gender, active.Level, null, null));
    }

    private int HeroDevelopmentCount(UserItem item) => state.HeroState == HeroSpawnState.Summoned && state.Hero is { HP: > 0 } hero &&
        CompanionHeroRules.Usable(item, hero.Class, hero.Gender, hero.Level)
        ? CompanionInventoryRules.DevelopmentCount(item, state.User!.Inventory, hero.Equipment, hero.Class, hero.Gender, hero.Level,
            hero.Magics.Select(magic => magic.Spell).ToHashSet()) : 0;
}
