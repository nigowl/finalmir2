using System.Drawing;
using Mir2.Companion;

internal static class CompanionPathChecks
{
    public static void Run(Action<bool, string> check)
    {
        var calls = 0;
        bool Walkable(Point p)
        {
            calls++;
            return p.X >= 0 && p.Y >= 0 && p.X < 240 && p.Y < 240 && (p.X != 100 || p.Y > 200);
        }
        var path = new CompanionPath();
        var occupied = new HashSet<Point>();
        var origin = new Point(90, 100); var target = new Point(110, 100);
        var first = path.Next(origin, target, 0, Walkable, occupied);
        check(first != null, "陪玩未找到需要先远离目标的大范围绕行路线");
        calls = 0;
        var second = path.Next(first!.Value, target, 0, Walkable, occupied);
        check(second != null && calls <= 2, "陪玩每走一步都重新搜索整张地图，没有沿用已找到路线");
        occupied.Add(second!.Value);
        var changed = path.Next(first.Value, target, 0, Walkable, occupied);
        check(changed != null && changed != second, "缓存路线下一格被占用后没有重新规划");
        occupied.Clear(); path.Clear();
        var point = origin; var steps = 0;
        while (point != target && steps++ < 1000)
        {
            var next = path.Next(point, target, 0, Walkable, occupied);
            if (next == null || !Walkable(next.Value)) break;
            point = next.Value;
        }
        check(point == target && steps > 200, "陪玩在长障碍前兜圈，未走完实际绕行路线");
        path.Clear(); occupied.Add(target);
        var approach = path.Next(new(108, 100), target, 1, Walkable, occupied);
        check(approach != null && approach != target, "NPC占格时没有搜索附近交互格");
        check(path.Next(new(109, 100), target, 1, Walkable, occupied) == null, "已到交互范围仍继续走动");
        check(path.Next(new(90, 100), new(-10, -10), 0, Walkable, occupied) == null, "越界目的地没有拒绝");
        LargeMapChecks(check);
        MovingGoalChecks(check);
        var retreat=CompanionSafety.Retreat(new(5,5),[new(5,6)],point=>point.X>=4&&point.X<=6&&point.Y>=4&&point.Y<=6,new HashSet<Point>{new(5,4)});
        check(retreat is { Y:4 }&&retreat!=new Point(5,4),"撤退没有避开占格并远离近身怪物");
        check(CompanionSafety.Retreat(new(5,5),[new(5,6)],point=>point.Y>=5,new HashSet<Point>())==null,"撤退受阻时反而向怪物绕行");
        var flank=CompanionSafety.Retreat(new(5,5),[new(5,6),new(5,4)],_=>true,new HashSet<Point>{new(5,6),new(5,4)});
        check(flank.HasValue && flank.Value.Y!=5 && flank.Value.X!=5 &&
            new[]{new Point(5,6),new Point(5,4)}.Count(p=>Functions.MaxDistance(p,flank.Value)<=1)==1,"被夹击时没有选择可减少近身攻击者的空格");
        check(CompanionSafety.Retreat(new(5,5),[new(5,6),new(5,4)],point=>point.X==5,new HashSet<Point>{new(5,4),new(5,6)})==null,"完全封住的通道仍向攻击者移动");
        check(CompanionSafety.Retreat(new(5,5),[],_=>true,new HashSet<Point>())==null,"没有威胁仍进行撤退");
    }

    private static void LargeMapChecks(Action<bool,string> check)
    {
        var calls=0;
        bool Open(Point p){calls++;return p.X>=0&&p.Y>=0&&p.X<1000&&p.Y<800;}
        var path=new CompanionPath();var occupied=new HashSet<Point>();
        var origin=new Point(500,750);var target=new Point(167,85);
        var next=path.Next(origin,target,0,Open,occupied);
        check(next!=null,"大地图上可直达的665格路线没有返回首步");
        check(calls<40000,"同等预计路径长度时仍大面积扩散，没有优先接近目标");
        var point=origin;var steps=0;var cached=true;
        while(next!=null&&steps++<1000)
        {
            point=next.Value;
            if(point==target)break;
            calls=0;next=path.Next(point,target,0,Open,occupied);
            cached&=calls<=2;
        }
        check(cached,"大地图已找到完整路线后仍反复搜索");
        check(point==target&&steps==665,"同优先级选择破坏了大地图最短路径或缓存推进");
        path.Clear();calls=0;
        bool Closed(Point p)=>Open(p)&&p.Y!=400;
        check(path.Next(origin,target,0,Closed,occupied)==null,"不可达的分隔地图返回了跨墙路线");
        check(calls<=800001,"不可达大地图突破原10万节点搜索上限");
    }

    private static void MovingGoalChecks(Action<bool,string> check)
    {
        bool Corridor(Point p) => p.X >= 0 && p.Y >= 0 && p.X <= 90 && p.Y <= 90 && (p.Y != 50 || p.X < 10 || p.X > 70);
        var path = new CompanionPath(); var occupied = new HashSet<Point>(); var point = new Point(40,49); var steps = 0;
        while (point.Y <= 50 && steps++ < 100)
        {
            var movingTarget = new Point(steps % 2 == 1 ? 60 : 20,51);
            var next = path.Next(point,movingTarget,1,Corridor,occupied,"hunt/region");
            if (next == null) break;
            point = next.Value;
        }
        check(point.Y > 50,"同一猎杀区域的最近怪物跨墙跳换时持续折返，未完成绕行");
        path.Clear(); var origin2 = new Point(40,49); var right = new Point(60,51); var left = new Point(20,51);
        var forward = path.Next(origin2,right,1,Corridor,occupied,"hunt/one")!.Value;
        var blockedStep = path.Next(forward,left,1,Corridor,occupied,"hunt/one")!.Value;
        occupied.Add(blockedStep);
        var detour = path.Next(forward,left,1,Corridor,occupied,"hunt/one");
        check(detour!=null&&detour!=blockedStep,"固定猎杀区域后没有避开下一格动态占用");
        occupied.Clear();
        var fresh = new CompanionPath().Next(forward,left,1,Corridor,occupied);
        check(path.Next(forward,left,1,Corridor,occupied,"hunt/two")==fresh,"换猎杀区域后仍沿用旧目标路线");
        path.Clear(); path.Next(origin2,right,1,Corridor,occupied,"hunt/one");
        check(path.Next(origin2,left,1,Corridor,occupied)==new CompanionPath().Next(origin2,left,1,Corridor,occupied),"转为战斗或补给等普通目标后仍锁定旧猎杀区域");
        path.Clear(); path.Next(origin2,right,1,Corridor,occupied,"hunt/one");
        check(path.Next(origin2,left,2,Corridor,occupied,"hunt/one")==new CompanionPath().Next(origin2,left,2,Corridor,occupied),"交互范围改变后仍锁定旧猎杀路线");
        path.Clear(); point = new(1,1);
        while(point.X<4)point=path.Next(point,new(4,1),0,_=>true,occupied,"hunt/near")!.Value;
        check(path.Next(point,new(8,1),0,_=>true,occupied,"hunt/near")!=null,"到达已选区域后没有追踪刷新目标");
        path.Clear(); path.Next(origin2,right,1,Corridor,occupied,"hunt/clear");path.Clear();
        check(path.Next(origin2,left,1,Corridor,occupied,"hunt/clear")==new CompanionPath().Next(origin2,left,1,Corridor,occupied),"清理地图后复用了旧猎杀区域");
    }
}
