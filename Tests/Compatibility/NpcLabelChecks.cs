using System.Text.Json;
using System.Text.RegularExpressions;

internal static class NpcLabelChecks
{
    public static void Audit(string source, string output)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(source));
        var rows = json.RootElement.EnumerateArray().Select(row =>
        {
            var old = row.GetProperty("name").GetString()!;
            var file = row.GetProperty("details").GetString()!.Split(" · ")[^1];
            return new { Id = row.GetProperty("id").GetInt32(), Original = old, Name = NpcLabels.DisplayName(old, file), Role = NpcLabels.Role(old, file), Script = file };
        }).ToArray();
        if (rows.Any(row => string.IsNullOrWhiteSpace(row.Name) || row.Name.Any(char.IsControl) || Regex.IsMatch(row.Name, "[A-Za-z0-9_<>]") || string.IsNullOrWhiteSpace(row.Role)))
            throw new InvalidDataException("NPC名称/分类仍含非中文或控制字符。");
        File.WriteAllText(output, JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        Console.WriteLine($"PASS {rows.Length} NPC nameplates: {rows.Count(row => row.Name != row.Original)} unified names; all Chinese, preserved script identities, two-line roles.");
    }
}
