using System.Drawing;
using System.Security.Cryptography;
using Client.MirObjects;
using Mir2.Hosting;
using Mir2.Management;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.ConsoleHost;

// Called only on the game thread; management mutations are serialized by the host.
public sealed class WorldDefinitions(ProjectPaths paths, AssetCatalog assets)
{
    private static Envir World => Envir.Main;
    private static ItemInfo Item(int id) => World.ItemInfoList.SingleOrDefault(i => i.Index == id) ?? throw new FileNotFoundException("Item not found.");
    private static MonsterInfo Monster(int id) => World.MonsterInfoList.SingleOrDefault(i => i.Index == id) ?? throw new FileNotFoundException("Monster not found.");
    private static MapInfo Map(int id) => World.MapInfoList.SingleOrDefault(i => i.Index == id) ?? throw new FileNotFoundException("Map not found.");
    internal static NPCInfo Npc(int id) => World.NPCInfoList.SingleOrDefault(i => i.Index == id) ?? throw new FileNotFoundException("NPC not found.");
    private static QuestInfo Quest(int id) => World.QuestInfoList.SingleOrDefault(i => i.Index == id) ?? throw new FileNotFoundException("Quest not found.");
    private static byte[] Serialize(Action<BinaryWriter> write)
    {
        using var memory = new MemoryStream();
        using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) write(writer);
        return memory.ToArray();
    }
    private static string Version(Action<BinaryWriter> write) => Convert.ToHexString(SHA256.HashData(Serialize(write))).ToLowerInvariant();
    private static void Match(string actual, string supplied)
    {
        if (actual != supplied) throw new InvalidOperationException("Definition changed since it was opened. Reload before saving or copying.");
    }
    private static string Name(string value, bool fileSafe = false)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(c => char.IsControl(c) || (fileSafe && (c is '/' or '\\' or ':'))))
            throw new InvalidDataException("Name must contain 1–80 characters without controls or path separators.");
        return value.Trim();
    }
    private string Script(string folder, string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 160 || value.Any(char.IsControl)) throw new InvalidDataException("Invalid script name.");
        var name = value.Trim().Replace('\\', '/');
        var file = ProjectPaths.SafePath(Path.Combine(paths.World, "Envir", folder), name + ".txt");
        if (!File.Exists(file)) throw new InvalidDataException("Create the referenced script in the file editor before saving this definition.");
        return name;
    }
    private static int Next(int counter, IEnumerable<int> ids)
    {
        var highest = Math.Max(0, Math.Max(counter, ids.DefaultIfEmpty(0).Max()));
        if (highest == int.MaxValue) throw new InvalidOperationException("Definition IDs are exhausted.");
        return highest + 1;
    }
    private void Commit(string label, Action change, Action undo)
    {
        World.SaveDB();
        var folder = Path.Combine(paths.Runtime, "backups", "definitions-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + label);
        Directory.CreateDirectory(folder);
        using (var source = DatabaseStorage.OpenRead(Envir.DatabasePath))
        using (var backup = new FileStream(Path.Combine(folder, "Server.MirDB"), FileMode.CreateNew, FileAccess.Write))
        { source.CopyTo(backup); backup.Flush(true); }
        try { change(); World.SaveDB(); }
        catch { undo(); throw; }
    }
    public DefinitionEdit Read(string kind, int id) => kind switch
    {
        "items" => Read(Item(id)),
        "monsters" => Read(Monster(id)),
        _ => throw new InvalidDataException("Unknown item/monster definition kind.")
    };
    private static DefinitionEdit Read(ItemInfo i) => new("items", i.Index, i.Name, i.RequiredAmount, 0, i.Stats[Stat.HP], i.Stats[Stat.MinDC], i.Stats[Stat.MaxDC], i.Price, i.StackSize, Version(i.Save));
    private static DefinitionEdit Read(MonsterInfo m) => new("monsters", m.Index, m.Name, m.Level, m.Experience, m.Stats[Stat.HP], m.Stats[Stat.MinDC], m.Stats[Stat.MaxDC], 0, 1, Version(m.Save));
    public void Save(DefinitionEdit edit)
    {
        var before = Read(edit.Kind, edit.Id); Match(before.Version, edit.Version);
        var name = Name(edit.Name, true);
        if (edit.Level is < 0 or > 65535 || (edit.Kind == "items" && edit.Level > 255) || edit.Hp is < 0 or > 100000000 ||
            edit.MinDc < 0 || edit.MaxDc < edit.MinDc || edit.MaxDc > 1000000 || edit.StackSize is < 1 or > 65535)
            throw new InvalidDataException("Invalid definition values.");
        var duplicate = edit.Kind == "items" ? World.ItemInfoList.Any(i => i.Index != edit.Id && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) :
            World.MonsterInfoList.Any(i => i.Index != edit.Id && i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (name != before.Name && duplicate) throw new InvalidOperationException("Another definition already uses that name.");
        Commit(edit.Kind + "-" + edit.Id, () => Apply(edit with { Name = name }), () => Apply(before));
    }
    private static void Apply(DefinitionEdit edit)
    {
        if (edit.Kind == "items")
        {
            var item = Item(edit.Id);
            item.Name = edit.Name; item.RequiredAmount = (byte)edit.Level; item.Price = edit.Price; item.StackSize = (ushort)edit.StackSize;
            item.Stats[Stat.HP] = edit.Hp; item.Stats[Stat.MinDC] = edit.MinDc; item.Stats[Stat.MaxDC] = edit.MaxDc;
        }
        else
        {
            var monster = Monster(edit.Id);
            monster.Name = edit.Name; monster.Level = (ushort)edit.Level; monster.Experience = edit.Experience;
            monster.Stats[Stat.HP] = edit.Hp; monster.Stats[Stat.MinDC] = edit.MinDc; monster.Stats[Stat.MaxDC] = edit.MaxDc;
        }
    }
    public DefinitionEdit Clone(DefinitionClone request)
    {
        var before = Read(request.Kind, request.SourceId); Match(before.Version, request.Version); var name = Name(request.Name, true);
        if (request.Kind == "items")
        {
            if (World.ItemInfoList.Any(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Item name already exists.");
            var source = Item(request.SourceId);
            using var reader = new BinaryReader(new MemoryStream(Serialize(source.Save)));
            var copy = new ItemInfo(reader, Envir.Version, Envir.CustomVersion) { Index = Next(World.ItemIndex, World.ItemInfoList.Select(i => i.Index)), Name = name, StartItem = false };
            var counter = World.ItemIndex;
            Commit("items-copy-" + source.Index, () => { World.ItemInfoList.Add(copy); World.ItemIndex = copy.Index; }, () => { World.ItemInfoList.Remove(copy); World.ItemIndex = counter; });
            return Read(copy);
        }
        var monster = Monster(request.SourceId);
        if (World.MonsterInfoList.Any(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("Monster name already exists.");
        var clone = new MonsterInfo
        {
            Index = Next(World.MonsterIndex, World.MonsterInfoList.Select(i => i.Index)), Name = name, Image = monster.Image, AI = monster.AI,
            Effect = monster.Effect, ViewRange = monster.ViewRange, CoolEye = monster.CoolEye, Level = monster.Level, Light = monster.Light,
            AttackSpeed = monster.AttackSpeed, MoveSpeed = monster.MoveSpeed, Experience = monster.Experience, Stats = new Stats(monster.Stats),
            DropPath = string.IsNullOrEmpty(monster.DropPath) ? monster.Name : monster.DropPath, CanTame = monster.CanTame, CanPush = monster.CanPush,
            AutoRev = monster.AutoRev, Undead = monster.Undead, CanRecall = monster.CanRecall, IsBoss = monster.IsBoss,
            HasSpawnScript = monster.HasSpawnScript, HasDieScript = monster.HasDieScript
        };
        var oldCounter = World.MonsterIndex;
        Commit("monsters-copy-" + monster.Index, () => { World.MonsterInfoList.Add(clone); World.MonsterIndex = clone.Index; }, () => { World.MonsterInfoList.Remove(clone); World.MonsterIndex = oldCounter; });
        return Read(clone);
    }
    public MapDefinition ReadMap(int id)
    {
        var map = Map(id);
        return new(id, map.Title, map.FileName, (int)map.Light, map.Music, map.NoFight, map.NoDropPlayer, map.NoDropMonster, map.NoTeleport, Version(map.Save));
    }
    public void Save(MapDefinition edit)
    {
        var before = ReadMap(edit.Id); Match(before.Version, edit.Version); var title = Name(edit.Title);
        if (edit.Light is < 0 or > 4 || edit.Music is < 0 or > 65535) throw new InvalidDataException("Invalid map light or music.");
        if (string.IsNullOrWhiteSpace(edit.FileName) || edit.FileName.Length > 160 || edit.FileName.Any(char.IsControl)) throw new InvalidDataException("Invalid map resource name.");
        var file = edit.FileName.Trim().Replace('\\', '/');
        ProjectPaths.SafePath(Path.Combine(paths.Assets, "maps"), file + ".map");
        var path = assets.ReadPath("maps", file + ".map");
        if (!file.Equals(before.FileName.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
        {
            var current = World.GetMap(edit.Id) ?? throw new InvalidDataException("Current map must be loaded before its resource can be changed.");
            var replacement = new MapReader(path);
            if (replacement.Width != current.Width || replacement.Height != current.Height) throw new InvalidDataException("Replacement map dimensions must match the existing map to preserve coordinates.");
        }
        Commit("maps-" + edit.Id, () => Apply(edit with { Title = title, FileName = file }), () => Apply(before));
    }
    private static void Apply(MapDefinition edit)
    {
        var map = Map(edit.Id); map.Title = edit.Title; map.FileName = edit.FileName; map.Light = (LightSetting)edit.Light; map.Music = (ushort)edit.Music;
        map.NoFight = edit.NoFight; map.NoDropPlayer = edit.NoDropPlayer; map.NoDropMonster = edit.NoDropMonster; map.NoTeleport = edit.NoTeleport;
    }
    public NpcDefinition ReadNpc(int id)
    {
        var npc = Npc(id); return new(id, npc.Name, npc.FileName, npc.MapIndex, npc.Location.X, npc.Location.Y, npc.Image, npc.Rate, Version(npc.Save),
            npc.TimeVisible, npc.HourStart * 60 + npc.MinuteStart, npc.HourEnd * 60 + npc.MinuteEnd, npc.MinLev, npc.MaxLev,
            npc.DayofWeek, npc.ClassRequired, npc.FlagNeeded, npc.ShowOnBigMap, npc.BigMapIcon, npc.CanTeleportTo);
    }
    public void Save(NpcDefinition edit)
    {
        var before = ReadNpc(edit.Id); Match(before.Version, edit.Version); var validated = ValidateNpc(edit);
        Commit("npcs-" + edit.Id, () => ApplyNpc(Npc(edit.Id), validated), () => ApplyNpc(Npc(edit.Id), before));
    }
    internal NpcDefinition ValidateNpc(NpcDefinition edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var name = Name(edit.Name); var script = Script("NPCs", edit.FileName);
        if (World.NPCs.Any(n => n.Info.Index != edit.Id && n.Info.FileName.Equals(script, StringComparison.OrdinalIgnoreCase) && n.Quests.Count > 0))
            throw new InvalidOperationException("This quest NPC script is already assigned. Use a separate script to avoid replacing the quest's NPC links.");
        var map = World.GetMap(edit.MapId) ?? throw new InvalidDataException("NPC map is not loaded.");
        if (!map.ValidPoint(edit.X, edit.Y)) throw new InvalidDataException("NPC position must be a walkable tile inside the map.");
        if (edit.Image is < 0 or > 65535 || edit.Rate is < 1 or > 65535 || edit.MinLevel is < 0 or > 32767 || edit.MaxLevel is < 0 or > 32767 ||
            (edit.MaxLevel != 0 && edit.MaxLevel < edit.MinLevel) || edit.FlagNeeded < 0 || edit.FlagNeeded >= Globals.FlagIndexCount || edit.BigMapIcon is < 0 or > 65535)
            throw new InvalidDataException("Invalid NPC image, rate, level, flag or map icon.");
        if (edit.StartMinute is < 0 or >= 1440 || edit.EndMinute is < 0 or >= 1440 || (edit.TimeVisible && edit.EndMinute <= edit.StartMinute))
            throw new InvalidDataException("NPC visibility requires a same-day time range, in minutes after midnight.");
        if (edit.DayOfWeek == null || (edit.DayOfWeek != "" && !Enum.GetNames<DayOfWeek>().Contains(edit.DayOfWeek)) ||
            edit.ClassRequired == null || (edit.ClassRequired != "" && !Enum.GetNames<MirClass>().Contains(edit.ClassRequired)))
            throw new InvalidDataException("Invalid NPC weekday or class.");
        return edit with { Name = name, FileName = script };
    }
    internal static void ApplyNpc(NPCInfo npc, NpcDefinition edit)
    {
        npc.Name = edit.Name; npc.FileName = edit.FileName; npc.MapIndex = edit.MapId;
        npc.Location = new Point(edit.X, edit.Y); npc.Image = (ushort)edit.Image; npc.Rate = (ushort)edit.Rate;
        npc.TimeVisible = edit.TimeVisible; npc.HourStart = (byte)(edit.StartMinute / 60); npc.MinuteStart = (byte)(edit.StartMinute % 60);
        npc.HourEnd = (byte)(edit.EndMinute / 60); npc.MinuteEnd = (byte)(edit.EndMinute % 60); npc.MinLev = (short)edit.MinLevel; npc.MaxLev = (short)edit.MaxLevel;
        npc.DayofWeek = edit.DayOfWeek; npc.ClassRequired = edit.ClassRequired; npc.FlagNeeded = edit.FlagNeeded;
        npc.ShowOnBigMap = edit.ShowOnBigMap; npc.BigMapIcon = edit.BigMapIcon; npc.CanTeleportTo = edit.CanTeleportTo;
    }
    public QuestDefinition ReadQuest(int id)
    {
        var quest = Quest(id);
        return new(id, quest.Name, quest.Group, quest.FileName, quest.RequiredMinLevel, quest.RequiredMaxLevel, quest.RequiredQuest, (int)quest.RequiredClass, (int)quest.Type, quest.TimeLimitInSeconds, Version(quest.Save));
    }
    public void Save(QuestDefinition edit)
    {
        var before = ReadQuest(edit.Id); Match(before.Version, edit.Version); var name = Name(edit.Name); var script = Script("Quests", edit.FileName);
        if (edit.Group == null || edit.Group.Length > 80 || edit.Group.Any(char.IsControl) || edit.MinLevel < 0 || edit.MaxLevel < Math.Max(1, edit.MinLevel) || edit.MaxLevel > 65535 ||
            edit.Prerequisite < 0 || edit.RequiredClass is < 1 or > 31 || edit.Type is < 0 or > 3 || edit.TimeLimitSeconds < 0)
            throw new InvalidDataException("Invalid quest requirements.");
        var visited = new HashSet<int> { edit.Id }; var prerequisite = edit.Prerequisite;
        while (prerequisite != 0)
        {
            if (!visited.Add(prerequisite)) throw new InvalidDataException("Quest prerequisites contain a cycle.");
            var prior = World.QuestInfoList.SingleOrDefault(q => q.Index == prerequisite) ?? throw new InvalidDataException("Prerequisite quest does not exist.");
            prerequisite = prior.RequiredQuest;
        }
        Commit("quests-" + edit.Id, () => Apply(edit with { Name = name, Group = edit.Group.Trim(), FileName = script }), () => Apply(before));
    }
    private static void Apply(QuestDefinition edit)
    {
        var quest = Quest(edit.Id); quest.Name = edit.Name; quest.Group = edit.Group; quest.FileName = edit.FileName;
        quest.RequiredMinLevel = edit.MinLevel; quest.RequiredMaxLevel = edit.MaxLevel; quest.RequiredQuest = edit.Prerequisite;
        quest.RequiredClass = (RequiredClass)edit.RequiredClass; quest.Type = (QuestType)edit.Type; quest.TimeLimitInSeconds = edit.TimeLimitSeconds;
    }
}
