using System.Drawing;
using System.Text.Json;
using Mir2.Hosting;
using Server;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class ClassBalanceAudit
{
    private sealed class Player : PlayerObject
    {
        public override void Enqueue(Packet packet) { }
        public override void Broadcast(Packet packet) { }
    }

    internal static void Run(ProjectPaths paths, string output)
    {
        if (!paths.Data.Contains("/mir2-balance.", StringComparison.Ordinal)) throw new InvalidOperationException("仅允许隔离平衡快照。");
        using var gate = new FileStream(Path.Combine(paths.Data, ".server.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Directory.SetCurrentDirectory(paths.World);
        using var storage = new SqliteDatabaseStorage(paths.Database); DatabaseStorage.Current = storage;
        Settings.Load(); Settings.LoadBaseStats();
        var world = Envir.Main; if (!world.LoadDB()) throw new InvalidDataException("读取平衡快照失败。");
        world.BuffInfoList.AddRange(BuffInfo.Load());
        foreach (var file in Directory.GetFiles(Settings.RecipePath, "*.txt"))
            world.RecipeInfoList.Add(new RecipeInfo(Path.GetFileNameWithoutExtension(file)));
        var map = new Map(new MapInfo { Index = -971 }) { Width = 30, Height = 30, Cells = new Cell[30, 30] };
        for (var y = 0; y < 30; y++) for (var x = 0; x < 30; x++) map.Cells[x, y] = new Cell { Attribute = CellAttribute.Walk };
        void Place(MapObject actor, Point point)
        { actor.CurrentMap = map; actor.CurrentLocation = point; actor.Node = new LinkedListNode<MapObject>(actor); map.GetCell(point).Add(actor); }
        var rows = new List<object>();
        foreach (var level in new ushort[] { 10, 20, 30, 40 })
        foreach (var job in Enum.GetValues<MirClass>())
        {
            var player = new Player { Info = new CharacterInfo { Class = job, Gender = MirGender.Male, Level = level, Name = "平衡采样", HP = 10000, MP = 10000 }, Account = new AccountInfo(),
                Stats = new Stats(), HasUpdatedBaseStats = true, ArmourRate = 1, DamageRate = 1 };
            player.Info.Mount = new MountInfo(player); Place(player, new(10, 10)); player.RefreshStats();
            var stat = job is MirClass.Wizard or MirClass.Archer ? Stat.MaxMC : job == MirClass.Taoist ? Stat.MaxSC : Stat.MaxDC;
            var weapon = world.ItemInfoList.Where(item => item.Type == ItemType.Weapon && !item.IsFishingRod && item.Price is > 0 and <= 100000 &&
                (job != MirClass.Archer || Functions.GetRealItem(item, level, job, world.ItemInfoList).Shape / Globals.ClassWeaponCount == 2) &&
                player.CanEquipItem(new UserItem(item), 0)).OrderByDescending(item => item.Stats[stat]).ThenBy(item => item.Price).FirstOrDefault();
            if (weapon != null) player.Info.Equipment[0] = new UserItem(weapon) { UniqueID = 971001, CurrentDura = weapon.Durability, MaxDura = weapon.Durability };
            foreach (var magic in world.MagicInfoList.Where(magic => magic.Level1 <= level && world.ItemInfoList.Any(book => book.Type == ItemType.Book &&
                book.Shape == (int)magic.Spell && book.RequiredClass.HasFlag((RequiredClass)(1 << (int)job)) &&
                book.RequiredGender.HasFlag(RequiredGender.Male) && (book.RequiredType != RequiredType.Level || book.RequiredAmount <= level))))
                player.Info.Magics.Add(new UserMagic(magic.Spell) { Level = (byte)(level >= magic.Level3 ? 3 : level >= magic.Level2 ? 2 : 0) });
            player.RefreshStats();
            var target = new MonsterObject(new MonsterInfo { Level = 1, AI = 0 }) { HP = 1000000,
                Stats = new Stats { [Stat.HP] = 1000000 }, ArmourRate = 1, DamageRate = 1 };
            Place(target, new(11, 10));
            object Sample(Spell spell, int distance, int defence)
            {
                map.GetCell(target.CurrentLocation).Remove(target); target.CurrentLocation = new(10 + distance, 10); map.GetCell(target.CurrentLocation).Add(target);
                target.Stats[Stat.MinAC] = target.Stats[Stat.MaxAC] = target.Stats[Stat.MinMAC] = target.Stats[Stat.MaxMAC] = defence;
                long damage = 0, mana = 0; var hits = 0; const int count = 400;
                for (var i = 0; i < count; i++)
                {
                    player.ActionTime = player.AttackTime = player.SpellTime = 0; player.MP = player.Stats[Stat.MP]; target.HP = 1000000;
                    if (player.Info.Equipment[0] is { } worn) worn.CurrentDura = worn.MaxDura;
                    foreach (var magic in player.Info.Magics) magic.CastTime = -100000;
                    player.ActionList.Clear(); var mp = player.MP;
                    if (spell is Spell.DoubleSlash or Spell.Slaying)
                    { player.Slaying = spell == Spell.Slaying; player.Attack(MirDirection.Right, spell); }
                    else if (spell != Spell.None) player.Magic(spell, MirDirection.Right, target.ObjectID, target.CurrentLocation);
                    else if (job == MirClass.Archer) player.RangeAttack(MirDirection.Right, target.CurrentLocation, target.ObjectID);
                    else player.Attack(MirDirection.Right, Spell.None);
                    for (var step = 0; player.ActionList.Count > 0 && step < 8; step++)
                        foreach (var action in player.ActionList.ToArray()) { player.ActionList.Remove(action); player.Process(action); }
                    var dealt = 1000000 - target.HP; damage += dealt; mana += mp - player.MP; if (dealt > 0) hits++;
                }
                var interval = spell is Spell.None or Spell.Slaying or Spell.DoubleSlash ? player.AttackSpeed : Math.Max(Settings.PlayerTiming.CastDelay(spell), player.GetMagic(spell).GetDelay());
                return new { Spell = spell.ToString(), Distance = distance, Defence = defence, Samples = count,
                    HitPercent = hits * 100.0 / count, DamagePerAction = damage / (double)count, ManaPerAction = mana / (double)count,
                    Interval = interval, EstimatedDps = Math.Round(damage * 1000.0 / count / interval, 2) };
            }
            var samples = new List<object> { Sample(Spell.None, 1, 0), Sample(Spell.None, 1, level / 5) };
            if (job == MirClass.Archer) foreach (var range in new[] { 3, 6, 9 }) samples.Add(Sample(Spell.None, range, level / 5));
            var choices = job switch { MirClass.Wizard => new[] { Spell.ThunderBolt, Spell.GreatFireBall, Spell.FireBall },
                MirClass.Taoist => new[] { Spell.SoulFireBall }, MirClass.Archer => new[] { Spell.DoubleShot, Spell.StraightShot },
                MirClass.Assassin => new[] { Spell.DoubleSlash }, _ => new[] { Spell.Slaying } };
            var selected = choices.FirstOrDefault(spell => player.Info.Magics.Any(magic => magic.Spell == spell));
            if (selected != Spell.None)
            {
                if (job == MirClass.Taoist) player.Info.Equipment[5] = new UserItem(new ItemInfo { Type = ItemType.Amulet, Shape = 0 }) { Count = 2000 };
                samples.Add(Sample(selected, job is MirClass.Assassin or MirClass.Warrior ? 1 : 6, level / 5));
            }
            rows.Add(new { Level = level, Class = job.ToString(), Weapon = weapon?.Name, WeaponIndex = weapon?.Index,
                HP = player.Stats[Stat.HP], MP = player.Stats[Stat.MP], DC = new[] { player.Stats[Stat.MinDC], player.Stats[Stat.MaxDC] },
                MC = new[] { player.Stats[Stat.MinMC], player.Stats[Stat.MaxMC] }, SC = new[] { player.Stats[Stat.MinSC], player.Stats[Stat.MaxSC] }, Samples = samples });
            map.GetCell(player.CurrentLocation).Remove(player); map.GetCell(target.CurrentLocation).Remove(target);
        }
        File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(output + ".materials.json", JsonSerializer.Serialize(world.QuestInfoList.SelectMany(q => q.CarryItems.Concat(q.ItemTasks)).Select(t => t.Item)
            .Concat(world.RecipeInfoList.SelectMany(r => r.Ingredients ?? []).Select(i => i.Info)).Distinct().Select(i => new { i.Index, i.Name, Type = i.Type.ToString() }), new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"PASS {rows.Count} current-version class/level samples: real HP/MP, legal weapon definitions and original damage handlers; estimates exclude movement, summons, AoE and long-fight mana.");
    }
}
