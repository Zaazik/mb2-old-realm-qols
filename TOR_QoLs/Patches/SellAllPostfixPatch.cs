using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection.Inventory;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TOR_QoLs.Behaviors;

namespace TOR_QoLs.Patches
{
    /// <summary>
    /// Prefix returns false на SPInventoryVM.TransferAll(bool isBuy) — internal метод
    /// который дёргают и ExecuteSellAllItems, и ExecuteBuyAllItems. Перехватываем
    /// только sell (isBuy=false), buy идёт vanilla.
    ///
    /// Vanilla TransferAll(false) продаёт всё unlocked включая всех лошадей — не QoL.
    /// Заменяем целиком: продаём всё non-horse/mule как vanilla, а лошади/мулы — через Wave 1+2:
    ///   - Wave 1: продать ВСЕХ lame (без оглядки на target)
    ///   - Wave 2: если после Wave 1 ещё > target — продать самых дорогих unlocked до target
    /// Не наш settlement (None/Hideout) — fallback на vanilla (return true).
    ///
    /// Priority.First — TOR_Core тоже patch'ит ExecuteSellAllItems Prefix'ом который
    /// возвращает false (стоп цепочки). Без Priority.First наш Prefix не успевает запуститься.
    /// </summary>
    [HarmonyPatch]
    public static class SellAllPostfixPatch
    {
        // Все формулы и floor'ы — в TOR_QoLs.Behaviors.TraderMath.
        private const string LameModifierId = "lame_horse";
        private static readonly bool Diagnostics = true;

        private static readonly FieldInfo InventoryLogicField =
            AccessTools.Field(typeof(SPInventoryVM), "_inventoryLogic");
        private static readonly FieldInfo CurrentCharacterField =
            AccessTools.Field(typeof(SPInventoryVM), "_currentCharacter");
        private static readonly MethodInfo RefreshInfoMethod =
            AccessTools.Method(typeof(SPInventoryVM), "RefreshInformationValues");

        public static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(typeof(SPInventoryVM), "TransferAll", new[] { typeof(bool) });
            FileLog.Log($"[SellAll] TargetMethod (TransferAll) resolved: {(method == null ? "NULL" : method.DeclaringType.FullName + "." + method.Name)}");
            return method;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(SPInventoryVM __instance, bool isBuy)
        {
            FileLog.Log($"[SellAll] TransferAll prefix invoked isBuy={isBuy}");
            if (isBuy) return true;  // Buy All — vanilla
            try
            {
                return DoSellAll(__instance);
            }
            catch (Exception ex)
            {
                FileLog.Log("[SellAll] TransferAll prefix threw, fallback to vanilla: " + ex);
                return true;
            }
        }

        // Public entry — вызывается из SellAllExecutePatch на случай если TransferAll inlined.
        public static bool RunForExecuteSellAll(SPInventoryVM vm)
        {
            try
            {
                return DoSellAll(vm);
            }
            catch (Exception ex)
            {
                FileLog.Log("[SellAll] RunForExecuteSellAll threw, fallback to vanilla: " + ex);
                return true;
            }
        }

