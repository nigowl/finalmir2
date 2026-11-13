using MudBlazor;
namespace Mir2.Admin.Services;

public static class AdminTheme
{
    public static readonly MudTheme Dark = new()
    {
        PaletteDark = new PaletteDark
        {
            Primary = "#67c8b4", Secondary = "#d3ac69", Background = "#11181d", Surface = "#1a252c",
            AppbarBackground = "#1a252c", DrawerBackground = "#172027"
        }
    };
}
