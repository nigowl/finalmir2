using Mir2.Companions;
using Server.ConsoleHost;
using Server.MirDatabase;
using Server.MirEnvir;
using Server.MirObjects;

internal static class CompanionQuestChecks
{
    public static void Run(Action<bool,string> check)
    {
        var world=Envir.Main;
        var oldMaps=world.MapInfoList;var oldLoaded=world.MapList;var oldNpcs=world.NPCs;
        var oldMonsters=world.MonsterInfoList;var oldQuests=world.QuestInfoList;var oldObjects=world.Objects;
        var oldScripts=world.Scripts;var oldScriptIndex=world.ScriptIndex;
        try
        {
            var local=CreateMap(701);var remote=CreateMap(909);
            local.Info.Movements.Add(new MovementInfo{MapIndex=909,Source=new(1,1)});
            remote.Info.Movements.Add(new MovementInfo{MapIndex=701,Source=new(1,1)});
            var monsters=Enumerable.Range(101,3).Select(id=>new MonsterInfo{Index=id,Name="QuestTarget"+id,Level=1,
                Experience=1,Stats=new Stats{[Stat.HP]=1}}).ToList();
            var token=new ItemInfo{Index=301,Type=ItemType.Quest};
            monsters[2].Drops.Add(new DropInfo{Item=token,Chance=1});
            remote.Info.Respawns.Add(new RespawnInfo{MonsterIndex=101,Location=new(2,2),Count=1});
            foreach(var id in new[]{102,103})local.Info.Respawns.Add(new RespawnInfo{MonsterIndex=id,Location=new(2,2),Count=1});
            world.MapInfoList=[local.Info,remote.Info];world.MapList=[local,remote];world.NPCs=[];
            world.MonsterInfoList=monsters;world.Objects=[];
            world.QuestInfoList=[new QuestInfo{Index=1,KillTasks=[new QuestKillTask{Monster=monsters[0],Count=1}]},
                new QuestInfo{Index=2,KillTasks=[new QuestKillTask{Monster=monsters[1],Count=10}]},
                new QuestInfo{Index=3,ItemTasks=[new QuestItemTask{Item=token,Count=10}]}];
            var player=new PlayerObject{Info=new CharacterInfo{Level=10},Account=new AccountInfo(),
                Stats=new Stats{[Stat.HP]=100,[Stat.MinDC]=10,[Stat.MaxDC]=10,[Stat.BagWeight]=1000}};
            player.CurrentMap=local;player.CurrentLocation=new(1,1);
            player.Info.Equipment[0]=new UserItem(new ItemInfo{Type=ItemType.Weapon});
            player.Info.CurrentQuests=[new(1),new(2),new(3)];
            var first=player.CurrentQuests[0];var bulk=player.CurrentQuests[1];var almost=player.CurrentQuests[2];
            almost.ItemTaskCount[0].Count=9;
            var profile=new CompanionProfile();
            KnownGoal? Choose(ISet<string>? blocked=null)=>CompanionPlanning.Choose(player,profile,blocked);
            var originalQuests=player.Info.CurrentQuests;player.Info.CurrentQuests=[first];player.Level=9;
            player.Info.Equipment[0].CurrentDura=1000;
            check(Choose() is {QuestId:0,MapId:701},"新手为了远途职业任务放弃附近的可用练级怪");
            local.Info.NoExperience=true;
            check(Choose()?.QuestId==1,"就近练级忽略禁经验地图或丢失原任务");
            local.Info.NoExperience=false;player.Level=10;
            check(Choose()?.QuestId==1,"10级后没有恢复原远途猎杀任务");
            player.Info.CurrentQuests=originalQuests;
            var preferred=Choose();
            check(preferred?.QuestId==3&&preferred.MapId==701,"任务列表首项跨图抢占了同图即将收集完成的任务");
            var taskOptions=new List<Mir2.Companions.KnownGoal>();
            check(CompanionPlanning.Choose(player,profile,taskChoices:taskOptions,preferredQuest:2)?.QuestId==2,"合法模型任务优先建议未生效");
            check(taskOptions.Select(goal=>goal.QuestId).Order().SequenceEqual([1,2,3]),"模型候选未复用全部合法任务规划结果");
            check(CompanionPlanning.Choose(player,profile,preferredQuest:999)?.QuestId==3,"模型编造任务绕过原任务候选");
            var declinedTask=taskOptions.Single(goal=>goal.QuestId==2);
            check(CompanionPlanning.Choose(player,profile,new HashSet<string>{declinedTask.Key},preferredQuest:2)?.QuestId!=2,"模型任务建议绕过不可达排除");
            check(Choose(new HashSet<string>{preferred!.Key})?.QuestId==2,"排程忽略暂不可达排除，未回退到同图其他任务");
            var second=Choose(new HashSet<string>{preferred.Key})!;
            check(Choose(new HashSet<string>{preferred.Key,second.Key})?.QuestId==1,"同图目标均不可达时没有保留跨图任务");
            local.Info.NoFight=true;
            check(Choose()?.QuestId==1,"同图优先绕过禁战或可猎杀预检");
            local.Info.NoFight=false;bulk.KillTaskCount[0].Count=9;
            check(Choose()?.QuestId==2,"同图相同剩余计数未保持原任务顺序");
            bulk.KillTaskCount[0].Count=11;
            check(Choose()?.QuestId==3,"已满足的击杀计数被当成负数优先目标");
            almost.EndDateTime=DateTime.UtcNow;
            check(Choose()?.QuestId==1,"已完成待交付任务仍抢占未完成猎杀目标");
            check(first.KillTaskCount[0].Count==0&&almost.ItemTaskCount[0].Count==9&&player.CompletedQuests.Count==0,
                "只读任务排程修改了真实进度或完成集合");
            var visit=new QuestInfo{Index=4,Name="被旧猎杀任务压住的后续剧情",RequiredMinLevel=1,RequiredMaxLevel=255,ExpReward=100};
            world.QuestInfoList.Add(visit);
            var npc=new NPCObject(new NPCInfo{Index=1,Name="后续剧情NPC",FileName="ProgressionChecksAbsent",Location=new(3,3)}){CurrentMap=local};
            visit.NpcIndex=npc.ObjectID;npc.Quests=[visit];world.NPCs=[npc];
            player.Connection=(Server.MirNetwork.MirConnection)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Server.MirNetwork.MirConnection));
            player.Connection.SentQuestInfo=[visit];
            check(Choose()?.Kind=="accept"&&Choose()?.QuestId==4,"旧猎杀任务长期占据优先级，未接取可推进等级的拜访剧情");
            check(Choose(new HashSet<string>{$"accept/4/{npc.ObjectID}"})?.QuestId==1,"剧情NPC暂不可达时没有回退原任务");
            visit.RequiredMinLevel=11;check(Choose()?.QuestId==1,"剧情优先绕过原服等级限制");visit.RequiredMinLevel=1;
            visit.RequiredQuest=999;check(Choose()?.QuestId==1,"剧情优先绕过原服前置任务");visit.RequiredQuest=0;
            visit.RequiredClass=RequiredClass.Wizard;check(Choose()?.QuestId==1,"剧情优先绕过原服职业限制");visit.RequiredClass=RequiredClass.None;
            visit.NpcIndex=npc.ObjectID+1;check(Choose()?.QuestId==1,"把交付NPC当成了剧情接取NPC");visit.NpcIndex=npc.ObjectID;
            npc.Visible=false;check(Choose()?.QuestId==1,"不可见剧情NPC仍抢占正常任务");npc.Visible=true;
            visit.FlagTasks=[new QuestFlagTask{Number=1}];check(Choose()?.QuestId==1,"未知脚本标记任务抢占正常任务");visit.FlagTasks=[];
            var markers=Enumerable.Range(1,3).Select(flag=>
            {
                var marker=new NPCObject(new NPCInfo{Index=10+flag,Name="标记"+flag,FileName="FlagChecksAbsent"+flag,Location=new(flag,3)}){CurrentMap=remote};
                var page=new NPCPage(NPCScript.MainKey);
                page.SegmentList.Add(new NPCSegment(page,[],[],[],[],[]){CheckList=[new(CheckType.CheckQuest,"4","ACTIVE")],
                    ActList=[new(ActionType.Set,flag.ToString(),"1"),new(ActionType.LocalMessage,"已发现","Hint"),new(ActionType.Break)]});
                NPCScript.Get(marker.ScriptID).NPCPages=[page];return marker;
            }).ToArray();
            visit.FlagTasks=Enumerable.Range(1,3).Select(flag=>new QuestFlagTask{Number=flag}).ToList();
            check(Choose()?.QuestId==4&&Choose()?.Kind=="accept","全部标记有公开安全来源时未接取剧情");
            check(CompanionWorldChecks.Read().Quests.Single(quest=>quest.Id==4).Targets.Length==3,"任务巡检没有显示三个真实主对话标记来源");
            markers[2].Visible=false;check(Choose()?.QuestId==1,"缺少可见来源仍接取多标记任务");markers[2].Visible=true;
            markers[2].VisibleLog[player.Info.Index]=false;check(Choose()?.QuestId==1,"绕过角色独立可见性接取标记任务");markers[2].VisibleLog.Clear();
            markers[2].CurrentMap=CreateMap(999);check(Choose()?.QuestId==1,"无公开路线仍接取标记任务");markers[2].CurrentMap=remote;
            var segment=NPCScript.Get(markers[2].ScriptID).NPCPages[0].SegmentList[0];
            foreach(var action in new[]{ActionType.GiveGold,ActionType.TakeItem,ActionType.Move,ActionType.Goto,ActionType.Call})
            {segment.ActList.Add(new(action,"1"));check(Choose()?.QuestId==1,"自动规划了含资产或跳转动作的标记脚本："+action);segment.ActList.RemoveAt(segment.ActList.Count-1);}
            segment.ElseActList.Add(new(ActionType.Set,"3","1"));check(Choose()?.QuestId==1,"自动规划了有分支副作用的标记脚本");segment.ElseActList.Clear();
            segment.CheckList.Add(new(CheckType.Random,"1","2"));check(Choose()?.QuestId==1,"自动求值未知标记条件");segment.CheckList.RemoveAt(1);
            segment.CheckList[0].Params[1]="COMPLETE";check(Choose()?.QuestId==1,"把完成前置条件误判为当前活动任务");segment.CheckList[0].Params[1]="ACTIVE";
            segment.CheckList[0].Params[0]="999";check(Choose()?.QuestId==1,"使用了其他任务的标记来源");segment.CheckList[0].Params[0]="4";
            segment.ActList[0].Params[0]="999";check(Choose()?.QuestId==1,"使用了任务外的标记");segment.ActList[0].Params[0]="3";
            segment.ActList[0].Params[1]="0";check(Choose()?.QuestId==1,"把清除标记当成任务进展");segment.ActList[0].Params[1]="1";
            segment.Buttons.Add("[@BUY]");check(Choose()?.QuestId==1,"自动规划了带交互跳转的标记页");segment.Buttons.Clear();
            var progress=new QuestProgressInfo(4);player.Info.CurrentQuests.Add(progress);
            var markerGoal=Choose();check(markerGoal?.Kind=="service"&&markerGoal.QuestId==4&&markerGoal.NextExit?.DestinationMapId==909,"活动标记任务未沿公开路线访问NPC");
            check(Choose(new HashSet<string>{markerGoal!.Key})?.NpcId!=markerGoal.NpcId,"标记规划忽略目标暂不可达排除");
            var done=progress.FlagTaskSet.Single(flag=>flag.Number==CompanionQuestFlags.Source(markers.Single(marker=>marker.ObjectID==markerGoal.NpcId))!.Value.Flag);
            done.State=true;check(Choose()?.NpcId!=markerGoal.NpcId,"已由原服完成的标记仍被反复访问");
            check(!player.Info.Flags.Take(4).Any(value=>value)&&progress.FlagTaskSet.Count(flag=>flag.State)==1&&player.Account.Gold==0,
                "标记规划执行了脚本或修改了角色真实标记/资产");
            player.Info.CurrentQuests.Remove(progress);world.NPCs=[npc];visit.FlagTasks=[];
            visit.ExpReward=0;check(Choose()?.QuestId==1,"无奖励、无后续的空白任务抢占发育");visit.ExpReward=100;
            var active=player.Info.CurrentQuests;player.Info.CurrentQuests=Enumerable.Repeat(first,Globals.MaxConcurrentQuests).ToList();
            check(Choose()?.Kind!="accept","剧情优先绕过原服同时接取任务数量限制");player.Info.CurrentQuests=active;
            check(player.CurrentQuests.Count==3&&player.CompletedQuests.Count==0&&player.Account.Gold==0,"剧情预检写入了任务、完成集合或资产");
            world.NPCs=[npc];world.QuestInfoList=[visit];npc.Quests=[visit];visit.FinishNpcIndex=npc.ObjectID;
            var completed=new QuestProgressInfo(4){EndDateTime=DateTime.UtcNow};player.Info.CurrentQuests=[completed];
            var boots=new ItemInfo{Index=302,Type=ItemType.Boots,StackSize=1};
            visit.FixedRewards=[new QuestItemReward{Item=boots,Count=1}];
            player.Info.Inventory=Enumerable.Range(0,46).Select(i=>new UserItem(new ItemInfo{Index=400+i,Type=ItemType.Ore,StackSize=1}){UniqueID=(ulong)i+1}).ToArray();
            var nextId=world.NextUserItemID;
            check(Choose()?.Kind=="wait","满包且无法减负时仍安排领取装备奖励，原服将拒绝而陪玩等不到回执");
            player.Info.Inventory[45]=null;check(Choose()?.Kind=="turn-in","腾出奖励空间后没有恢复正常交付");
            visit.FixedRewards[0].Count=2;check(Choose()?.Kind=="wait","一格空间被误认为能领取两件不堆叠奖励");
            player.Info.Inventory[44]=null;check(Choose()?.Kind=="turn-in","两件奖励有两格时仍被阻止");
            visit.FixedRewards=[];visit.SelectRewards=[new QuestItemReward{Item=boots,Count=1},new QuestItemReward{Item=boots,Count=3}];
            check(Choose()?.Kind=="wait","选择奖含放不下的选项时仍发放交付目标");
            player.Info.Inventory[43]=null;check(Choose()?.Kind=="turn-in","选择奖每种都能放下时未恢复交付");
            visit.SelectRewards=[];player.Info.Inventory[43]=player.Info.Inventory[44]=player.Info.Inventory[45]=new UserItem(boots);
            check(Choose()?.Kind=="turn-in","无物品奖励的任务被普通满包错误阻止");
            visit.FixedRewards=[new QuestItemReward{Item=token,Count=1}];
            check(Choose()?.Kind=="turn-in","普通满包错误阻止有独立任务包空间的奖励");
            player.Info.QuestInventory=Enumerable.Repeat(new UserItem(token){Count=1},40).ToArray();
            check(Choose()?.Kind=="wait","任务包也满时仍领取任务物品奖励");
            check(world.NextUserItemID==nextId&&player.CompletedQuests.Count==0&&completed.Completed&&player.Account.Gold==0,
                "奖励容量预检消耗真实物品编号或改写任务/金币");
        }
        finally
        {
            world.MapInfoList=oldMaps;world.MapList=oldLoaded;world.NPCs=oldNpcs;
            world.MonsterInfoList=oldMonsters;world.QuestInfoList=oldQuests;world.Objects=oldObjects;
            world.Scripts=oldScripts;world.ScriptIndex=oldScriptIndex;
        }
    }

    private static Map CreateMap(int id)
    {
        var map=new Map(new MapInfo{Index=id}){Width=5,Height=5,Cells=new Cell[5,5],WalkableCells=[]};
        for(var x=0;x<5;x++)for(var y=0;y<5;y++)
        {map.Cells[x,y]=new Cell{Attribute=CellAttribute.Walk};map.WalkableCells.Add(new(x,y));}
        return map;
    }
}
