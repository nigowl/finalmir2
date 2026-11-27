using Crystal.Native;
using Server.MirDatabase;
using Server.MirObjects;
using Server.MirEnvir;
using Server.MirNetwork;
using S = ServerPackets;

internal static class HeroInventoryChecks
{
    private sealed class Player : PlayerObject
    {
        public readonly List<Packet> Packets = [];
        public override void Enqueue(Packet packet) => Packets.Add(packet);
    }

    private sealed class Hero : HeroObject
    {
        public Hero(HeroInfo info, Player owner) : base(info, owner) { }
        protected override void Load(CharacterInfo info, MirConnection connection)
        { Info = info; HInfo = (HeroInfo)info; Stats = new Stats(); }
        protected override void SendHealthChanged() => Owner.Enqueue(new S.HeroHealthChanged { HP = HP, MP = MP });
        public override void Revive(int hp, bool effect) { Dead = false; HP = hp; MP = Stats[Stat.MP]; }
        public void Stone() => ProcessMagicBloodStone();
        public void Potions() { AutoPotTime = long.MinValue; ProcessAutoPot(); }
    }

    public static void Run(Action<bool, string> check)
    {
        UserItem Item(ulong id, ushort count = 1) => new(new ItemInfo { Index = 88010, Name = "普通药品", Type = ItemType.Potion, StackSize = 99 }) { UniqueID = id, Count = count };
        var hero = new HeroInfo(new ClientPackets.NewHero { Name = "旧英雄" });
        var old = Item(100, 7); var second = Item(101);
        hero.Inventory[0] = old; hero.Inventory[9] = second;
        var owner = new CharacterInfo { Inventory = Enumerable.Range(0, 8).Select(i => Item((ulong)i + 1)).ToArray() };
        var before = owner.Inventory.ToArray();
        check(Server.Helpers.HeroInventory.Restore(owner, hero).Length == 2 && hero.Inventory.Length == 0 && owner.Inventory.Length == 10 &&
            owner.Inventory.Take(8).SequenceEqual(before) && owner.Inventory[8] == old && old.Count == 7 && owner.Inventory[9] == second,
            "满包迁移丢失旧英雄编号、数量或覆盖主人物品");
        check(Server.Helpers.HeroInventory.Restore(owner, hero).Length == 0 && owner.Inventory.Length == 10, "重复迁移复制资产或重复扩包");
        hero.Inventory = [old];
        var duplicateRejected = false;
        try { Server.Helpers.HeroInventory.Restore(owner, hero); } catch (InvalidDataException) { duplicateRejected = true; }
        check(duplicateRejected && hero.Inventory[0] == old && owner.Inventory[8] == old, "重复编号未阻断或部分修改旧数据");

        var player = new Player { Info = new CharacterInfo(), Stats = new Stats() };
        var actor = new Hero(new HeroInfo(new ClientPackets.NewHero { Name = "共用装备" }) { HP = 10, MP = 10, Inventory = [] }, player)
            { CurrentMap = new Map(new MapInfo()), Stats = new Stats { [Stat.HP] = 100, [Stat.MP] = 100 } };
        var stone = new UserItem(new ItemInfo { Type = ItemType.Torch, Stats = new Stats { [Stat.MagicBlood] = 100 } }) { UniqueID = 300 };
        actor.Info.Equipment[3] = stone; actor.Stone();
        check(actor.HP == 40 && actor.MP == 40 && MagicBloodStone.Remaining(stone) == 40 &&
            player.Packets.OfType<S.RefreshItem>().Single().Item.UniqueID == 300 && actor.Info.Inventory.Length == 0,
            "英雄普通魔血石没有恢复本人血蓝、真实扣储量或误读主人背包");
        var medicine = Item(301, 4);
        medicine.Info.Shape = 1; medicine.Info.RequiredClass = RequiredClass.Warrior; medicine.Info.RequiredGender = RequiredGender.Male;
        medicine.Info.Stats[Stat.HP] = 20; player.Info.Inventory = [medicine];
        actor.UseItem(medicine.UniqueID);
        check(actor.HP == 60 && player.HP == 0 && medicine.Count == 3 && player.Packets.OfType<S.UseItem>().Last().Success,
            "英雄没有从主人背包取药或错误增加主人生命");
        actor.CurrentMap.Info.NoDrug = true; actor.UseItem(medicine.UniqueID);
        check(medicine.Count == 3 && !player.Packets.OfType<S.UseItem>().Last().Success, "英雄使用共享背包绕过禁药地图");
        actor.CurrentMap.Info.NoDrug = false; medicine.Info.Bind = BindMode.NoHero; actor.UseItem(medicine.UniqueID);
        check(medicine.Count == 3 && !player.Packets.OfType<S.UseItem>().Last().Success, "共享背包绕过NoHero限制");
        medicine.Info.Bind = BindMode.None; medicine.Info.Type = ItemType.Scroll; medicine.Info.Shape = 6; actor.Dead = true;
        actor.UseItem(medicine.UniqueID);
        check(!actor.Dead && actor.HP == 100 && medicine.Count == 2 && player.Packets.OfType<S.UseItem>().Last().Success,
            "共用背包复活卷不能作用于阵亡英雄");
        var declined = new UserItem(new ItemInfo { Type = ItemType.Weapon, RequiredClass = RequiredClass.Wizard, RequiredGender = RequiredGender.Male });
        actor.Info.Class = MirClass.Archer;
        check(!actor.CanEquipItem(declined, 0), "弓箭英雄绕过普通装备职业要求");
        var bow = (ArcherHero)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ArcherHero));
        bow.Info = new HeroInfo(new ClientPackets.NewHero { Name = "弓箭手", Class = MirClass.Archer }) { Level = 50 };
        bow.Info.Equipment[0] = new UserItem(new ItemInfo { Type = ItemType.Weapon, Shape = 200, RequiredClass = (RequiredClass)31 }) { CurrentDura = 1000 };
        check(bow.HasClassWeapon, "弓箭英雄没有识别角色通用的原造型弓");
        bow.Info.Equipment[0].Info.Shape = 1;
        check(!bow.HasClassWeapon, "弓箭英雄把普通剑当成弓");
        AutoPotions(check, player);

        var state = new GameState(); var potion = Item(200, 4); var weapon = new UserItem(new ItemInfo { Type = ItemType.Weapon }) { UniqueID = 201 };
        state.Apply(new S.UserInformation { ObjectID = 1, Inventory = [potion, weapon, null], Equipment = new UserItem[14], QuestInventory = [], Magics = [new() { Spell = Spell.FireBall, Level = 2, Experience = 70 }] });
        state.Apply(new S.HeroInformation { ObjectID = 2, Inventory = [Item(999)], Equipment = new UserItem[14] });
        check(ReferenceEquals(state.Hero!.Inventory, state.User!.Inventory) && !state.Hero.Inventory.Any(item => item?.UniqueID == 999), "英雄建立了第二份背包真相");
        state.Apply(new S.EquipItem { Grid = MirGridType.HeroInventory, UniqueID = 201, To = 0, Success = true });
        check(state.Hero.Equipment[0] == weapon && state.User.Inventory[1] == null && state.User.Equipment[0] == null, "英雄穿戴误改角色装备");
        state.Apply(new S.DuraChanged { UniqueID = 201, CurrentDura = 2000 });
        check(weapon.CurrentDura == 2000, "英雄装备耐久没有归约");
        state.Apply(new S.UseItem { Grid = MirGridType.HeroInventory, UniqueID = 200, Success = true });
        check(potion.Count == 3 && state.Hero.Inventory[0].Count == 3, "共享背包用药被重复扣除");
        state.Apply(new S.NewMagic { Hero = true, Magic = new() { Spell = Spell.FireBall, Experience = 4 } });
        state.Apply(new S.MagicLeveled { ObjectID = 2, Spell = Spell.FireBall, Level = 1, Experience = 12 });
        check(state.Hero.Magics.Single().Level == 1 && state.Hero.Magics.Single().Experience == 12 && state.User.Magics.Single().Experience == 70,
            "普通技能学习或熟练度覆盖了角色技能");
        state.Apply(new S.RemoveItem { Grid = MirGridType.HeroInventory, UniqueID = 201, To = 2, Success = true });
        check(state.User.Inventory[2] == weapon && state.Hero.Equipment[0] == null, "卸下没有进入同一背包");
        state.Apply(new S.UserSlotsRefresh { Inventory = [potion, weapon, null, null] });
        check(ReferenceEquals(state.Hero.Inventory, state.User.Inventory) && state.Hero.Inventory.Length == 4, "完整刷新后共享背包别名失效");
        state.Apply(new S.DropItem { HeroItem = true, UniqueID = 200, Count = 1, Success = true });
        check(potion.Count == 2, "英雄上下文丢弃未按原回执扣除共用背包");
        state.Apply(new S.DeleteItem { UniqueID = 201, Count = 1 });
        check(state.User.Inventory[1] == null && state.Hero.Inventory[1] == null, "共享物品删除产生残留");
        state.Apply(new S.LogOutSuccess());
        check(state.Hero == null && state.User == null, "退出后保留上一角色的英雄背包");
    }

    private static void AutoPotions(Action<bool, string> check, Player player)
    {
        var actor = new Hero(new HeroInfo(new ClientPackets.NewHero()) { HP = 10, MP = 10, Inventory = [] }, player)
            { CurrentMap = new Map(new MapInfo()), Stats = new Stats { [Stat.HP] = 100, [Stat.MP] = 100 } };
        UserItem Potion(ulong id, Stat stat, short amount, ushort count) => new(new ItemInfo
            { Index = (int)id, Type = ItemType.Potion, Stats = new Stats { [stat] = amount } }) { UniqueID = id, Count = count };
        var red = Potion(310, Stat.HP, 30, 5); var blue = Potion(311, Stat.MP, 30, 3);
        player.Info.Inventory = [red, blue]; player.Packets.Clear();
        actor.Potions();
        check(red.Count == 4 && actor.PotHealthAmount == 30 && actor.HP == 10 && !actor.AutoPot,
            "未配置旧版药品的英雄没有自动取共用红药，或绕过原持续恢复");
        actor.Potions();
        check(red.Count == 4 && blue.Count == 2 && actor.PotManaAmount == 30, "持续回血未防止重复喝红，或没有自动补蓝");
        actor.Potions();
        check(red.Count == 4 && blue.Count == 2 && player.Packets.OfType<S.UseItem>().Count() == 2,
            "持续恢复中继续扣药或缺少英雄原编号回执");
        actor.PotHealthAmount = actor.PotManaAmount = 0;
        actor.Info.Equipment[3] = new UserItem(new ItemInfo { Type = ItemType.Torch, Stats = new Stats { [Stat.MagicBlood] = 1000 } });
        actor.HP = actor.MP = 40; actor.Potions();
        check(red.Count == 4 && blue.Count == 2, "魔血石可持续恢复时仍浪费共用药品");
        actor.HP = 20; actor.Potions();
        check(red.Count == 3 && actor.PotHealthAmount == 30, "魔血石阻止了英雄低血应急喝红");
        actor.HP = 100; actor.MP = 5; actor.Potions();
        check(blue.Count == 1 && actor.PotManaAmount == 30, "魔血石阻止了英雄低蓝应急喝蓝");
        actor.Info.Equipment[3] = null; actor.PotHealthAmount = actor.PotManaAmount = 0; actor.HP = 10;
        actor.CurrentMap.Info.NoDrug = true; actor.Potions();
        check(red.Count == 3, "英雄自动恢复绕过禁药地图");
        actor.CurrentMap.Info.NoDrug = false; red.Info.Bind = BindMode.NoHero; actor.Potions();
        check(red.Count == 3, "英雄自动恢复绕过NoHero绑定");
        red.Info.Bind = BindMode.None; red.Count = 2; actor.Potions();
        check(red.Count == 2 && blue.Count == 1, "英雄自动喝药用掉主人应急储备");
        red.Count = 3; actor.Dead = true; actor.Potions();
        check(red.Count == 3, "阵亡英雄继续自动消耗药品");
    }
}
