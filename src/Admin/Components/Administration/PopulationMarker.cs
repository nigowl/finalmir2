namespace Mir2.Admin.Components.Administration;

public sealed record PopulationMarker(string Kind, int Id, string Name, int X, int Y, int Count = 0, int Spread = 0, int Image = 0)
{
    public static string NpcImage(int image) => $"/asset-preview?library={Uri.EscapeDataString(image >= 1000 ? $"Flag/{image - 1000:00}" : $"NPC/{image:00}")}&index=0";
}