        private static bool DoSellAll(SPInventoryVM vm)
        {
            var settlement = Settlement.CurrentSettlement;
            if (settlement == null || (!settlement.IsTown && !settlement.IsVillage))
            {
                Diag("not in town/village → fallback vanilla");
                return true;
            }
            var party = MobileParty.MainParty;
            if (party == null)
            {
                Diag("no MainParty → fallback vanilla");
                return true;
            }

            var inventoryLogic = InventoryLogicField.GetValue(vm) as InventoryLogic;
            var currentChar = CurrentCharacterField.GetValue(vm) as CharacterObject;
            if (inventoryLogic == null || currentChar == null)
            {
                Diag("reflection failed → fallback vanilla");
                return true;
            }

            int totalMen = party.MemberRoster.TotalManCount;
            int unmountedInf = party.Party.NumberOfMenWithoutHorse;
            int targetWarhorses = TraderMath.TargetWarhorses(totalMen, unmountedInf);
            int warhorsesNow = party.ItemRoster.NumberOfMounts;
            int mulesNow = party.ItemRoster.NumberOfPackAnimals;

            float dailyConsumption = TraderMath.NormalizeDailyConsumption(Math.Abs(party.FoodChange), totalMen);
            int requiredFood = TraderMath.RequiredFood(dailyConsumption);

            Diag($"=== SellAll === wh={warhorsesNow}/target={targetWarhorses} mu={mulesNow} foodReq={requiredFood}");

            var warhorseCandidates = new List<SPItemVM>();
            var muleCandidates = new List<SPItemVM>();
            var foodCandidates = new List<SPItemVM>();
            var nonFoodOther = new List<SPItemVM>();

            foreach (SPItemVM itemVM in vm.RightItemListVM)
            {
                if (itemVM == null) continue;
                if (itemVM.IsFiltered || itemVM.IsLocked || !itemVM.IsTransferable) continue;
                var item = itemVM.ItemRosterElement.EquipmentElement.Item;
                if (item == null) continue;
                var hc = item.HorseComponent;
                if (hc != null && !hc.IsLiveStock && hc.IsPackAnimal) { muleCandidates.Add(itemVM); continue; }
                if (hc != null && !hc.IsLiveStock && hc.IsMount) { warhorseCandidates.Add(itemVM); continue; }
                if (item.IsFood) { foodCandidates.Add(itemVM); continue; }
                nonFoodOther.Add(itemVM);  // включает livestock, equipment, прочее
            }

            Diag($"  candidates: nonFood={nonFoodOther.Count} food={foodCandidates.Count} wh={warhorseCandidates.Count} mu={muleCandidates.Count}");

            var commands = new List<TransferCommand>();

            // Non-food other (включая livestock): продаём всё unlocked.
            foreach (var itemVM in nonFoodOther)
            {
                int amount = itemVM.ItemRosterElement.Amount;
                if (amount <= 0) continue;
                commands.Add(TransferCommand.Transfer(
                    amount,
                    InventoryLogic.InventorySide.PlayerInventory,
                    InventoryLogic.InventorySide.OtherInventory,
                    itemVM.ItemRosterElement,
                    EquipmentIndex.None, EquipmentIndex.None, currentChar));
            }

            // Food: оставить минимум requiredFood total, по 1 каждого вида для морал-diversity.
            int currentFoodTotal = foodCandidates.Sum(v => v.ItemRosterElement.Amount);
            int toSellFood = Math.Max(0, currentFoodTotal - requiredFood);
            Diag($"  food: total={currentFoodTotal} required={requiredFood} toSell={toSellFood}");
            if (toSellFood > 0)
            {
                var sortedFood = foodCandidates.OrderByDescending(v => v.ItemRosterElement.Amount).ToList();
                foreach (var fitem in sortedFood)
                {
                    if (toSellFood <= 0) break;
                    int amount = fitem.ItemRosterElement.Amount;
                    int maxSellable = Math.Max(0, amount - 1); // оставить 1 каждого типа
                    int sellCount = Math.Min(maxSellable, toSellFood);
                    if (sellCount <= 0) continue;
                    commands.Add(TransferCommand.Transfer(
                        sellCount,
                        InventoryLogic.InventorySide.PlayerInventory,
                        InventoryLogic.InventorySide.OtherInventory,
                        fitem.ItemRosterElement,
                        EquipmentIndex.None, EquipmentIndex.None, currentChar));
                    toSellFood -= sellCount;
                    Diag($"  food sold {sellCount}× {fitem.ItemRosterElement.EquipmentElement.Item.StringId}");
                }
            }

            int whLameSold = 0, whExpSold = 0;
            int muLameSold = 0, muExpSold = 0;

            ProcessHorseCategory(warhorseCandidates, warhorsesNow, targetWarhorses,
                settlement, currentChar, commands, ref whLameSold, ref whExpSold, "warhorses");

            // Mule target — sweet-spot. Livestock после SellAll = 0 (продан в nonFoodOther).
            int warhorsesAfter = warhorsesNow - whLameSold - whExpSold;
            int excessWarhorsesAfter = TraderMath.ExcessWarhorses(warhorsesAfter, unmountedInf);
            int desiredMules = TraderMath.DesiredMules(totalMen);
            int muleRoom = TraderMath.MuleRoom(totalMen, livestockNow: 0, excessWarhorses: excessWarhorsesAfter);
            int targetMules = TraderMath.TargetMules(desiredMules, muleRoom);
            Diag($"  mule sweet-spot: whAfter={warhorsesAfter} excessWh={excessWarhorsesAfter} desired={desiredMules} room={muleRoom} → target={targetMules}");

            ProcessHorseCategory(muleCandidates, mulesNow, targetMules,
                settlement, currentChar, commands, ref muLameSold, ref muExpSold, "mules");

            if (commands.Count == 0)
            {
                Diag("  nothing to sell");
                return false;
            }

            vm.IsRefreshed = false;
            inventoryLogic.AddTransferCommands(commands);
            RefreshInfoMethod?.Invoke(vm, null);
            vm.ExecuteRemoveZeroCounts();
            vm.IsRefreshed = true;

            Diag($"  done: nonFood={nonFoodOther.Count} food={foodCandidates.Count}, wh lame={whLameSold} exp={whExpSold}, mu lame={muLameSold} exp={muExpSold}");

            int horsesSold = whLameSold + whExpSold;
            int mulesSold = muLameSold + muExpSold;
            if (horsesSold + mulesSold > 0)
            {
                var parts = new List<string>();
                if (horsesSold > 0) parts.Add($"sold {horsesSold} horses");
                if (mulesSold > 0) parts.Add($"sold {mulesSold} mules");
                InformationManager.DisplayMessage(new InformationMessage(
                    "Sell-all: " + string.Join(", ", parts),
                    Color.FromUint(0xFF66FF66u)));
            }

            return false;
        }

