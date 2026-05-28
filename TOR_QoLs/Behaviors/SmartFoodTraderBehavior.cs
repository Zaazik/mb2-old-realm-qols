using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace TOR_QoLs.Behaviors
{
    /// <summary>
    /// При входе MainParty в Town/Village:
    /// 1. Food: докупка до 10 дней / продажа излишка свыше 15 дней (минимум 1 каждого типа)
    /// 2. Livestock: для каждой единицы — выбор sell vs butcher по выгоде
    /// 3. Warhorses: продаём только lame/sick если warhorses > target; красное warning если < U
    /// 4. Mules: продаём только lame/sick если mules > target_mules
    /// Цены: buy ≤ 1.5×value, sell ≥ 0.7×value
    /// </summary>
    public class SmartFoodTraderBehavior : CampaignBehaviorBase
    {
        // Все формулы и константы — в TraderMath (pure, покрыты тестами).
        // Здесь остаются только маркеры и переключатели диагностики.

        // Lame status marker
        private const string LameModifierId = "lame_horse";

        // Diagnostics toggle. true → подробные FileLog в HarmonyLog.txt.
        private static readonly bool Diagnostics = true;

        public override void RegisterEvents()
        {
            CampaignEvents.SettlementEntered.AddNonSerializedListener(this, OnSettlementEntered);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnSettlementEntered(MobileParty party, Settlement settlement, Hero hero)
        {
            if (party != MobileParty.MainParty) return;
            if (settlement == null) return;
            if (!settlement.IsTown && !settlement.IsVillage) return;
            if (Hero.MainHero == null) return;

            try
            {
                Process(party, settlement);
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage(
                    "[TOR_QoLs] SmartFood threw: " + ex.Message,
                    Color.FromUint(0xFFFF6666u)));
            }
        }

        private void Process(MobileParty party, Settlement settlement)
        {
            var stats = new TradeStats();

            int totalMen = party.MemberRoster.TotalManCount;
            float dailyConsumption = TraderMath.NormalizeDailyConsumption(Math.Abs(party.FoodChange), totalMen);

            int unmountedInf = party.Party.NumberOfMenWithoutHorse;
            int warhorses = party.ItemRoster.NumberOfMounts;
            int mules = party.ItemRoster.NumberOfPackAnimals;

            int targetWarhorses = TraderMath.TargetWarhorses(totalMen, unmountedInf);
            int desiredMules = TraderMath.DesiredMules(totalMen);

            int currentFoodTotal = SumFood(party);
            float requiredFood = dailyConsumption * TraderMath.FoodMinDays;

            var lockedSet = GetUserLocks();

            Diag($"=== entry settlement={settlement.StringId} ({(settlement.IsTown ? "town" : "village")}) ===");
            Diag($"  totalMen={totalMen} unmountedInf={unmountedInf} warhorses={warhorses} mules={mules} locks={lockedSet.Count}");
            Diag($"  dailyFood={dailyConsumption:F2} currentFood={currentFoodTotal} required={requiredFood:F0} gold={Hero.MainHero.Gold}");
            Diag($"  targets: warhorses={targetWarhorses} desiredMules={desiredMules}");

            ProcessLivestock(party, settlement, stats, currentFoodTotal, requiredFood, lockedSet);

            ProcessFood(party, settlement, stats, dailyConsumption, lockedSet);

            // Warhorses: respect locks. Wave 1 (lame) + Wave 2 (cheapest non-lame до target).
            ProcessHerdTrim(party, settlement, stats, warhorses, targetWarhorses,
                isPackAnimalCategory: false, label: "warhorses", lockedSet: lockedSet, ignoreLocks: false);

            // Mule target — sweet spot priority: если livestock + excess warhorses уже
            // заполнили herd-budget (>= totalMen) — мулов до 0. Иначе up to desiredMules.
            int livestockNow = SumLivestock(party);
            int warhorsesNow = party.ItemRoster.NumberOfMounts;
            int excessWarhorsesNow = TraderMath.ExcessWarhorses(warhorsesNow, unmountedInf);
            int muleRoom = TraderMath.MuleRoom(totalMen, livestockNow, excessWarhorsesNow);
            int targetMules = TraderMath.TargetMules(desiredMules, muleRoom);
            Diag($"  mule sweet-spot: livestock={livestockNow} excessWh={excessWarhorsesNow} room={muleRoom} → target={targetMules}");

            // Pack animals: lock игнор. Wave 1 (lame) + Wave 2 (cheapest до target).
            ProcessHerdTrim(party, settlement, stats, mules, targetMules,
                isPackAnimalCategory: true, label: "mules", lockedSet: lockedSet, ignoreLocks: true);

            int finalWarhorses = party.ItemRoster.NumberOfMounts;
            int finalFoodSum = SumFood(party);
            int finalDays = (int)(finalFoodSum / Math.Max(dailyConsumption, 0.1f));

            Diag($"  final: warhorses={finalWarhorses} food={finalFoodSum} (~{finalDays}d) net={stats.Earned - stats.Spent}");

            EmitMessages(stats, finalDays, finalFoodSum, (int)Math.Ceiling(requiredFood), finalWarhorses, unmountedInf);
        }

        private static int SumFood(MobileParty party)
        {
            return party.ItemRoster
                .Where(e => e.EquipmentElement.Item?.IsFood == true)
                .Sum(e => e.Amount);
        }

        private static int SumLivestock(MobileParty party)
        {
            return party.ItemRoster
                .Where(e => e.EquipmentElement.Item?.HorseComponent?.IsLiveStock == true)
                .Sum(e => e.Amount);
        }

        // ----------------- Livestock pass -----------------

        private void ProcessLivestock(MobileParty party, Settlement settlement, TradeStats stats,
                                       int currentFoodTotal, float requiredFood, HashSet<string> lockedSet)
        {
            var livestockSnapshot = new List<ItemRosterElement>();
            foreach (var element in party.ItemRoster)
            {
                if (element.EquipmentElement.Item?.HorseComponent?.IsLiveStock != true) continue;
                if (IsLocked(element.EquipmentElement, lockedSet))
                {
                    Diag($"  livestock locked skip: {element.EquipmentElement.Item.StringId}");
                    continue;
                }
                livestockSnapshot.Add(element);
            }
            Diag($"[livestock] kinds={livestockSnapshot.Count} currentFood={currentFoodTotal} required={requiredFood:F0}");
            if (livestockSnapshot.Count == 0) return;

            int meatSellPrice = DefaultItems.Meat != null
                ? GetSellPrice(party, settlement, new EquipmentElement(DefaultItems.Meat))
                : 0;
            int meatBuyPrice = DefaultItems.Meat != null
                ? GetBuyPrice(party, settlement, new EquipmentElement(DefaultItems.Meat))
                : 0;
            int hideSellPrice = DefaultItems.Hides != null
                ? GetSellPrice(party, settlement, new EquipmentElement(DefaultItems.Hides))
                : 0;

            int remainingDeficit = Math.Max(0, (int)Math.Ceiling(requiredFood - currentFoodTotal));

            foreach (var element in livestockSnapshot)
            {
                var item = element.EquipmentElement.Item;
                var horseComp = item.HorseComponent;
                int amount = element.Amount;
                if (amount <= 0) continue;

                int sellPrice = GetSellPrice(party, settlement, element.EquipmentElement);
                int meatCount = horseComp.MeatCount;
                int hideCount = horseComp.HideCount;
                float floor = GetSellFloor(item);
                bool sellOk = sellPrice >= item.Value * floor;

                Diag($"  {item.StringId} ×{amount}: sellPrice={sellPrice} (floor={item.Value * floor:F0}) meat={meatCount}@{meatSellPrice}/{meatBuyPrice} hide={hideCount}@{hideSellPrice} deficit={remainingDeficit}");

                var (butcherCount, sellCount, newDeficit) = TraderMath.SimulateLivestockBatch(
                    amount, meatCount, hideCount,
                    sellPrice, meatBuyPrice, meatSellPrice, hideSellPrice,
                    remainingDeficit, sellOk);

                if (butcherCount > 0)
                {
                    party.ItemRoster.AddToCounts(element.EquipmentElement, -butcherCount);
                    if (DefaultItems.Meat != null && meatCount > 0)
                        party.ItemRoster.AddToCounts(DefaultItems.Meat, butcherCount * meatCount);
                    if (DefaultItems.Hides != null && hideCount > 0)
                        party.ItemRoster.AddToCounts(DefaultItems.Hides, butcherCount * hideCount);
                    stats.LivestockButchered += butcherCount;
                    stats.MeatGained += butcherCount * meatCount;
                    stats.HidesGained += butcherCount * hideCount;
                    Diag($"  → BUTCHER {butcherCount}× → +{butcherCount * meatCount} meat / +{butcherCount * hideCount} hides");
                }

                if (sellCount > 0)
                {
                    int totalEarn = sellPrice * sellCount;
                    party.ItemRoster.AddToCounts(element.EquipmentElement, -sellCount);
                    settlement.ItemRoster.AddToCounts(element.EquipmentElement, sellCount);
                    GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, totalEarn, disableNotification: true);
                    stats.Earned += totalEarn;
                    stats.LivestockSold += sellCount;
                    Diag($"  → SELL {sellCount}× @ {sellPrice} = {totalEarn}g");
                }

                if (butcherCount == 0 && sellCount == 0)
                    Diag($"  → SKIP all (sellPrice={sellPrice} < floor={item.Value * floor:F0}, butcher <= sell)");

                remainingDeficit = newDeficit;
            }
        }

        // ----------------- Food pass -----------------

        private void ProcessFood(MobileParty party, Settlement settlement, TradeStats stats, float dailyConsumption, HashSet<string> lockedSet)
        {
            float requiredFood = dailyConsumption * TraderMath.FoodMinDays;
            float bufferFood = TraderMath.BufferFood(dailyConsumption);

            var partyFood = new List<ItemRosterElement>();
            int currentFoodTotal = 0;
            int totalDistinctKinds = 0;  // all food entries (locked + unlocked) для diversity check
            foreach (var element in party.ItemRoster)
            {
                if (element.EquipmentElement.Item?.IsFood != true) continue;
                currentFoodTotal += element.Amount;
                totalDistinctKinds++;
                if (IsLocked(element.EquipmentElement, lockedSet))
                {
                    Diag($"  food locked skip: {element.EquipmentElement.Item.StringId} (count in total but not sold)");
                    continue;
                }
                partyFood.Add(element);
            }

            Diag($"[food] current={currentFoodTotal} required={requiredFood:F0} buffer={bufferFood:F0} totalKinds={totalDistinctKinds} unlockedKinds={partyFood.Count}");

            if (currentFoodTotal < requiredFood)
            {
                int needed = (int)Math.Ceiling(requiredFood - currentFoodTotal);
                Diag($"  → BUY needed={needed}");
                stats.Spent += BuyFood(party, settlement, needed);
            }
            // Spec шаг 3: diversity ≥ 2 — учитываем locked food тоже (она держит мораль-баф).
            // Продаём только из unlocked, но diversity gate смотрит на total entries.
            else if (currentFoodTotal > bufferFood && totalDistinctKinds >= 2 && partyFood.Count >= 1)
            {
                int excess = (int)(currentFoodTotal - bufferFood);
                Diag($"  → SELL excess={excess}");
                int gained = SellFood(party, settlement, partyFood, excess);
                stats.Earned += gained;
                stats.FoodEarned += gained;
            }
            else
            {
                string reason = currentFoodTotal <= bufferFood
                    ? "within buffer"
                    : totalDistinctKinds < 2
                        ? $"only {totalDistinctKinds} food kind(s), need ≥2"
                        : "all food kinds locked";
                Diag($"  → SKIP ({reason})");
            }
        }

        private int BuyFood(MobileParty party, Settlement settlement, int neededUnits)
        {
            int spent = 0;
            var offers = settlement.ItemRoster
                .Where(e => e.EquipmentElement.Item?.IsFood == true
                            && e.EquipmentElement.Item?.HorseComponent?.IsLiveStock != true
                            && e.Amount > 0)
                .Select(e => new
                {
                    Element = e,
                    Item = e.EquipmentElement.Item,
                    Price = GetBuyPrice(party, settlement, e.EquipmentElement)
                })
                .OrderBy(x => x.Price)
                .ToList();

            foreach (var offer in offers)
            {
                if (neededUnits <= 0) break;
                if (offer.Price > offer.Item.Value * TraderMath.BuyPriceCapFood)
                {
                    Diag($"  buy skip {offer.Item.StringId}: price={offer.Price} > cap={offer.Item.Value * TraderMath.BuyPriceCapFood:F0}");
                    continue;
                }

                int affordableByGold = Hero.MainHero.Gold / Math.Max(offer.Price, 1);
                int buyCount = Math.Min(Math.Min(offer.Element.Amount, neededUnits), affordableByGold);
                if (buyCount <= 0) continue;

                int totalCost = offer.Price * buyCount;
                settlement.ItemRoster.AddToCounts(offer.Element.EquipmentElement, -buyCount);
                party.ItemRoster.AddToCounts(offer.Element.EquipmentElement, buyCount);
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, settlement.OwnerClan?.Leader, totalCost, disableNotification: true);
                spent += totalCost;
                neededUnits -= buyCount;
                Diag($"  bought {buyCount} {offer.Item.StringId} @ {offer.Price} = {totalCost}g");
            }
            return spent;
        }

        private int SellFood(MobileParty party, Settlement settlement, List<ItemRosterElement> partyFood, int excessUnits)
        {
            int earned = 0;
            var sorted = partyFood
                .Where(e => e.EquipmentElement.Item?.HorseComponent?.IsLiveStock != true) // livestock уже обработан
                .Select(e => new
                {
                    Element = e,
                    Item = e.EquipmentElement.Item,
                    SellPrice = GetSellPrice(party, settlement, e.EquipmentElement)
                })
                .OrderByDescending(x => x.Element.Amount)
                .ToList();

            foreach (var offer in sorted)
            {
                if (excessUnits <= 0) break;
                float floor = GetSellFloor(offer.Item);
                if (offer.SellPrice < offer.Item.Value * floor)
                {
                    Diag($"  sell skip {offer.Item.StringId}: price={offer.SellPrice} < floor={offer.Item.Value * floor:F0}");
                    continue;
                }

                int maxSellable = Math.Max(0, offer.Element.Amount - 1); // оставить 1 для морал-бафа
                int sellCount = Math.Min(maxSellable, excessUnits);
                if (sellCount <= 0) continue;

                int totalEarned = offer.SellPrice * sellCount;
                party.ItemRoster.AddToCounts(offer.Element.EquipmentElement, -sellCount);
                settlement.ItemRoster.AddToCounts(offer.Element.EquipmentElement, sellCount);
                GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, totalEarned, disableNotification: true);
                earned += totalEarned;
                excessUnits -= sellCount;
                Diag($"  sold {sellCount} {offer.Item.StringId} @ {offer.SellPrice} = {totalEarned}g");
            }
            return earned;
        }

        // ----------------- Horse/Mule entry trim -----------------
        // Wave 1: продать lame (приоритет — они бесполезны)
        // Wave 2: cheapest non-lame до target (sell All использует expensive — но на entry
        //         сохраняем самых дорогих, продаём дешёвых для освобождения слотов)

        private void ProcessHerdTrim(MobileParty party, Settlement settlement, TradeStats stats,
                                       int currentCount, int target, bool isPackAnimalCategory, string label,
                                       HashSet<string> lockedSet, bool ignoreLocks)
        {
            if (currentCount <= target)
            {
                Diag($"[{label}] SKIP count={currentCount} <= target={target}");
                return;
            }
            int toSell = currentCount - target;

            var allCandidates = party.ItemRoster
                .Where(e =>
                {
                    var hc = e.EquipmentElement.Item?.HorseComponent;
                    if (hc == null) return false;
                    if (!(isPackAnimalCategory ? hc.IsPackAnimal : (hc.IsMount && !hc.IsPackAnimal))) return false;
                    if (!ignoreLocks && IsLocked(e.EquipmentElement, lockedSet))
                    {
                        Diag($"  [{label}] locked skip: {e.EquipmentElement.Item.StringId}");
                        return false;
                    }
                    return true;
                })
                .ToList();

            var lameCandidates = allCandidates.Where(e => IsLame(e.EquipmentElement)).ToList();
            int lameTotal = lameCandidates.Sum(e => e.Amount);

            Diag($"[{label}] count={currentCount} target={target} toSell={toSell} lame={lameTotal}");

            // Wave 1: lame first (приоритет — они бесполезны).
            foreach (var element in lameCandidates)
            {
                if (toSell <= 0) break;
                var item = element.EquipmentElement.Item;
                int sellPrice = GetSellPrice(party, settlement, element.EquipmentElement);
                float floor = GetSellFloor(item);
                if (sellPrice < item.Value * floor)
                {
                    Diag($"  wave1 skip lame {item.StringId}: price={sellPrice} < floor={item.Value * floor:F0}");
                    continue;
                }
                int sellCount = Math.Min(element.Amount, toSell);
                int totalEarned = sellPrice * sellCount;

                party.ItemRoster.AddToCounts(element.EquipmentElement, -sellCount);
                settlement.ItemRoster.AddToCounts(element.EquipmentElement, sellCount);
                GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, totalEarned, disableNotification: true);

                stats.Earned += totalEarned;
                if (isPackAnimalCategory) stats.MulesLameSold += sellCount;
                else stats.WarhorsesLameSold += sellCount;
                toSell -= sellCount;
                Diag($"  wave1 sold {sellCount}× {item.StringId} (lame) @ {sellPrice} = {totalEarned}g");
            }

            if (toSell <= 0) return;

            // Wave 2: cheapest non-lame до target. Дорогих сохраняем для боя.
            var nonLameByPrice = allCandidates
                .Where(e => !IsLame(e.EquipmentElement))
                .Select(e => new
                {
                    Element = e,
                    Item = e.EquipmentElement.Item,
                    Price = GetSellPrice(party, settlement, e.EquipmentElement)
                })
                .OrderBy(x => x.Price)
                .ToList();

            foreach (var entry in nonLameByPrice)
            {
                if (toSell <= 0) break;
                float floor = GetSellFloor(entry.Item);
                if (entry.Price < entry.Item.Value * floor)
                {
                    Diag($"  wave2 skip {entry.Item.StringId}: price={entry.Price} < floor={entry.Item.Value * floor:F0}");
                    continue;
                }
                int sellCount = Math.Min(entry.Element.Amount, toSell);
                int totalEarned = entry.Price * sellCount;

                party.ItemRoster.AddToCounts(entry.Element.EquipmentElement, -sellCount);
                settlement.ItemRoster.AddToCounts(entry.Element.EquipmentElement, sellCount);
                GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, totalEarned, disableNotification: true);

                stats.Earned += totalEarned;
                if (isPackAnimalCategory) stats.MulesExcessSold += sellCount;
                else stats.WarhorsesExcessSold += sellCount;
                toSell -= sellCount;
                Diag($"  wave2 sold {sellCount}× {entry.Item.StringId} @ {entry.Price} = {totalEarned}g");
            }
        }

        // ----------------- helpers -----------------

        private static bool IsLame(EquipmentElement element)
        {
            return element.ItemModifier != null && element.ItemModifier.StringId == LameModifierId;
        }

        private static HashSet<string> GetUserLocks()
        {
            try
            {
                var tracker = Campaign.Current?.GetCampaignBehavior<IViewDataTracker>();
                var locks = tracker?.GetInventoryLocks();
                if (locks == null) return new HashSet<string>();
                return new HashSet<string>(locks);
            }
            catch
            {
                return new HashSet<string>();
            }
        }

        private static bool IsLocked(EquipmentElement element, HashSet<string> lockedSet)
        {
            if (lockedSet == null || lockedSet.Count == 0) return false;
            try
            {
                var lockId = CampaignUIHelper.GetItemLockStringID(element);
                return lockedSet.Contains(lockId);
            }
            catch
            {
                return false;
            }
        }

        private static int GetBuyPrice(MobileParty party, Settlement settlement, EquipmentElement element)
        {
            if (settlement.Town != null)
                return settlement.Town.GetItemPrice(element, party, isSelling: false);
            return element.Item?.Value ?? 0;
        }

        private static int GetSellPrice(MobileParty party, Settlement settlement, EquipmentElement element)
        {
            if (settlement.Town != null)
                return settlement.Town.GetItemPrice(element, party, isSelling: true);
            return element.Item?.Value ?? 0;
        }

        private static float GetSellFloor(ItemObject item)
        {
            if (item == null) return TraderMath.SellFloorFood;
            var hc = item.HorseComponent;
            if (hc != null)
            {
                if (hc.IsLiveStock) return TraderMath.SellFloorLivestock;
                return TraderMath.SellFloorHorse;
            }
            return TraderMath.SellFloorFood;
        }

        private static void Diag(string msg)
        {
            if (!Diagnostics) return;
            FileLog.Log("[SmartFood] " + msg);
        }

        // ----------------- messages -----------------

        private const uint ColorGreen = 0xFF66FF66u;
        private const uint ColorRed = 0xFFFF6666u;

        private void EmitMessages(TradeStats stats, int daysOfFood, int finalFoodSum, int requiredFood,
                                    int finalWarhorses, int unmountedInf)
        {
            // 1. Food
            if (stats.Spent > 0)
            {
                if (finalFoodSum >= requiredFood)
                    Display($"Food: bought up to ~{daysOfFood}d (-{stats.Spent}g)", ColorGreen);
                else
                    Display($"Food: short — spent {stats.Spent}g but still only {finalFoodSum}/{requiredFood} (~{daysOfFood}d)", ColorRed);
            }
            else if (stats.FoodEarned > 0)
            {
                Display($"Food: sold excess (+{stats.FoodEarned}g, ~{daysOfFood}d left)", ColorGreen);
            }

            // 2. Livestock
            if (stats.LivestockSold + stats.LivestockButchered > 0)
            {
                var parts = new List<string>();
                if (stats.LivestockSold > 0) parts.Add($"sold {stats.LivestockSold}");
                if (stats.LivestockButchered > 0) parts.Add($"butchered {stats.LivestockButchered} (+{stats.MeatGained} meat, +{stats.HidesGained} hides)");
                Display("Livestock: " + string.Join(", ", parts), ColorGreen);
            }

            // 3. Horses + mules (Wave 1 lame + Wave 2 cheapest non-lame до target)
            int horsesSold = stats.WarhorsesLameSold + stats.WarhorsesExcessSold;
            int mulesSold = stats.MulesLameSold + stats.MulesExcessSold;
            if (horsesSold + mulesSold > 0)
            {
                var parts = new List<string>();
                if (horsesSold > 0) parts.Add($"sold {horsesSold} horses");
                if (mulesSold > 0) parts.Add($"sold {mulesSold} mules");
                Display("Herd: " + string.Join(", ", parts), ColorGreen);
            }

            if (finalWarhorses < unmountedInf)
            {
                int missing = unmountedInf - finalWarhorses;
                Display($"⚠ Need {missing} more warhorses for optimal speed", ColorRed);
            }
        }

        private static void Display(string text, uint color)
        {
            InformationManager.DisplayMessage(new InformationMessage(text, Color.FromUint(color)));
        }

        private class TradeStats
        {
            public int Spent;            // sum spent buying food
            public int Earned;            // total earned (food + livestock + herd)
            public int FoodEarned;        // earned from sold excess food
            public int LivestockSold;
            public int LivestockButchered;
            public int MeatGained;
            public int HidesGained;
            public int WarhorsesLameSold;
            public int WarhorsesExcessSold;
            public int MulesLameSold;
            public int MulesExcessSold;
        }
    }
}
