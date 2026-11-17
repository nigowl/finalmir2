using Mir2.Hosting;
using Server.ConsoleHost;

internal static class DataBootstrapChecks
{
    internal static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "mir2-bootstrap-" + Guid.NewGuid().ToString("N"));
        var paths = new ProjectPaths(root, Path.Combine(root, "runtime"));
        try
        {
            void Write(string path, string value)
            { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, value); }
            var source = Path.Combine(paths.Import, "Crystal Database");
            Write(Path.Combine(source, "Server.MirDB"), "seed");
            Write(Path.Combine(paths.Assets, "import-report.json"), "[]");
            Write(Path.Combine(source, "Configs/Setup.ini"), "seed config");
            Write(Path.Combine(source, "Envir/Drops/旧掉落.txt"), "old rules");
            Write(Path.Combine(source, "Envir/NPCs/旧脚本.txt"), "old npc");
            Write(Path.Combine(paths.World, "Configs/Setup.ini"), "运营配置");
            DataBootstrap.Prepare(paths);
            check(File.Exists(Path.Combine(paths.World, "Envir/Drops/旧掉落.txt")) && File.Exists(Path.Combine(paths.World, "Envir/NPCs/旧脚本.txt")), "首次导入未补齐游戏脚本");
            check(File.ReadAllText(Path.Combine(paths.World, "Configs/Setup.ini")) == "运营配置", "首次导入覆盖已存在配置");
            using (var storage = new SqliteDatabaseStorage(paths.Database)) storage.Write("Server.MirDB", [1, 2, 3]);
            File.Delete(Path.Combine(paths.World, "Envir/Drops/旧掉落.txt")); File.Delete(Path.Combine(paths.World, "Envir/NPCs/旧脚本.txt"));
            Write(Path.Combine(source, "Envir/Drops/后加旧脚本.txt"), "another seed");
            foreach (var cycle in new[] { 1, 2 })
            {
                DataBootstrap.Prepare(paths);
                check(!File.Exists(Path.Combine(paths.World, "Envir/Drops/旧掉落.txt")) && !File.Exists(Path.Combine(paths.World, "Envir/NPCs/旧脚本.txt")), $"第{cycle}次启动复活已清理脚本");
                check(!File.Exists(Path.Combine(paths.World, "Envir/Drops/后加旧脚本.txt")), "已建库版本继续导入新出现的旧脚本");
                check(File.ReadAllText(Path.Combine(paths.World, "Configs/Setup.ini")) == "运营配置", "已建库版本配置被导入覆盖");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
