using System.Text.Json;
using Client.MirObjects;
using Mir2.Hosting;
using Server.ConsoleHost;

internal static class AudioChecks
{
    public static void Run(Action<bool, string> check)
    {
        var wave = "RIFF"u8.ToArray().Concat(BitConverter.GetBytes(4u)).Concat("WAVE"u8.ToArray()).ToArray();
        check(OriginalSoundRules.WaveBytes(wave.Concat(new byte[100]).ToArray()).SequenceEqual(wave), "WAV尾部填充仍进入解码器");
        var rejected = false; try { OriginalSoundRules.WaveBytes(wave[..11]); } catch (InvalidDataException) { rejected = true; }
        check(rejected, "截断的WAV未被拒绝");
        check(OriginalSoundRules.SoundName(61) == "006-1" && OriginalSoundRules.SoundName(20310) == "M31-0", "未列出的怪物和技能声音未使用素材编号规则");
        check(OriginalSoundRules.Item(ItemType.Weapon) == 10111 && OriginalSoundRules.Item(ItemType.Potion) == 10108 && OriginalSoundRules.Item(ItemType.Quest) == 10118, "物品分类声音错误");
        check(OriginalSoundRules.Attack(Monster.Deer, 0) == 41 && OriginalSoundRules.Attack(Monster.Deer, 1) == 46, "怪物第二攻击声没有分开");
        check(OriginalSoundRules.RangeAttack(Monster.ZumaArcher, 0) == (int)Monster.ZumaArcher * 10 + 1 && OriginalSoundRules.RangeAttack(Monster.EvilMir, 0) == (int)Monster.EvilMir * 10 + 5, "怪物远程声音未遵守各自规则");
        check(OriginalSoundRules.Attack(Monster.DarkCaptain, 2) == -1 && OriginalSoundRules.RangeAttack(Monster.TucsonGeneral, 1) == (int)Monster.TucsonGeneral * 10 + 5, "特殊攻击被通用声音覆盖");
        check(OriginalSoundRules.CastingSound(Spell.Fencing, MirGender.Male) == 0 && OriginalSoundRules.CastingSound(Spell.FireBall, MirGender.Male) == 20310, "被动技能或施法声音错误");
        check(OriginalSoundRules.MissileSound(Spell.SoulShield) == 20690 && OriginalSoundRules.ImpactSound(Spell.SoulShield) == 20691, "护盾弹道声音编号错误");
        check(OriginalSoundRules.ImpactSound(Spell.None) == 21212 && OriginalSoundRules.ImpactSound(Spell.Plague) == 20823 && OriginalSoundRules.ImpactSound(Spell.IceThrust) == 20530, "普通箭矢/瘟疫/冰刺声音错误");
        check(OriginalSoundRules.GroundSound(Spell.MapLava, false) == 8302 && OriginalSoundRules.ObjectSound(SpellEffect.AwakeningSuccess) == 50002, "地面和对象效果声音未覆盖");
        check(OriginalSoundRules.AttackFrameSound(Monster.ZumaArcher, MirAction.AttackRange1, 4) == (int)Monster.ZumaArcher * 10 + 6 &&
            OriginalSoundRules.AttackFrameSound(Monster.HornedCommander, MirAction.Attack3, 5) == 8452, "怪物特殊攻击的帧声音丢失");
        check(OriginalSoundRules.Fallback(1391) == 21210 && OriginalSoundRules.Fallback(20861) == 20611 && OriginalSoundRules.Fallback(20310) == -1, "缺失声音替代错误覆盖现有声音");
        check(OriginalSoundRules.MeleeSound(Spell.DoubleSlash, MirGender.Male, true) == 20921 && OriginalSoundRules.MeleeSound(Spell.Slaying, MirGender.Female) == 20021, "双连斩第二段或女性攻杀声音错误");
        check(OriginalSoundRules.Attack(Monster.BabyPig, 1) == 10500 && OriginalSoundRules.Attack(Monster.BabyPig, 0) == -1, "宠物声音错误使用怪物编号");
        check(OriginalSoundRules.SoundName(10514) == "pet_rat" && OriginalSoundRules.Fallback(8304) == 8331, "已有宠物/地面素材未复用");
        check(OriginalSoundRules.Summon(Monster.BoneFamiliar) == 785 && OriginalSoundRules.Appear(Monster.BoneFamiliar) < 0,
            "骷髅召唤与已在场音效混用");
        check(OriginalSoundRules.Appear(Monster.ChestnutTree) < 0 && OriginalSoundRules.Appear(Monster.ArcherGuard) < 0 &&
            OriginalSoundRules.RangeAttack(Monster.ArcherGuard, 0) == 1391, "静物或守卫错误播放出场声，或影响守卫射击声");
    }

