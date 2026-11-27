using Server.MirEnvir;

namespace Server.Helpers;

public static class MaterialInventoryRules
{
    public static bool Contains(ItemInfo item, Envir world) => item != null &&
        (item.Type is ItemType.Quest or ItemType.CraftingMaterial or ItemType.Ore ||
         !item.IsEquipment && item.Type is not (ItemType.Potion or ItemType.Book or ItemType.Amulet or ItemType.Bait or ItemType.SealedHero) &&
         (world.QuestInfoList.Any(quest => quest.CarryItems.Concat(quest.ItemTasks).Any(task => task.Item == item)) ||
          world.RecipeInfoList.Any(recipe => recipe.Ingredients?.Any(ingredient => ingredient.Info == item) == true)));
}
