using Mir2.Hosting;

internal static class AdminKeyChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "mir2-admin-key." + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("MIR2_ADMIN_KEY");
        try
        {
            Environment.SetEnvironmentVariable("MIR2_ADMIN_KEY", null);
            var paths = new ProjectPaths(root, Path.Combine(root, "runtime"));
            var generated = paths.AdminKey();
            check(generated.Length == 64 && paths.AdminKey() == generated, "默认随机管理密钥未生成或被重复替换");
            foreach (var value in new[] { "x", "test123", new string('K', 4096) })
            {
                var stored = " " + value + "\n";
                File.WriteAllText(paths.AdminKeyFile, stored);
                check(paths.AdminKey() == value, "文件管理密钥仍限制长度或未保留原有去空白语义");
                check(File.ReadAllText(paths.AdminKeyFile) == stored, "读取管理密钥改写了已有文件");
                Environment.SetEnvironmentVariable("MIR2_ADMIN_KEY", value);
                check(paths.AdminKey() == value, "环境变量管理密钥仍限制长度");
                check(File.ReadAllText(paths.AdminKeyFile) == stored, "环境变量管理密钥覆盖了原有文件");
                Environment.SetEnvironmentVariable("MIR2_ADMIN_KEY", null);
            }
            foreach (var empty in new[] { "", " \r\n\t" })
            {
                File.WriteAllText(paths.AdminKeyFile, empty);
                var rejected = false;
                try { paths.AdminKey(); }
                catch (InvalidDataException) { rejected = true; }
                check(rejected && File.ReadAllText(paths.AdminKeyFile) == empty, "空管理密钥被接受或被静默替换");
            }
            File.WriteAllText(paths.AdminKeyFile, "fallback");
            Environment.SetEnvironmentVariable("MIR2_ADMIN_KEY", "   ");
            check(paths.AdminKey() == "fallback", "空白环境变量未沿用文件密钥");
            check(ProjectPaths.KeyEquals("x", "x") && !ProjectPaths.KeyEquals("y", "x") && !ProjectPaths.KeyEquals(null, "x"), "短密钥鉴权失效");
            if (!OperatingSystem.IsWindows())
            {
                check(File.GetUnixFileMode(paths.AdminKeyFile) == (UnixFileMode.UserRead | UnixFileMode.UserWrite), "管理密钥文件不再私有");
                check(File.GetUnixFileMode(paths.Runtime) == (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute), "管理运行目录不再私有");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("MIR2_ADMIN_KEY", previous);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
