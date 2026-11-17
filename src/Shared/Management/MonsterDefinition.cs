namespace Mir2.Management;

public sealed record MonsterDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Image { get; set; }
    public int Ai { get; set; }
    public int Effect { get; set; }
    public int ViewRange { get; set; }
    public int CoolEye { get; set; }
    public int Level { get; set; }
    public int Light { get; set; }
    public int AttackSpeed { get; set; }
    public int MoveSpeed { get; set; }
    public uint Experience { get; set; }
    public bool CanTame { get; set; }
    public bool CanPush { get; set; }
    public bool CanRecall { get; set; }
    public bool AutoRev { get; set; }
    public bool Undead { get; set; }
    public bool IsBoss { get; set; }
    public bool HasSpawnScript { get; set; }
    public bool HasDieScript { get; set; }
    public Stats Stats { get; set; } = new();
    public string Version { get; set; } = "";
}
