using Mir2.Management;
using Server.MirDatabase;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed class MonsterDefinitions(WorldDefinitions definitions, AdminChanges changes)
{
    private static MonsterInfo Monster(int id) => Envir.Main.GetMonsterInfo(id) ?? throw new FileNotFoundException("未找到怪物。");
    public MonsterDefinition Read(int id)
    {
        var m = Monster(id);
        return new() { Id = id, Name = m.Name, Image = (int)m.Image, Ai = m.AI, Effect = m.Effect, ViewRange = m.ViewRange,
            CoolEye = m.CoolEye, Level = m.Level, Light = m.Light, AttackSpeed = m.AttackSpeed, MoveSpeed = m.MoveSpeed,
            Experience = m.Experience, CanTame = m.CanTame, CanPush = m.CanPush, CanRecall = m.CanRecall, AutoRev = m.AutoRev,
            Undead = m.Undead, IsBoss = m.IsBoss, HasSpawnScript = m.HasSpawnScript, HasDieScript = m.HasDieScript,
            Stats = new(m.Stats), Version = definitions.Read("monsters", id).Version };
    }
    public void Save(int id, MonsterDefinition edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        var before = Read(id); AdminChanges.Match(before.Version, edit.Version);
        Validate(id, edit);
        if (edit.Name.Trim() != before.Name && Envir.Main.MonsterInfoList.Any(m => m.Index != id && m.Name.Equals(edit.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("其他怪物已使用这个名称。");
        var monster = Monster(id); var dropPath = monster.DropPath;
        changes.Commit(() =>
        {
            // 原默认掉落按名字找文件，改名时固定原引用，不能让重启创建空规则覆盖实际掉落。
            if (string.IsNullOrWhiteSpace(monster.DropPath) && edit.Name.Trim() != monster.Name) monster.DropPath = monster.Name;
            Apply(monster, edit);
        }, () => { Apply(monster, before); monster.DropPath = dropPath; });
    }
    public static void Validate(int id, MonsterDefinition edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        if (edit.Id != id) throw new InvalidDataException("怪物编号与当前对象不匹配。");
        AdminChanges.Name(edit.Name, 80);
        if (edit.Level is < 0 or > 65535 || edit.Image is < 0 or > 65535 || edit.AttackSpeed is < 1 or > 65535 || edit.MoveSpeed is < 1 or > 65535 ||
            new[] { edit.Ai, edit.Effect, edit.ViewRange, edit.CoolEye, edit.Light }.Any(v => v is < 0 or > 255))
            throw new InvalidDataException("等级/外观须为0–65535，攻击/移动间隔须为1–65535毫秒，行为/特效/视野/识破/光照须为0–255。");
        if (edit.Stats == null || edit.Stats[Stat.HP] is < 1 or > 100000000 ||
            edit.Stats.Values.Any(p => !Enum.IsDefined(p.Key) || p.Value is < -1000000 or > 100000000))
            throw new InvalidDataException("生命须为1–100000000，属性值须为-1000000–100000000且属性编号有效。");
        foreach (var (minimum, maximum) in new[] { (Stat.MinDC, Stat.MaxDC), (Stat.MinMC, Stat.MaxMC), (Stat.MinSC, Stat.MaxSC), (Stat.MinAC, Stat.MaxAC), (Stat.MinMAC, Stat.MaxMAC) })
            if (edit.Stats[minimum] < 0 || edit.Stats[maximum] < edit.Stats[minimum]) throw new InvalidDataException("攻击和防御范围须非负，最大值不能小于最小值。");
    }
    private static void Apply(MonsterInfo m, MonsterDefinition v)
    {
        m.Name = v.Name.Trim(); m.Image = (Monster)v.Image; m.AI = (byte)v.Ai; m.Effect = (byte)v.Effect;
        m.ViewRange = (byte)v.ViewRange; m.CoolEye = (byte)v.CoolEye; m.Level = (ushort)v.Level; m.Light = (byte)v.Light;
        m.AttackSpeed = (ushort)v.AttackSpeed; m.MoveSpeed = (ushort)v.MoveSpeed; m.Experience = v.Experience;
        m.CanTame = v.CanTame; m.CanPush = v.CanPush; m.CanRecall = v.CanRecall; m.AutoRev = v.AutoRev;
        m.Undead = v.Undead; m.IsBoss = v.IsBoss; m.HasSpawnScript = v.HasSpawnScript; m.HasDieScript = v.HasDieScript; m.Stats = new(v.Stats);
    }
}
