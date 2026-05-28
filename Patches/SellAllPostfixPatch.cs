using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TOR_QoLs.Patches
{
    /// <summary>
    /// Postfix с Priority.Last на SPInventoryVM.ExecuteSellAllItems (диагональная стрелка "Sell All" в trade).
    /// После vanilla + всех других моды отработали:
    ///   - Wave 1: продать всех lame warhorses / lame mules
    ///   - Recalc, Wave 2: если осталось > target — продать самых дорогих unlocked до target
    /// Native locks (через IViewDataTracker) — пропускаем locked items.
    /// </summary>
    [HarmonyPatch(typeof(SPInventoryVM), nameof(SPInventoryVM.ExecuteSellAllItems))]
    public static class SellAllPostfixPatch
    {
        private const float WarhorseBufferPct = 0.15f;
        private const float MuleTargetPct = 0.45f;
        private const float SellPriceFloor = 0.7f;
        private const string LameModifierId = "lame_horse";

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(SPInventoryVM __instance)
        {
            try
            {
                var settlement = Settlement.CurrentSettlement;
                if (settlement == null) return;
                if (!settlement.IsTown && !settlement.IsVillage) return;

                var party = MobileParty.MainParty;
                if (party == null) return;

                int totalMen = party.MemberRoster.TotalManCount;
                int unmountedInf = party.Party.NumberOfMenWithoutHorse;
                int targetWarhorses = unmountedInf + (int)Math.Ceiling(WarhorseBufferPct * totalMen);
                int targetMules = (int)(MuleTargetPct * totalMen);

                int totalEarned = 0;
                int lameSold = 0;
                int expensiveSold = 0;

                // Native locks
                var tracker = Campaign.Current.GetCampaignBehavior<IViewDataTracker>();
                var lockedSet = new HashSet<string>(tracker?.GetInventoryLocks() ?? Enumerable.Empty<string>());

                // Warhorses
                (int earned1, int lame1, int expensive1) = ProcessCategory(party, settlement, lockedSet,
                    targetWarhorses, isPackAnimalCategory: false);
                // Mules
                (int earned2, int lame2, int expensive2) = ProcessCategory(party, settlement, lockedSet,
                    targetMules, isPackAnimalCategory: true);

                totalEarned = earned1 + earned2;
                lameSold = lame1 + lame2;
                expensiveSold = expensive1 + expensive2;

                if (totalEarned > 0)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        $"Sell-all cleanup: {lameSold} lame + {expensiveSold} expensive sold, earned {totalEarned}g",
                        Color.FromUint(0xFF66FF66u)));
                }
            }
            catch (Exception ex)
            {
                FileLog.Log("[TOR_QoLs] SellAllPostfix threw: " + ex);
            }
        }

        private static (int earned, int lameSold, int expensiveSold) ProcessCategory(
            MobileParty party, Settlement settlement, HashSet<string> lockedSet,
            int target, bool isPackAnimalCategory)
        {
            int earned = 0;
            int lameSold = 0;
            int expensiveSold = 0;

            // Wave 1: все lame подряд (не до target — все)
            var lameCandidates = SnapshotCategory(party, isPackAnimalCategory, requireLame: true, lockedSet);
            foreach (var element in lameCandidates)
            {
                int price = GetSellPrice(party, settlement, element.EquipmentElement);
                if (price < element.EquipmentElement.Item.Value * SellPriceFloor) continue;
                int sellCount = element.Amount;
                int total = price * sellCount;

                party.ItemRoster.AddToCounts(element.EquipmentElement, -sellCount);
                settlement.ItemRoster.AddToCounts(element.EquipmentElement, sellCount);
                GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, total);
                earned += total;
                lameSold += sellCount;
            }

            // Recalc current count
            int currentCount = isPackAnimalCategory
                ? party.ItemRoster.NumberOfPackAnimals
                : party.ItemRoster.NumberOfMounts;
            int excess = currentCount - target;
            if (excess <= 0) return (earned, lameSold, 0);

            // Wave 2: самые дорогие unlocked, до target
            var expensiveCandidates = SnapshotCategory(party, isPackAnimalCategory, requireLame: false, lockedSet)
                .OrderByDescending(e => GetSellPrice(party, settlement, e.EquipmentElement))
                .ToList();

            foreach (var element in expensiveCandidates)
            {
                if (excess <= 0) break;
                int price = GetSellPrice(party, settlement, element.EquipmentElement);
                if (price < element.EquipmentElement.Item.Value * SellPriceFloor) continue;

                int sellCount = Math.Min(element.Amount, excess);
                int total = price * sellCount;

                party.ItemRoster.AddToCounts(element.EquipmentElement, -sellCount);
                settlement.ItemRoster.AddToCounts(element.EquipmentElement, sellCount);
                GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, total);
                earned += total;
                expensiveSold += sellCount;
                excess -= sellCount;
            }

            return (earned, lameSold, expensiveSold);
        }

        private static List<ItemRosterElement> SnapshotCategory(MobileParty party,
            bool isPackAnimalCategory, bool requireLame, HashSet<string> lockedSet)
        {
            var result = new List<ItemRosterElement>();
            foreach (var element in party.ItemRoster)
            {
                var hc = element.EquipmentElement.Item?.HorseComponent;
                if (hc == null) continue;
                if (isPackAnimalCategory ? !hc.IsPackAnimal : !hc.IsMount) continue;

                bool isLame = element.EquipmentElement.ItemModifier?.StringId == LameModifierId;
                if (requireLame && !isLame) continue;

                // Skip locked
                if (IsLocked(element.EquipmentElement, lockedSet)) continue;

                result.Add(element);
            }
            return result;
        }

        private static bool IsLocked(EquipmentElement element, HashSet<string> lockedSet)
        {
            if (lockedSet.Count == 0) return false;
            try
            {
                var lockId = TaleWorlds.CampaignSystem.ViewModelCollection.CampaignUIHelper.GetItemLockStringID(element);
                return lockedSet.Contains(lockId);
            }
            catch
            {
                return false;
            }
        }

        private static int GetSellPrice(MobileParty party, Settlement settlement, EquipmentElement element)
        {
            if (settlement.Town != null)
                return settlement.Town.GetItemPrice(element, party, isSelling: true);
            return element.Item?.Value ?? 0;
        }
    }
}
