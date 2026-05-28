using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace TOR_QoLs.Patches
{
    // Подменяет источник веса брони в TORAbilityModel.GetWindsRechargeRate:
    //   baseCharacter.Equipment  →  baseCharacter.HeroObject.CivilianEquipment
    //
    // Влияет ТОЛЬКО на эту функцию (расчёт регена винда). Другие функции,
    // читающие CharacterObject.Equipment, продолжают работать как раньше.
    //
    // Существующие иммунитеты к weight-штрафу (Arkayne / WardenOfTalsyn keystones
    // у MainHero, компаньоны-вампиры) сохраняются — мы патчим только ветку,
    // которая реально читает вес.
    [HarmonyPatch]
    public static class CivilianWindsWeightPatch
    {
        static MethodBase TargetMethod()
        {
            return AccessTools.Method("TOR_Core.Models.TORAbilityModel:GetWindsRechargeRate");
        }

        // Диагностика отключена. Если нужно дебажить — раскомментируй.
        // [HarmonyPostfix]
        // static void Postfix(CharacterObject baseCharacter, float __result) { ... }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            // Equipment-геттер живёт на BasicCharacterObject (родитель CharacterObject).
            // PropertyGetter ищем по обоим — какой найдётся, того и заматчим.
            var equipmentGetterChar = AccessTools.PropertyGetter(typeof(CharacterObject), "Equipment");
            var equipmentGetterBase = AccessTools.PropertyGetter(typeof(BasicCharacterObject), "Equipment");
            var heroObjectGetter = AccessTools.PropertyGetter(typeof(CharacterObject), nameof(CharacterObject.HeroObject));
            var civilianGetter = AccessTools.PropertyGetter(typeof(Hero), nameof(Hero.CivilianEquipment));

            bool patched = false;
            foreach (var instr in instructions)
            {
                bool isEquipmentGetter =
                    (equipmentGetterChar != null && instr.Calls(equipmentGetterChar)) ||
                    (equipmentGetterBase != null && instr.Calls(equipmentGetterBase));

                if (!patched && isEquipmentGetter)
                {
                    // baseCharacter уже на стэке от предыдущего ldarg.1
                    // get_Equipment → get_HeroObject; затем добавляем get_CivilianEquipment
                    yield return new CodeInstruction(OpCodes.Callvirt, heroObjectGetter);
                    yield return new CodeInstruction(OpCodes.Callvirt, civilianGetter);
                    patched = true;
                }
                else
                {
                    yield return instr;
                }
            }
            if (!patched)
            {
                FileLog.Log("[TOR_QoLs] WARNING: GetWindsRechargeRate transpiler pattern NOT FOUND. " +
                            "Weight-source patch is INACTIVE. " +
                            $"equipmentGetterChar={equipmentGetterChar}, equipmentGetterBase={equipmentGetterBase}");
            }
            else
            {
                FileLog.Log("[TOR_QoLs] OK: weight-source transpiler applied.");
            }
        }
    }
}