        private static void ProcessHorseCategory(List<SPItemVM> candidates, int currentCount, int target,
            Settlement settlement, CharacterObject currentChar, List<TransferCommand> commands,
            ref int lameSold, ref int expensiveSold, string label)
        {
            // Wave 1: all lame
            foreach (var itemVM in candidates.ToList())
            {
                var rosterEl = itemVM.ItemRosterElement;
                var element = rosterEl.EquipmentElement;
                bool isLame = element.ItemModifier?.StringId == LameModifierId;
                if (!isLame) continue;

                int price = GetSellPrice(settlement, element);
                if (price < element.Item.Value * TraderMath.SellFloorWave2)
                {
                    Diag($"  [{label}] skip lame {element.Item.StringId}: price={price} < floor");
                    continue;
                }

                int amount = rosterEl.Amount;
                if (amount <= 0) continue;
                commands.Add(TransferCommand.Transfer(
                    amount,
                    InventoryLogic.InventorySide.PlayerInventory,
                    InventoryLogic.InventorySide.OtherInventory,
                    rosterEl,
                    EquipmentIndex.None, EquipmentIndex.None, currentChar));
                lameSold += amount;
                currentCount -= amount;
                candidates.Remove(itemVM);
                Diag($"  [{label}] wave1 sold {amount}× {element.Item.StringId} @ {price}");
            }

            // Wave 2: most expensive down to target
            int excess = currentCount - target;
            if (excess <= 0)
            {
                Diag($"  [{label}] wave2 skip: count={currentCount} <= target={target}");
                return;
            }

            var sortedByPrice = candidates
                .Select(v => new
                {
                    Vm = v,
                    Price = GetSellPrice(settlement, v.ItemRosterElement.EquipmentElement)
                })
                .OrderByDescending(x => x.Price)
                .ToList();

            foreach (var entry in sortedByPrice)
            {
                if (excess <= 0) break;
                var rosterEl = entry.Vm.ItemRosterElement;
                var element = rosterEl.EquipmentElement;
                if (entry.Price < element.Item.Value * TraderMath.SellFloorWave2)
                {
                    Diag($"  [{label}] skip exp {element.Item.StringId}: price={entry.Price} < floor");
                    continue;
                }
                int amount = rosterEl.Amount;
                int sellNow = Math.Min(amount, excess);
                if (sellNow <= 0) continue;
                commands.Add(TransferCommand.Transfer(
                    sellNow,
                    InventoryLogic.InventorySide.PlayerInventory,
                    InventoryLogic.InventorySide.OtherInventory,
                    rosterEl,
                    EquipmentIndex.None, EquipmentIndex.None, currentChar));
                expensiveSold += sellNow;
                excess -= sellNow;
                Diag($"  [{label}] wave2 sold {sellNow}× {element.Item.StringId} @ {entry.Price}");
            }
        }

        private static int GetSellPrice(Settlement settlement, EquipmentElement element)
        {
            if (settlement.Town != null)
                return settlement.Town.GetItemPrice(element, MobileParty.MainParty, isSelling: true);
            return element.Item?.Value ?? 0;
        }

        private static void Diag(string msg)
        {
            if (!Diagnostics) return;
            FileLog.Log("[SellAll] " + msg);
        }
    }

    /// <summary>
    /// Fallback-патч на public ExecuteSellAllItems — на случай если TransferAll
    /// inlined компилятором и наш main patch не цепляется.
    /// UI кнопка XML дёргает Command.Click="ExecuteSellAllItems" напрямую.
    /// </summary>
    [HarmonyPatch]
    public static class SellAllExecutePatch
    {
        public static MethodBase TargetMethod()
        {
            var method = AccessTools.Method(typeof(SPInventoryVM), nameof(SPInventoryVM.ExecuteSellAllItems));
            FileLog.Log($"[SellAll] TargetMethod (ExecuteSellAllItems) resolved: {(method == null ? "NULL" : method.DeclaringType.FullName + "." + method.Name)}");
            return method;
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(SPInventoryVM __instance)
        {
            FileLog.Log("[SellAll] ExecuteSellAllItems prefix invoked");
            return SellAllPostfixPatch.RunForExecuteSellAll(__instance);
        }
    }
}
