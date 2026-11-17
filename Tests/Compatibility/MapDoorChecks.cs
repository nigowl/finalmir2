using Client.MirObjects;
using Server.MirDatabase;
using Server.MirEnvir;

internal static class MapDoorChecks
{
    public static void Run(Action<bool,string> check,string temporary)
    {
        foreach(var classic in new[]{false,true})
        {
            var path=Path.Combine(temporary,$"doors-{classic}.map");CompanionMovementScenario.WriteMap(path,classic);
            var client=new MapReader(path);
            var server=new Map(new MapInfo{Index=987,Title="隔离门位检查",FileName=Path.ChangeExtension(path,null)});
            check(server.Load(),$"地图格式{(classic?0:1)}未成功加载");
            check(server.Width==client.Width&&server.Height==client.Height,"两端地图尺寸不同");
            check(client.MapCells[8,5].DoorIndex==1&&client.MapCells[9,5].DoorIndex==2,"原地图门号或高位掩码解析错误");
            check(server.DoorIndex[8,5]?.index==1&&server.DoorIndex[9,5]?.index==2,$"格式{(classic?0:1)}服务端门位与原客户端不一致");
            check(server.Doors.Count==2&&server.DoorIndex[15,5]==null,"前景动画被错误读取为门，产生不可见阻挡");
            check(!server.CheckDoorOpen(new(8,5))&&!server.CheckDoorOpen(new(9,5)),"未开门已经可通行");
            check(server.OpenDoor(client.MapCells[8,5].DoorIndex)&&server.CheckDoorOpen(new(8,5)),"原Opendoor门号不能打开对应门格");
            check(!server.CheckDoorOpen(new(9,5)),"开启一扇门误开另一门号");
            check(server.OpenDoor(client.MapCells[9,5].DoorIndex)&&server.CheckDoorOpen(new(9,5)),"相邻第二扇门无法打开");
            check(client.MapCells[20,5].FishingCell&&server.Cells[20,5].FishingAttribute==3,"门位修正影响后续灯光/钓鱼字段");
            check(!server.ValidPoint(0,0)&&(client.MapCells[0,0].BackImage&0x20000000)!=0,"地图阻挡字段失去两端一致性");
        }
    }
}
