using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using TOR_Core.Items;

namespace TOR_QoLs.Patches
{
    // Фикс TOR-бага: HasMagicItemId возвращает false для модифицированных
    // magic-предметов (Сбалансированное / Заточенное / Лучшее / ...), и они
    // теряют фиолетовую подсветку в инвентаре. Трейты при этом наследуются
    // от base item'а и работают как надо — поломана только подсветка.
    //
    // Оригинальный баг (ExtendedItemObjectManager.HasMagicItemId, ветка с модификатором):
    //     return entrys.AnyQ(entry => entry.Key.EndsWith(modifier.StringId));
    // Base id вида "tor_empire_weapon_staff_bw_001" никогда не оканчивается
    // на StringId модификатора ("balanced", "sharp" и т.п.) → всегда false.
    //
    // Правильная проверка: у base entry есть ItemTraits.
    [HarmonyPatch(typeof(ExtendedItemObjectManager), nameof(ExtendedItemObjectManager.HasMagicItemId))]
    public static class FixMagicItemHighlightPatch
    {
        // _itemToInfoMap — private static в ExtendedItemObjectManager.
        // Один раз резолвим через reflection, дальше быстрый доступ.
        private static readonly FieldInfo _mapField =
            AccessTools.Field(typeof(ExtendedItemObjectManager), "_itemToInfoMap");

        [HarmonyPrefix]
        public static bool Prefix(string uiStringID, ref bool __result)
        {
            if (_mapField == null) return true;     // не нашли поле — пусть бежит оригинал
            if (!(_mapField.GetValue(null) is Dictionary<string, ExtendedItemObjectProperties> map))
                return true;

            ItemModifier modifier = MBObjectManager.Instance
                .GetObjectTypeList<ItemModifier>()
                .FirstOrDefault(x => uiStringID.EndsWith(x.StringId));

            if (modifier == null)
            {
                // Без модификатора — оригинальная логика работает корректно.
                __result = map.TryGetValue(uiStringID, out var entry)
                           && entry.ItemTraits.Count > 0;
            }
            else
            {
                // С модификатором: ищем base entry, чей Key — подстрока uiStringID
                // (т.е. uiStringID начинается с base id), и у которого есть ItemTraits.
                __result = false;
                foreach (var kv in map)
                {
                    if (uiStringID.Contains(kv.Key) && kv.Value.ItemTraits.Count > 0)
                    {
                        __result = true;
                        break;
                    }
                }
            }

            return false;   // оригинал не запускаем
        }
    }
}
