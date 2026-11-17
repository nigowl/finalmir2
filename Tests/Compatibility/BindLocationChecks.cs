using System.Drawing;
using Server.MirDatabase;
using Server.MirEnvir;
using Shared;

internal static class BindLocationChecks
{
    public static void Run(Action<bool,string> check)
    {
        var info=new MapInfo();var map=new Map(info){Width=5,Height=5,Cells=new Cell[5,5]};
        for(var x=0;x<5;x++)for(var y=0;y<5;y++)map.Cells[x,y]=Cell.HighWall;
        map.Cells[1,2]=map.Cells[4,4]=new Cell{Attribute=CellAttribute.Walk};
        var center=new Point(2,2);
        check(map.GetBindLocation(center)==null,"没有对应安全区也移动绑定落点");
        var zone=new SafeZoneInfo{Info=info,Location=center,Size=1};info.SafeZones.Add(zone);
        check(map.GetBindLocation(center)==new Point(1,2),"阻挡的安全区中心没有选取区内合法落点");
        check(zone.Location==center,"修正落点改写原安全区定义");
        check(map.GetBindLocation(new(4,4))==new Point(4,4),"原合法落点被重定位");
        map.Cells[1,2]=Cell.HighWall;
        check(map.GetBindLocation(center)==null,"整个安全区被阻挡时传送到了区外");
        check(map.GetBindLocation(new(-1,2))==null,"无安全区的越界绑定位置被放宽");
        zone.Location=new(0,0);zone.Size=1;map.Cells[0,1]=new Cell{Attribute=CellAttribute.Walk};
        check(map.GetBindLocation(new(0,0))==new Point(0,1),"地图边缘的安全区没有正确限制搜索边界");
    }
}
