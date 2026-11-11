namespace Client.MirObjects;

// Original map-library indices shared by the Godot renderer and management thumbnails.
public static class MapTileLibraries
{
    public static string Resolve(int index)
    {
        if (index == 0) return "Map/WemadeMir2/Tiles";
        if (index == 1) return "Map/WemadeMir2/Smtiles";
        if (index == 2) return "Map/WemadeMir2/Objects";
        if (index is >= 3 and <= 28) return $"Map/WemadeMir2/Objects{index - 1}";
        if (index == 90) return "Map/WemadeMir2/Objects_32bit";
        if (index is >= 100 and < 110) return "Map/ShandaMir2/Tiles" + (index == 100 ? "" : index - 99);
        if (index is >= 110 and < 120) return "Map/ShandaMir2/SmTiles" + (index == 110 ? "" : index - 109);
        if (index is >= 120 and <= 150) return "Map/ShandaMir2/Objects" + (index == 120 ? "" : index - 119);
        if (index == 190) return "Map/ShandaMir2/AniTiles1";
        if (index is < 200 or >= 375) return "";
        string[] names = ["Tilesc", "Tiles30c", "Tiles5c", "Smtilesc", "Housesc", "Cliffsc", "Dungeonsc", "Innersc", "Furnituresc", "Wallsc", "smObjectsc", "Animationsc", "Object1c", "Object2c"];
        var start = index >= 300 ? 300 : 200; var group = (index - start) / 15; var part = (index - start) % 15;
        if (group > 4 || part >= names.Length) return "";
        var theme = new[] { "", "wood", "sand", "snow", "forest" }[group];
        return start == 200 ? $"Map/WemadeMir3/{(theme.Length > 0 ? theme + "/" : "")}{names[part]}" : $"Map/ShandaMir3/{names[part]}{theme}";
    }

}
