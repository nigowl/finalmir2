using Godot;
using C = ClientPackets;

public partial class Main
{
    private PopupMenu attackModes = null!;

    private void BuildAttackModes()
    {
        attackModes = new PopupMenu { Name = "AttackModes" }; gameMenu.AddChild(attackModes);
        foreach (var mode in new[] { AttackMode.Peace, AttackMode.Group, AttackMode.Guild, AttackMode.Couple, AttackMode.Mentor, AttackMode.EnemyGuild, AttackMode.RedBrown, AttackMode.All })
            attackModes.AddRadioCheckItem(mode.ToLocalizedString(), (int)mode);
        gameMenu.AddSubmenuNodeItem("攻击模式", attackModes, 8);
        attackModes.IdPressed += id =>
        {
            if (HandshakeReady && State.User != null && id >= 0 && id < Assets.AttackModes)
                Send(new C.ChangeAMode { Mode = (AttackMode)id });
        };
        gameMenu.AboutToPopup += RefreshAttackModes;
        attackModes.AboutToPopup += RefreshAttackModes;
    }

    private void RefreshAttackModes()
    {
        for (var index = 0; index < attackModes.ItemCount; index++)
        {
            var mode = (AttackMode)attackModes.GetItemId(index);
            attackModes.SetItemChecked(index, mode == State.CurrentAttackMode);
            attackModes.SetItemDisabled(index, (int)mode >= Assets.AttackModes);
            attackModes.SetItemTooltip(index, mode switch
            {
                AttackMode.Couple => "保护配偶及其宠物；其他目标仍受原地图规则限制。",
                AttackMode.Mentor => "保护师父/徒弟及其宠物；其他目标仍受原地图规则限制。",
                _ => $"{mode.ToLocalizedString()}攻击模式，以服务器回执为准。"
            });
        }
        // PopupMenu 的加速键与子菜单箭头共用右侧位置，提示写入标题以免重叠。
        gameMenu.SetItemText(gameMenu.GetItemIndex(8), $"攻击模式：{State.CurrentAttackMode.ToLocalizedString()}（Ctrl+H）");
    }
}
