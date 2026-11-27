using System.Reflection;
using Crystal.Native;
using Mir2.Companion;
using Mir2.Companions;
using S = ServerPackets;

internal static class CompanionPurchaseChecks
{
    internal static void Run(Action<bool, string> check)
    {
        Rejections(check);
        foreach (var material in new[] { false, true })
        foreach (var paidFirst in new[] { false, true })
        {
            var events = new List<WorkerEvent>();
            var profile = new CompanionProfile { Id = Guid.NewGuid(), Name = "购买回执检查" };
            var session = new CompanionSession(new(profile, "ReceiptAccount", "ReceiptPassword", true, "127.0.0.1", 1,
                "test", "purchase-check", CompanionProtocol.Fingerprint, "", "", "", 1), e => { events.Add(e); return Task.CompletedTask; });
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo Field(string name) => typeof(CompanionSession).GetField(name, flags)!;
            void Apply(Packet packet) => ((Task)typeof(CompanionSession).GetMethod("ApplyAssetResult", flags)!.Invoke(session, [packet])!).GetAwaiter().GetResult();
            Packet Gained(int index)
            {
                var item = new UserItem(new ItemInfo { Index = index, Type = ItemType.CraftingMaterial });
                return material ? new S.GainedQuestItem { Item = item } : new S.GainedItem { Item = item };
            }
            bool Pending() => (string?)Field("pendingAsset").GetValue(session) == "buy";
            try
            {
                Field("pendingAsset").SetValue(session, "buy"); Field("boughtItem").SetValue(session, 501);
                Apply(Gained(502)); check(Pending(), "其他商品的获取回执解除了购买锁");
                Apply(paidFirst ? new S.LoseGold { Gold = 10 } : Gained(501));
                check(Pending(), "仅收到扣款或商品之一便认定购买成功");
                Apply(paidFirst ? Gained(501) : new S.LoseGold { Gold = 10 });
                check(!Pending() && events.Count(e => e.Kind == "asset" && e.Text == "buy:成功") == 1,
                    "普通/材料商品及扣款回执齐全后仍未完成原购买");
            }
            finally { session.Stop().GetAwaiter().GetResult(); }
        }
    }
    private static void Rejections(Action<bool,string> check)
    {
        var events=new List<WorkerEvent>();var profile=new CompanionProfile{Id=Guid.NewGuid(),Name="购买拒绝检查"};
        var session=new CompanionSession(new(profile,"ReceiptAccount","ReceiptPassword",true,"127.0.0.1",1,"test","reject-check",CompanionProtocol.Fingerprint,"","","",1),e=>{events.Add(e);return Task.CompletedTask;});
        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
        FieldInfo Field(string name)=>typeof(CompanionSession).GetField(name,flags)!;
        void Apply(Packet packet)=>((Task)typeof(CompanionSession).GetMethod("ApplyAssetResult",flags)!.Invoke(session,[packet])!).GetAwaiter().GetResult();
        bool Pending()=>(string?)Field("pendingAsset").GetValue(session)=="buy";
        try
        {
            var state=(GameState)Field("state").GetValue(session)!;state.Apply(new S.MapInformation{MapIndex=1});
            session.Knowledge=new("reject",1,true,true,true,[],[],[],[],Goal:new("service",1,0,0,"原NPC",Key:"service/42"));
            Field("pendingAsset").SetValue(session,"buy");Field("assetId").SetValue(session,47UL);
            Apply(new S.Chat{Type=ChatType.Normal,Message="购买失败：金币不足。"});check(Pending(),"普通聊天伪造购买拒绝并解除资产锁");
            Apply(new S.Chat{Type=ChatType.System,Message="无关系统提示"});check(Pending(),"无关系统消息解除了购买锁");
            Field("purchasePaid").SetValue(session,true);
            Apply(new S.Chat{Type=ChatType.System,Message="购买失败：金币不足。"});check(Pending(),"已有扣款的矛盾结果被当成明确拒绝");
            Field("purchasePaid").SetValue(session,false);Field("receivedPurchase").SetValue(session,true);
            Apply(new S.Chat{Type=ChatType.System,Message="购买失败：金币不足。"});check(Pending(),"已有物品的矛盾结果被当成明确拒绝");
            Field("receivedPurchase").SetValue(session,false);
            Apply(new S.Chat{Type=ChatType.System,Message="购买失败：金币不足。"});
            check(!Pending()&&events.Count(e=>e.Text=="buy:拒绝")==1,"原服明确拒绝购买仍等待超时并误报资产未知");
            check(events.Any(e=>e.Kind=="goal-blocked"&&e.Text=="service/42"),"购买拒绝后未排除旧货架而会立即重复购买");
        }
        finally{session.Stop().GetAwaiter().GetResult();}
    }
}
