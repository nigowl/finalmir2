using System.Globalization;
using Mir2.Management;
using Server.MirEnvir;

namespace Server.ConsoleHost;

public sealed partial class SystemSettings(DevelopmentFiles files)
{
    private const string Setup = "Configs/Setup.ini";
    public ServerSettingsDocument Read()
    {
        var current = Current();
        return new(current, Version(files.Read(Setup), current), Envir.Main.Connections.Count, Envir.Main.Players.Count) { SkillOptions = SkillOptions() };
    }
    public ServerSettingsDocument Save(ServerSettingsEdit edit)
    {
        Validate(edit.Values);
        var document = files.Read(Setup);
        AdminChanges.Match(Version(document, Current()), edit.Version);
        var restart = edit.Values.SafeZoneBorder != Settings.SafeZoneBorder || edit.Values.SafeZoneHealing != Settings.SafeZoneHealing || edit.Values.SkillCooldownRate != Settings.SkillCooldownRate;
        // 先原子保存并保留原配置备份；失败时不改变运行参数。
        files.Save(new(Setup, UpdateIni(document.Content, edit.Values), document.Version));
        Apply(edit.Values);
        return Read() with { RequiresRestart = restart };
    }
    private static string Version(TextDocument document, ServerSettings current) => AdminChanges.Version(new { document.Version, current, Skills = SkillOptions() });
    private static ServerSettings Current() => WithBalance(new()
    {
        MaxUser = Settings.MaxUser, MaxIP = Settings.MaxIP,
        AllowNewAccount = Settings.AllowNewAccount, AllowChangePassword = Settings.AllowChangePassword,
        AllowLogin = Settings.AllowLogin, AllowNewCharacter = Settings.AllowNewCharacter, AllowDeleteCharacter = Settings.AllowDeleteCharacter,
        AllowCreateWarrior = Settings.AllowCreateWarrior, AllowCreateWizard = Settings.AllowCreateWizard, AllowCreateTaoist = Settings.AllowCreateTaoist,
        AllowCreateAssassin = Settings.AllowCreateAssassin, AllowCreateArcher = Settings.AllowCreateArcher,
        RequireStoragePassword = Settings.RequireStoragePassword,
        ExpRate = Settings.ExpRate, DropRate = Settings.DropRate, SaveDelay = Settings.SaveDelay,
        ItemTimeOut = Settings.ItemTimeOut, PlayerDiedItemTimeOut = Settings.PlayerDiedItemTimeOut, PKDelay = Settings.PKDelay,
        SafeZoneBorder = Settings.SafeZoneBorder, SafeZoneHealing = Settings.SafeZoneHealing, ExpMobLevelDifference = Settings.ExpMobLevelDifference,
        AutoLearnSkills = Settings.AutoLearnSkills, AutoLearnWarriorSkills = Settings.AutoLearnWarriorSkills.ToArray(),
        AutoLearnWizardSkills = Settings.AutoLearnWizardSkills.ToArray(), AutoLearnTaoistSkills = Settings.AutoLearnTaoistSkills.ToArray(),
        AutoLearnAssassinSkills = Settings.AutoLearnAssassinSkills.ToArray(), AutoLearnArcherSkills = Settings.AutoLearnArcherSkills.ToArray(),
        MonsterRecallEnabled = Settings.MonsterRecallEnabled, MonsterRecallRange = Settings.MonsterRecallRange, MonsterRecallCooldown = Settings.MonsterRecallCooldown
    });
    private static void Apply(ServerSettings s)
    {
        Settings.MaxUser = (ushort)s.MaxUser; Settings.MaxIP = (ushort)s.MaxIP;
        Settings.AllowNewAccount = s.AllowNewAccount; Settings.AllowChangePassword = s.AllowChangePassword;
        Settings.AllowLogin = s.AllowLogin; Settings.AllowNewCharacter = s.AllowNewCharacter; Settings.AllowDeleteCharacter = s.AllowDeleteCharacter;
        Settings.AllowCreateWarrior = s.AllowCreateWarrior; Settings.AllowCreateWizard = s.AllowCreateWizard; Settings.AllowCreateTaoist = s.AllowCreateTaoist;
        Settings.AllowCreateAssassin = s.AllowCreateAssassin; Settings.AllowCreateArcher = s.AllowCreateArcher; Settings.RequireStoragePassword = s.RequireStoragePassword;
        Settings.ExpRate = s.ExpRate; Settings.DropRate = s.DropRate; Settings.SaveDelay = s.SaveDelay;
        Settings.ItemTimeOut = s.ItemTimeOut; Settings.PlayerDiedItemTimeOut = s.PlayerDiedItemTimeOut; Settings.PKDelay = s.PKDelay;
        Settings.SafeZoneBorder = s.SafeZoneBorder; Settings.SafeZoneHealing = s.SafeZoneHealing; Settings.ExpMobLevelDifference = s.ExpMobLevelDifference;
        Settings.AutoLearnSkills = s.AutoLearnSkills; Settings.AutoLearnWarriorSkills = s.AutoLearnWarriorSkills.ToArray();
        Settings.AutoLearnWizardSkills = s.AutoLearnWizardSkills.ToArray(); Settings.AutoLearnTaoistSkills = s.AutoLearnTaoistSkills.ToArray();
        Settings.AutoLearnAssassinSkills = s.AutoLearnAssassinSkills.ToArray(); Settings.AutoLearnArcherSkills = s.AutoLearnArcherSkills.ToArray();
        Settings.MonsterRecallEnabled = s.MonsterRecallEnabled; Settings.MonsterRecallRange = s.MonsterRecallRange; Settings.MonsterRecallCooldown = s.MonsterRecallCooldown;
        var attackSpeedChanged = Settings.PlayerAttackSpeedBonus != s.PlayerAttackSpeedBonus;
        ApplyBalance(s);
        if (attackSpeedChanged)
        {
            Envir.Main.RequiresBaseStatUpdate();
            foreach (var player in Envir.Main.Players) player.RefreshStats();
            Envir.Main.RequiresHeroBaseStatUpdate();
        }
    }
    private static void Validate(ServerSettings s)
    {
        if (s is null) throw new InvalidDataException("缺少系统设置。");
        Range(s.MaxUser, 1, 65535, "最大在线连接数"); Range(s.MaxIP, 1, 65535, "同 IP 最大连接数");
        if (!float.IsFinite(s.ExpRate) || s.ExpRate < .01f || s.ExpRate > 100 || !float.IsFinite(s.DropRate) || s.DropRate < .01f || s.DropRate > 100)
            throw new InvalidDataException("经验与掉落倍率须为 0.01–100。");
        Range(s.SaveDelay, 1, 60, "自动存档间隔"); Range(s.ItemTimeOut, 1, 1440, "物品保留时间");
        Range(s.PlayerDiedItemTimeOut, 1, 1440, "死亡掉落保留时间"); Range(s.PKDelay, 1, 3600, "PK 值衰减间隔");
        Range(s.MonsterRecallRange, 1, 100, "怪物召回距离"); Range(s.MonsterRecallCooldown, 100, 600000, "怪物召回冷却");
        ValidateBalance(s);
        ValidateSkills(s);
    }
    private static void Range(int value, int min, int max, string name)
    { if (value < min || value > max) throw new InvalidDataException($"{name}须为 {min}–{max}。"); }
    private static Dictionary<string, Dictionary<string, string>> Entries(ServerSettings s) => WithBalanceEntries(new()
    {
        ["Network"] = new() { ["MaxUser"] = s.MaxUser.ToString(), ["MaxIP"] = s.MaxIP.ToString() },
        ["Permission"] = new()
        {
            ["AllowNewAccount"] = s.AllowNewAccount.ToString(), ["AllowChangePassword"] = s.AllowChangePassword.ToString(), ["AllowLogin"] = s.AllowLogin.ToString(),
            ["AllowNewCharacter"] = s.AllowNewCharacter.ToString(), ["AllowDeleteCharacter"] = s.AllowDeleteCharacter.ToString(),
            ["AllowCreateWarrior"] = s.AllowCreateWarrior.ToString(), ["AllowCreateWizard"] = s.AllowCreateWizard.ToString(), ["AllowCreateTaoist"] = s.AllowCreateTaoist.ToString(),
            ["AllowCreateAssassin"] = s.AllowCreateAssassin.ToString(), ["AllowCreateArcher"] = s.AllowCreateArcher.ToString(), ["RequireStoragePassword"] = s.RequireStoragePassword.ToString()
        },
        ["Game"] = new()
        {
            ["ExpRate"] = s.ExpRate.ToString(CultureInfo.CurrentCulture), ["DropRate"] = s.DropRate.ToString(CultureInfo.CurrentCulture),
            ["ItemTimeOut"] = s.ItemTimeOut.ToString(), ["PlayerDiedItemTimeOut"] = s.PlayerDiedItemTimeOut.ToString(), ["PKDelay"] = s.PKDelay.ToString(),
            ["MonsterRecallEnabled"] = s.MonsterRecallEnabled.ToString(), ["MonsterRecallRange"] = s.MonsterRecallRange.ToString(), ["MonsterRecallCooldown"] = s.MonsterRecallCooldown.ToString()
        },
        ["Optional"] = new() { ["SafeZoneBorder"] = s.SafeZoneBorder.ToString(), ["SafeZoneHealing"] = s.SafeZoneHealing.ToString(), ["ExpMobLevelDifference"] = s.ExpMobLevelDifference.ToString() },
        ["Database"] = new() { ["SaveDelay"] = s.SaveDelay.ToString() },
        ["AutoLearnSkills"] = new()
        {
            ["Enabled"] = s.AutoLearnSkills.ToString(),
            ["Warrior"] = string.Join(',', s.AutoLearnWarriorSkills.Select(spell => (byte)spell)),
            ["Wizard"] = string.Join(',', s.AutoLearnWizardSkills.Select(spell => (byte)spell)),
            ["Taoist"] = string.Join(',', s.AutoLearnTaoistSkills.Select(spell => (byte)spell)),
            ["Assassin"] = string.Join(',', s.AutoLearnAssassinSkills.Select(spell => (byte)spell)),
            ["Archer"] = string.Join(',', s.AutoLearnArcherSkills.Select(spell => (byte)spell))
        }
    }, s);
    private static string UpdateIni(string content, ServerSettings settings)
    {
        var remaining = Entries(settings); var lines = content.Replace("\r\n", "\n").Split('\n').ToList();
        var sections = new HashSet<string>(); var keys = new HashSet<string>(); string section = "";
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                if (remaining.ContainsKey(section) && !sections.Add(section)) throw new InvalidDataException($"配置存在重复节：{section}，请先在游戏开发中修正。");
            }
            else if (remaining.TryGetValue(section, out var values) && line.IndexOf('=') is var equals && equals > 0)
            {
                var key = line[..equals];
                if (!values.ContainsKey(key)) continue;
                if (!keys.Add(section + "/" + key)) throw new InvalidDataException($"配置存在重复参数：{section}/{key}。");
                lines[i] = key + "=" + values[key];
            }
        }
        // 缺失的键补入原节，未知键、注释和其他节保持原样。
        foreach (var (name, values) in remaining)
        {
            var missing = values.Where(p => !keys.Contains(name + "/" + p.Key)).Select(p => p.Key + "=" + p.Value).ToArray();
            if (missing.Length == 0) continue;
            var start = lines.IndexOf("[" + name + "]");
            if (start < 0) { lines.Add("[" + name + "]"); lines.AddRange(missing); }
            else
            {
                var end = lines.FindIndex(start + 1, l => l.StartsWith('[') && l.EndsWith(']'));
                lines.InsertRange(end < 0 ? lines.Count : end, missing);
            }
        }
        return string.Join('\n', lines);
    }
}
