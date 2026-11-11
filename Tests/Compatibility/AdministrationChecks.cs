using Server;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class AdministrationChecks
{
    public static void Run(Action<bool, string> check, string temporary)
    {
        var cwd = Directory.GetCurrentDirectory(); var folder = Path.Combine(temporary, "engine-checks"); Directory.CreateDirectory(Path.Combine(folder, "Configs"));
        try
        {
            Directory.SetCurrentDirectory(folder);
            File.WriteAllText(Path.Combine("Configs", "GuildSettings.ini"), "[Exp]\nLevel-0=100\n[Cap]\nLevel-0=10\n");
            Settings.LoadEXP(); Settings.LoadHeroEXP(); Settings.LoadGuildSettings();
            var sizes = new[] { Settings.ExperienceList.Count, Settings.HeroExperienceList.Count, Settings.Guild_ExperienceList.Count, Settings.OrbsExpList.Count };
            Settings.LoadEXP(); Settings.LoadHeroEXP(); Settings.LoadGuildSettings();
            check(sizes.SequenceEqual(new[] { Settings.ExperienceList.Count, Settings.HeroExperienceList.Count, Settings.Guild_ExperienceList.Count, Settings.OrbsExpList.Count }) && sizes[0] == 500, "Repeated engine starts appended experience tables");
            var account = new AccountInfo { Password = "Start123", StoragePassword = "Store123" }; var restore = account.CaptureCredentials();
            var login = account.Password; var salt = account.Salt; var storage = account.StoragePassword;
            account.Password = "Later123"; account.StoragePassword = "Later123"; restore();
            check(account.Password == login && account.StoragePassword == storage && account.Salt == salt, "Credential rollback rehashed existing passwords");
            var muted = new CharacterInfo { Name = "MutedPlayer", CreationIP = "127.0.0.1", Heroes = new HeroInfo[1], ChatBanned = true, ChatBanExpiryDate = DateTime.MaxValue };
            using (var saved = new MemoryStream())
            {
                using (var writer = new BinaryWriter(saved, System.Text.Encoding.UTF8, true)) muted.Save(writer);
                saved.Position = 0; using var muteReader = new BinaryReader(saved, System.Text.Encoding.UTF8, true);
                var muteLoaded = new CharacterInfo(muteReader, Envir.Version, Envir.CustomVersion);
                check(muteLoaded.ChatBanned && muteLoaded.ChatBanExpiryDate == DateTime.MaxValue, "Player mute and expiry did not survive serialization");
                using var previous = new MemoryStream(saved.ToArray()[..^14]); using var previousReader = new BinaryReader(previous);
                var versionOne = new CharacterInfo(previousReader, Envir.Version, 1);
                check(versionOne.ChatBanned && versionOne.TradeGold == 0 && versionOne.Trade.All(i => i == null) && previous.Position == previous.Length, "Custom-version-one character no longer loads without escrow");
                using var legacy = new MemoryStream(saved.ToArray()[..^23]); using var legacyReader = new BinaryReader(legacy);
                var original = new CharacterInfo(legacyReader, Envir.Version, 0);
                check(!original.ChatBanned && legacy.Position == legacy.Length && original.Name == muted.Name, "Original custom-version-zero character is no longer readable");
            }
            var world = Envir.Main; var first = new CharacterInfo { Name = "RankFirst", Index = 1, Level = 2, AccountInfo = account };
            var second = new CharacterInfo { Name = "RankSecond", Index = 2, Level = 3, AccountInfo = account };
            world.RankTop.Clear(); world.RankClass = Enumerable.Range(0, 5).Select(_ => new List<RankCharacterInfo>()).ToArray();
            world.CheckRankUpdate(first); world.CheckRankUpdate(second);
            check(second.Rank.SequenceEqual(new[] { 1, 1 }) && first.Rank.SequenceEqual(new[] { 2, 2 }), "Inserted ranks were off by one");
            world.RemoveRank(second);
            check(second.Rank.All(r => r == 0) && first.Rank.All(r => r == 1) && world.RankClass[0].Count == 1, "Rank removal corrupted class ranking or position");
            var guild = new GuildInfo(new PlayerObject { Info = first }, "TestGuild"); guild.Ranks.Add(new GuildRank { Index = 1, Name = "Officers", Options = GuildRankOptions.CanRecruit });
            using var memory = new MemoryStream(); using (var writer = new BinaryWriter(memory, System.Text.Encoding.UTF8, true)) guild.Save(writer);
            memory.Position = 0; using var reader = new BinaryReader(memory); var loaded = new GuildInfo(reader);
            check(loaded.Ranks.Count == 2 && loaded.Ranks[1].Name == "Officers" && loaded.Ranks[1].Options == GuildRankOptions.CanRecruit, "Empty guild ranks lost names, indices or permissions");
            world.RankTop.Clear(); foreach (var list in world.RankClass) list.Clear();
        }
        finally { Directory.SetCurrentDirectory(cwd); }
    }
}