    public static void Audit(string output)
    {
        var root = Path.Combine(new ProjectPaths().Assets, "sounds");
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(path => Path.GetRelativePath(root, path).Replace('\\', '/'), StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<int, string>();
        foreach (var line in File.ReadLines(Path.Combine(root, "SoundList.lst")))
        {
            var parts = line.Trim().Split(':', '\t');
            if (parts.Length >= 2 && int.TryParse(parts[0], out var id)) overrides.TryAdd(id, parts[^1].Trim().Replace('\\', '/'));
        }
        var references = new Dictionary<int, List<string>>();
        void Add(int id, string source) { if (id <= 0) return; if (!references.TryGetValue(id, out var sources)) references[id] = sources = []; sources.Add(source); }
        var assets = new GameAssets(new ProjectPaths().Assets);
        var monsters = 0;
        foreach (var monster in Enum.GetValues<Monster>().Distinct())
        {
            var image = (int)monster;
            var library = image >= 10000 ? $"Pet/{image - 10000:00}" : image is >= 950 and < 960 ? $"Gate/{image - 950:00}"
                : image is >= 940 and < 950 ? $"Siege/{image - 940:00}" : $"Monster/{image:000}";
            JsonElement metadata;
            try { metadata = JsonSerializer.SerializeToElement(assets.Frames(library)); }
            catch (FileNotFoundException) { continue; }
            monsters++;
            var animations = metadata.GetProperty("frames");
            foreach (var animation in animations.EnumerateObject())
            {
                if (!Enum.TryParse<MirAction>(animation.Name, out var action) || animation.Value.GetProperty("count").GetInt32() <= 0 ||
                    animation.Value.GetProperty("start").GetInt32() >= metadata.GetProperty("count").GetInt32()) continue;
                var type = Array.IndexOf(new[] { MirAction.Attack1, MirAction.Attack2, MirAction.Attack3, MirAction.Attack4, MirAction.Attack5 }, action);
                if (type >= 0) Add(OriginalSoundRules.Attack(monster, (byte)type), $"monster/{monster}/{action}");
                type = Array.IndexOf(new[] { MirAction.AttackRange1, MirAction.AttackRange2, MirAction.AttackRange3 }, action);
                if (type >= 0) Add(OriginalSoundRules.RangeAttack(monster, (byte)type), $"monster/{monster}/{action}");
                if (action == MirAction.Struck) Add(image * 10 + 2, $"monster/{monster}/struck");
                if (action == MirAction.Die) Add(image * 10 + 3, $"monster/{monster}/die");
                for (var frame = -1; frame < animation.Value.GetProperty("count").GetInt32(); frame++)
                    Add(OriginalSoundRules.AttackFrameSound(monster, action, frame), $"monster/{monster}/{action}/frame{frame}");
            }
        }
        foreach (var spell in Enum.GetValues<Spell>().Where(spell => (byte)spell is > 0 and < 200))
        {
            foreach (var gender in Enum.GetValues<MirGender>()) Add(OriginalSoundRules.CastingSound(spell, gender), $"spell/{spell}/casting/{gender}");
            foreach (var gender in Enum.GetValues<MirGender>()) Add(OriginalSoundRules.MeleeSound(spell, gender), $"spell/{spell}/melee/{gender}");
            if (spell == Spell.DoubleSlash) Add(20921, $"spell/{spell}/second");
            if (OriginalSpellEffects.Missile(spell) != null)
                Add(OriginalSoundRules.MissileSound(spell, Crystal.Native.ClientSkillRules.Animation(spell) == MirAction.AttackRange2), $"spell/{spell}/missile");
            if (OriginalSpellEffects.Missile(spell) != null || OriginalSpellEffects.Impact(spell, MirDirection.Up) != null)
                Add(OriginalSoundRules.ImpactSound(spell), $"spell/{spell}/impact");
        }
        foreach (var spell in Enum.GetValues<Spell>()) Add(OriginalSoundRules.GroundSound(spell, true), $"ground/{spell}");
        foreach (var effect in Enum.GetValues<SpellEffect>()) Add(OriginalSoundRules.ObjectSound(effect), $"effect/{effect}");
        foreach (var id in overrides.Keys) Add(id, "override");
        string? FindSound(string name)
        {
            var candidates = Path.HasExtension(name)
                ? new[] { name, Path.ChangeExtension(name, Path.GetExtension(name).Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? ".wav" : ".mp3") }
                : new[] { name + ".wav", name + ".mp3" };
            return candidates.FirstOrDefault(files.ContainsKey);
        }
        var rows = references.OrderBy(pair => pair.Key).Select(pair =>
        {
            var name = overrides.GetValueOrDefault(pair.Key) ?? OriginalSoundRules.SoundName(pair.Key);
            var file = FindSound(name);
            var fallback = OriginalSoundRules.Fallback(pair.Key);
            string? fallbackFile = null;
            for (var alternate = fallback; alternate >= 0; alternate = OriginalSoundRules.Fallback(alternate))
            {
                var fallbackName = overrides.GetValueOrDefault(alternate) ?? OriginalSoundRules.SoundName(alternate);
                fallbackFile = FindSound(fallbackName);
                if (fallbackFile != null) break;
            }
            return new { id = pair.Key, name, file, fallback, fallbackFile, sources = pair.Value };
        }).ToArray();
        File.WriteAllText(output, JsonSerializer.Serialize(new { files = files.Count, overrides = overrides.Count, monsters, references = rows }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Audio audit: {files.Count} files, {overrides.Count} overrides, {monsters} monster libraries, {rows.Length} referenced IDs, {rows.Count(row => row.file != null || row.fallbackFile != null)} available; report {output}");
    }
}
