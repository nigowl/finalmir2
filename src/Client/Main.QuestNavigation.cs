using Crystal.Native;
using Godot;
using C = ClientPackets;
using S = ServerPackets;

public partial class Main
{
    private readonly Dictionary<int, int> questNavigationRequests = new();
    private int questNavigationSequence;
    private (int Quest, int Target)? activeQuestNavigation;
    private bool applyingQuestNavigation, questNavigationStarted;
    private long questNavigationWait, questExitTurn;
    private string hoveredQuestLink = "";

    private void ConfigureQuestLinks(RichTextLabel label)
    {
        label.BbcodeEnabled = true; label.MetaUnderlined = false;
        label.MouseFilter = MouseFilterEnum.Pass;
        label.MetaClicked += meta =>
        {
            var parts = meta.AsString().Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out var quest) && int.TryParse(parts[1], out var target)) NavigateQuest(quest, target);
        };
        label.MetaHoverStarted += meta => { hoveredQuestLink = meta.AsString(); RefreshQuestLinks(); };
        label.MetaHoverEnded += _ => { hoveredQuestLink = ""; RefreshQuestLinks(); };
    }
    private static string QuestEscape(string text) => text.Replace("[", "[lb]");
    private string QuestLinkedText(int quest, string text)
    {
        var rendered = QuestEscape(text);
        if (!State.QuestNavigation.TryGetValue(quest, out var navigation)) return rendered;
        var links = new Dictionary<string, QuestNavigationTarget>(StringComparer.Ordinal);
        foreach (var target in navigation.Targets)
        {
            if (target.Name.Length > 0) links.TryAdd(target.Name, target);
            var coordinate = $"{target.Location.X}:{target.Location.Y}";
            foreach (var value in new[] { coordinate, $"[{coordinate}]", $"({coordinate})", $"（{coordinate}）", $"{target.MapTitle}({coordinate})", $"{target.MapTitle}（{coordinate}）" })
                links.TryAdd(value, target);
        }
        string Link(string value, QuestNavigationTarget target)
        {
            var key = $"{quest}:{target.Id}"; var hover = key == hoveredQuestLink;
            var coordinate = $"{target.Location.X}:{target.Location.Y}";
            if (new[] { coordinate, $"[{coordinate}]", $"({coordinate})", $"（{coordinate}）", $"{target.MapTitle}({coordinate})", $"{target.MapTitle}（{coordinate}）" }.Contains(value, StringComparer.Ordinal))
                value = $"{target.MapTitle}({coordinate})";
            return $"[color=#{(hover ? "ffffff" : target.Id == 0 ? "91df88" : "edc775")}][url={key}]" +
                (hover ? "[u]" : "") + QuestEscape(value) + (hover ? "[/u]" : "") + "[/url][/color]";
        }
        if (links.Count > 0)
        {
            var pattern = string.Join('|', links.Keys.OrderByDescending(value => value.Length).Select(System.Text.RegularExpressions.Regex.Escape));
            // 普通文本也转义，避免任务脚本中的方括号成为界面指令。
            var fragments = new List<string>(); var offset = 0;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, pattern))
            { fragments.Add(QuestEscape(text[offset..match.Index])); fragments.Add(Link(match.Value, links[match.Value])); offset = match.Index + match.Length; }
            fragments.Add(QuestEscape(text[offset..])); rendered = string.Concat(fragments);
            foreach (var target in navigation.Targets.Where(target => !text.Contains(target.Name, StringComparison.Ordinal)))
                rendered += "\n" + Link($"前往 {target.Name} · {target.MapTitle}({target.Location.X}:{target.Location.Y})", target);
        }
        return rendered;
    }
    private void RefreshQuestLinks()
    {
        questTracker.Text = string.Join("\n\n", State.Quests.Values.Where(q => q.QuestInfo != null).Take(3).Select(q =>
            QuestEscape(q.QuestInfo.Name + (q.Completed ? " · 可交付" : "")) + "\n" + QuestLinkedText(q.Id, QuestText(q.TaskList))));
        if (questList.Visible) RenderQuestDetail();
    }
    private void RequestQuestNavigation(int quest)
    {
        if (!Assets.SupportsQuestNavigation || !State.Quests.ContainsKey(quest)) return;
        var request = ++questNavigationSequence; questNavigationRequests[quest] = request;
        Send(new C.QuestNavigationRequest { QuestIndex = quest, RequestId = request });
    }
    private void NavigateQuest(int quest, int target)
    {
        if (State.Me?.Dead != false || World.Loading || State.Trading || State.StorageActive) { Log("当前状态不能导航。", ChatType.Hint); return; }
        void Start()
        {
            destination = null; actionTarget = 0; pendingAttack = null; MiniMap.Route = [];
            activeQuestNavigation = (quest, target); questNavigationStarted = false; questNavigationWait = Environment.TickCount64;
            RequestQuestNavigation(quest); Log("任务导航已开始，Esc 取消。", ChatType.System);
        }
        if (!autoBattleEnabled) { Start(); return; }
        pendingGroundMove = null; pendingGroundAction = Start;
        stopBattleConfirmation.DialogText = "停止挂机并导航到任务目标？"; stopBattleConfirmation.PopupCentered();
    }
    private void CancelQuestNavigation() { activeQuestNavigation = null; questNavigationStarted = false; }
    private void QuestNavigationPacket(Packet packet)
    {
        if (packet is S.QuestNavigation navigation)
        {
            if (navigation.OriginMap != State.Map?.MapIndex || questNavigationRequests.GetValueOrDefault(navigation.QuestIndex) != navigation.RequestId)
            { State.QuestNavigation.Remove(navigation.QuestIndex); return; }
            RefreshQuestLinks();
        }
        if (packet is S.MapChanged)
        {
            questNavigationRequests.Clear(); questRevision = -1; questNavigationStarted = false; questNavigationWait = Environment.TickCount64;
        }
        if (packet is S.Death or S.Disconnect or S.LogOutSuccess or S.ReturnToLogin) CancelQuestNavigation();
    }
    private void ProcessQuestNavigation()
    {
        if (activeQuestNavigation is not { } active || State.User == null || World.Loading) return;
        if (State.Trading || State.StorageActive || npc.Visible) { CancelQuestNavigation(); destination = null; MiniMap.Route = []; return; }
        if (!State.QuestNavigation.TryGetValue(active.Quest, out var route) || route.OriginMap != State.Map?.MapIndex ||
            questNavigationRequests.GetValueOrDefault(active.Quest) != route.RequestId)
        {
            if (Environment.TickCount64 - questNavigationWait > 5000) { CancelQuestNavigation(); Log("任务导航读取超时，请重新点击目标。", ChatType.Hint); }
            return;
        }
        var target = route.Targets.FirstOrDefault(target => target.Id == active.Target);
        if (target?.Reachable != true) { CancelQuestNavigation(); Log("任务目标已完成、暂无公开位置或当前无法到达。", ChatType.Hint); return; }
        if (target.MapIndex == route.OriginMap && target.NpcId != 0 && Distance(State.User.Location, target.Location) <= 5)
        { SelectTarget(target.NpcId); return; }
        if (!questNavigationStarted)
        {
            applyingQuestNavigation = true;
            try { SetDestination(target.NextLocation, preferences.DefaultRun); }
            finally { applyingQuestNavigation = false; }
            questNavigationStarted = true; questNavigationWait = Environment.TickCount64; return;
        }
        if (State.User.Location == target.NextLocation && !moving)
        {
            if (target.MapIndex == route.OriginMap) { CancelQuestNavigation(); Log($"已到达 {target.Name} 附近。", ChatType.System); return; }
            if (Environment.TickCount64 - questNavigationWait > 5000) { CancelQuestNavigation(); Log("出口暂时无法通行，任务导航已停止。", ChatType.Hint); return; }
            if (Environment.TickCount64 - questExitTurn > 800) { questExitTurn = Environment.TickCount64; Send(new C.Turn { Direction = State.Me!.Direction }); }
        }
        else if (destination == null && !moving) CancelQuestNavigation();
        else questNavigationWait = Environment.TickCount64;
    }
}
