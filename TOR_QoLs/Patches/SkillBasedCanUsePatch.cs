using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection;
using TaleWorlds.Library;

namespace TOR_QoLs.Patches
{
    // В TOR-патче RefreshCharacterCanUseItem (InventoryPatches.cs:124) флаг
    // CanCharacterUseItem перетирается race-проверкой, теряя нативный
    // skill-difficulty check. В результате предметы типа "Empire Flintlock Pistol"
    // (требует Gunpowder 30) НЕ подсвечиваются красным, даже если у героя
    // навык не дотягивает.
    //
    // Этот патч идёт ПОСЛЕ TOR-овского (Priority.Last) и AND-ит CanCharacterUseItem
    // со skill-чеком. Если у предмета есть Difficulty > 0 и герой не дотягивает
    // по RelevantSkill — флаг становится false → красная подсветка возвращается.
    [HarmonyPatch(typeof(SPInventoryVM), "RefreshCharacterCanUseItem")]
    public static class SkillBasedCanUsePatch
    {
        private static readonly FieldInfo CurrentCharacterField =
            AccessTools.Field(typeof(SPInventoryVM), "_currentCharacter");

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(SPInventoryVM __instance)
        {
            var character = CurrentCharacterField?.GetValue(__instance) as CharacterObject;
            if (character == null) return;

            Apply(__instance.RightItemListVM, character);
            Apply(__instance.LeftItemListVM, character);
        }

        private static void Apply(MBBindingList<SPItemVM> list, CharacterObject character)
        {
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                var vm = list[i];
                if (vm == null) continue;
                if (!vm.CanCharacterUseItem) continue; // уже красное — не трогаем

                var item = vm.ItemRosterElement.EquipmentElement.Item;
                if (item == null) continue;
                if (item.Difficulty <= 0 || item.RelevantSkill == null) continue;

                int heroSkill = character.GetSkillValue(item.RelevantSkill);
                if (heroSkill < item.Difficulty)
                {
                    vm.CanCharacterUseItem = false;
                }
            }
        }
    }
}
