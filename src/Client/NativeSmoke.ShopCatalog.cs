using Godot;
using C = ClientPackets;
using S = ServerPackets;

namespace Crystal.Native;

public partial class NativeSmoke
{
    private async Task VerifyShopCatalog()
    {
        if (!(Environment.GetEnvironmentVariable("MIR2_CLIENT_CACHE") ?? "").Contains("mir2-native.", StringComparison.Ordinal) || AssetsPortIsDefault())
            throw new InvalidOperationException("商城配置验证仅允许隔离版本。");
        stage = "configured public shop login";
        await Until(() => Client.HandshakeReady); Client.Login("NativeGM", "Native123"); await Packet<S.LoginSuccess>();
        Client.Send(new C.StartGame { CharacterIndex = State.Characters.Single(role => role.Name == "NativeGM").Index });
        await Until(() => State.User != null && !Client.World.Loading);
        await ClickSelectionControl((Control)Client.FindChild("PublicShopButton", true, false));
        await Until(() => State.PublicShopTabs != null && State.PublicShop != null);
        var panel = (Control)Client.FindChild("PublicShopDialog", true, false);
        var tabs = panel.FindChildren("*", "Button", true, false).OfType<Button>().Where(button => button.ThemeTypeVariation == "GameTab").ToArray();
        if (!tabs.Select(tab => tab.Text).SequenceEqual(new[] { "常用药品", "职业补给" })) throw new Exception("商城未显示后台配置的名称和顺序。");
        var rows = (VBoxContainer)Client.FindChild("PublicShopRows", true, false);
        await Until(() => rows.FindChildren("PublicShopItem", "Button", true, false).Count == 1);
        if (rows.GetChildren().OfType<Button>().Any()) throw new Exception("单规格商品仍显示二级展开按钮。");
        var single = rows.FindChildren("PublicShopItem", "Button", true, false).Cast<Button>().Single();
        if (!single.Text.Contains($"{37:N0} 金币")) throw new Exception("商城未使用后台设置单价。");
        var buy = rows.FindChildren("PublicShopBuy1", "LinkButton", true, false).Cast<LinkButton>().Single();
        var confirm = (ConfirmationDialog)Client.FindChild("PublicShopConfirmation", true, false);
        var quantity = confirm.FindChildren("*", "SpinBox", true, false).Cast<SpinBox>().Single();
        var gold = State.User!.Gold; var offer = State.PublicShop!.Items.Single(item => item.Category == 7);
        var count = State.User.Inventory.Where(item => item?.ItemIndex == offer.ItemIndex).Sum(item => item.Count);
        await ClickSelectionControl(buy); await Until(() => confirm.Visible); await ClickSelectionControl(confirm.GetCancelButton());
        if (State.User.Gold != gold) throw new Exception("取消商城购买改变金币。");
        await ClickSelectionControl(buy); await Until(() => confirm.Visible); quantity.Value = 2;
        await ClickSelectionControl(confirm.GetOkButton()); await Until(() => State.User.Gold == gold - 74);
        if (State.User.Inventory.Where(item => item?.ItemIndex == offer.ItemIndex).Sum(item => item.Count) != count + 2) throw new Exception("后台配置商品购买未按真实数量入包。");
        await Screenshot("shop-config-single");
        await ClickSelectionControl(tabs[1]);
        await Until(() => rows.GetChildren().OfType<Button>().Count() == 1);
        await ClickSelectionControl(rows.GetChildren().OfType<Button>().Single());
        await Until(() => rows.FindChildren("PublicShopItem", "Button", true, false).Cast<Button>().Count(button => button.IsVisibleInTree()) == 3);
        await Screenshot("shop-config-variants");
        GD.Print("PASS native configured shop: dynamic tab names/order, direct single product, 3 variants, real mouse cancel/confirm and 74 gold/2 items.");
        await Client.Quit();
    }
}
