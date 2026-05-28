using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
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
        // Food
        private const float FoodMinDays = 10f;
        private const float FoodMaxDays = 15f;
        private const float BuyPriceCap = 1.5f;
        private const float SellPriceFloor = 0.7f;

        // Horses/Mules
        private const float WarhorseBufferPct = 0.15f;   // target_warhorses = unmounted × (1 + 0.15)
        private const float MuleTargetPct = 0.45f;       // target_mules = totalMen × 0.45

        // Lame status marker
        private const string LameModifierId = "lame_horse";

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

            // 1. Daily food consumption (с учётом всех источников food party.FoodChange).
            float dailyConsumption = Math.Abs(party.FoodChange);
            if (dailyConsumption < 0.1f) dailyConsumption = Math.Max(1f, party.MemberRoster.TotalManCount / 20f);

            int totalMen = party.MemberRoster.TotalManCount;
            int unmountedInf = party.Party.NumberOfMenWithoutHorse;
            int warhorses = party.ItemRoster.NumberOfMounts;
            int mules = party.ItemRoster.NumberOfPackAnimals;

            int targetWarhorses = unmountedInf + (int)Math.Ceiling(WarhorseBufferPct * totalMen);
            int targetMules = (int)(MuleTargetPct * totalMen);

            // 2. Livestock — sell vs butcher per-unit
            ProcessLivestock(party, settlement, stats, dailyConsumption);

            // 3. Food — buy if short, sell if excess (после livestock-butcher мяса добавилось)
            ProcessFood(party, settlement, stats, dailyConsumption);

            // 4. Warhorses — продаём только lame/sick если > target
            ProcessLameOnly(party, settlement, stats, warhorses, targetWarhorses,
                isPackAnimalCategory: false, label: "warhorses");

            // 5. Mules — то же
            ProcessLameOnly(party, settlement, stats, mules, targetMules,
                isPackAnimalCategory: true, label: "mules");

            // 6. Финальное сообщение + красное предупреждение если warhorses < unmounted_infantry
            int finalWarhorses = party.ItemRoster.NumberOfMounts;
            int finalFoodSum = party.ItemRoster
                .Where(e => e.EquipmentElement.Item?.IsFood == true)
                .Sum(e => e.Amount);
            int finalDays = (int)(finalFoodSum / Math.Max(dailyConsumption, 0.1f));

            EmitMessage(stats, finalDays, finalWarhorses, unmountedInf);
        }

        // ----------------- Livestock pass -----------------

        private void ProcessLivestock(MobileParty party, Settlement settlement, TradeStats stats, float dailyConsumption)
        {
            // Снимаем снимок livestock-элементов (чтобы итерация не падала при изменении roster)
            var livestockSnapshot = new List<ItemRosterElement>();
            foreach (var element in party.ItemRoster)
            {
                if (element.EquipmentElement.Item?.HorseComponent?.IsLiveStock == true)
                    livestockSnapshot.Add(element);
            }
            if (livestockSnapshot.Count == 0) return;

            foreach (var element in livestockSnapshot)
            {
                var item = element.EquipmentElement.Item;
                var horseComp = item.HorseComponent;
                int amount = party.ItemRoster.GetItemNumber(item);
                if (amount <= 0) continue;

                int sellPrice = GetSellPrice(party, settlement, element.EquipmentElement);

                int meatCount = horseComp.MeatCount;
                int hideCount = horseComp.HideCount;

                int meatPrice = DefaultItems.Meat != null
                    ? GetSellPrice(party, settlement, new EquipmentElement(DefaultItems.Meat))
                    : 0;
                int hidePrice = DefaultItems.Hides != null
                    ? GetSellPrice(party, settlement, new EquipmentElement(DefaultItems.Hides))
                    : 0;

                int butcherValue = meatCount * meatPrice + hideCount * hidePrice;

                // Sell-цена-фильтр: и livestock, и butcher products должны быть в норме
                bool sellOk = sellPrice >= item.Value * SellPriceFloor;

                for (int unitIdx = 0; unitIdx < amount; unitIdx++)
                {
                    if (butcherValue > sellPrice)
                    {
                        // Butcher: livestock-1, +meat, +hides
                        party.ItemRoster.AddToCounts(element.EquipmentElement, -1);
                        if (DefaultItems.Meat != null && meatCount > 0)
                            party.ItemRoster.AddToCounts(DefaultItems.Meat, meatCount);
                        if (DefaultItems.Hides != null && hideCount > 0)
                            party.ItemRoster.AddToCounts(DefaultItems.Hides, hideCount);
                        stats.LivestockButchered++;
                        stats.MeatGained += meatCount;
                        stats.HidesGained += hideCount;
                    }
                    else if (sellOk)
                    {
                        // Sell as livestock
                        party.ItemRoster.AddToCounts(element.EquipmentElement, -1);
                        settlement.ItemRoster.AddToCounts(element.EquipmentElement, 1);
                        GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, sellPrice);
                        stats.Earned += sellPrice;
                        stats.LivestockSold++;
                    }
                    else
                    {
                        // Цена не устраивает — оставляем
                        break;
                    }
                }
            }
        }

        // ----------------- Food pass -----------------

        private void ProcessFood(MobileParty party, Settlement settlement, TradeStats stats, float dailyConsumption)
        {
            float requiredFood = dailyConsumption * FoodMinDays;
            float bufferFood = dailyConsumption * FoodMaxDays;

            var partyFood = new List<ItemRosterElement>();
            int currentFoodTotal = 0;
            foreach (var element in party.ItemRoster)
            {
                if (element.EquipmentElement.Item?.IsFood == true)
                {
                    partyFood.Add(element);
                    currentFoodTotal += element.Amount;
                }
            }

            if (currentFoodTotal < requiredFood)
            {
                int needed = (int)Math.Ceiling(requiredFood - currentFoodTotal);
                stats.Spent += BuyFood(party, settlement, needed);
            }
            else if (currentFoodTotal > bufferFood && partyFood.Count >= 2)
            {
                int excess = (int)(currentFoodTotal - bufferFood);
                stats.Earned += SellFood(party, settlement, partyFood, excess);
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
                if (offer.Price > offer.Item.Value * BuyPriceCap) continue;

                int affordableByGold = Hero.MainHero.Gold / Math.Max(offer.Price, 1);
                int buyCount = Math.Min(Math.Min(offer.Element.Amount, neededUnits), affordableByGold);
                if (buyCount <= 0) continue;

                int totalCost = offer.Price * buyCount;
                settlement.ItemRoster.AddToCounts(offer.Element.EquipmentElement, -buyCount);
                party.ItemRoster.AddToCounts(offer.Element.EquipmentElement, buyCount);
                GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, settlement.OwnerClan?.Leader, totalCost);
                spent += totalCost;
                neededUnits -= buyCount;
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
                if (offer.SellPrice < offer.Item.Value * SellPriceFloor) continue;

                int maxSellable = Math.Max(0, offer.Element.Amount - 1); // оставить 1 для морал-бафа
                int sellCount = Math.Min(maxSellable, excessUnits);
                if (sellCount <= 0) continue;

                int totalEarned = offer.SellPrice * sellCount;
                party.ItemRoster.AddToCounts(offer.Element.EquipmentElement, -sellCount);
                settlement.ItemRoster.AddToCounts(offer.Element.EquipmentElement, sellCount);
                GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, totalEarned);
                earned += totalEarned;
                excessUnits -= sellCount;
            }
            return earned;
        }

        // ----------------- Horse/Mule Wave 1 pass -----------------

        private void ProcessLameOnly(MobileParty party, Settlement settlement, TradeStats stats,
                                       int currentCount, int target, bool isPackAnimalCategory, string label)
        {
            if (currentCount <= target) return;
            int toSell = currentCount - target;

            var candidates = party.ItemRoster
                .Where(e =>
                {
                    var hc = e.EquipmentElement.Item?.HorseComponent;
                    if (hc == null) return false;
                    if (isPackAnimalCategory ? !hc.IsPackAnimal : !hc.IsMount) return false;
                    return IsLame(e.EquipmentElement);
                })
                .ToList();

            foreach (var element in candidates)
            {
                if (toSell <= 0) break;

                int sellPrice = GetSellPrice(party, settlement, element.EquipmentElement);
                if (sellPrice < element.EquipmentElement.Item.Value * SellPriceFloor) continue;

                int sellCount = Math.Min(element.Amount, toSell);
                int totalEarned = sellPrice * sellCount;

                party.ItemRoster.AddToCounts(element.EquipmentElement, -sellCount);
                settlement.ItemRoster.AddToCounts(element.EquipmentElement, sellCount);
                GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, totalEarned);

                stats.Earned += totalEarned;
                if (isPackAnimalCategory) stats.MulesLameSold += sellCount;
                else stats.WarhorsesLameSold += sellCount;

                toSell -= sellCount;
            }
        }

        // ----------------- helpers -----------------

        private static bool IsLame(EquipmentElement element)
        {
            return element.ItemModifier != null && element.ItemModifier.StringId == LameModifierId;
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

        // ----------------- message -----------------

        private void EmitMessage(TradeStats stats, int daysOfFood, int finalWarhorses, int unmountedInf)
        {
            // Основное summary
            int net = stats.Earned - stats.Spent;
            if (stats.AnyActivity)
            {
                var parts = new List<string>();
                if (stats.Spent > 0 || stats.Earned > 0)
                    parts.Add($"spent {stats.Spent}g, earned {stats.Earned}g, net {net}g");
                if (stats.LivestockButchered > 0)
                    parts.Add($"butchered {stats.LivestockButchered} ({stats.MeatGained} meat / {stats.HidesGained} hides)");
                if (stats.LivestockSold > 0)
                    parts.Add($"sold {stats.LivestockSold} livestock");
                if (stats.WarhorsesLameSold > 0)
                    parts.Add($"sold {stats.WarhorsesLameSold} lame horses");
                if (stats.MulesLameSold > 0)
                    parts.Add($"sold {stats.MulesLameSold} lame mules");
                parts.Add($"~{daysOfFood} days food");

                uint color = net >= 0 ? 0xFF66FF66u : 0xFFFF6666u;
                InformationManager.DisplayMessage(new InformationMessage(
                    "Trader: " + string.Join(" | ", parts), Color.FromUint(color)));
            }

            // Красное предупреждение если боевых лошадей недостаточно
            if (finalWarhorses < unmountedInf)
            {
                int missing = unmountedInf - finalWarhorses;
                InformationManager.DisplayMessage(new InformationMessage(
                    $"⚠ Need {missing} more warhorses for optimal speed",
                    Color.FromUint(0xFFFF6666u)));
            }
        }

        private class TradeStats
        {
            public int Spent;
            public int Earned;
            public int LivestockSold;
            public int LivestockButchered;
            public int MeatGained;
            public int HidesGained;
            public int WarhorsesLameSold;
            public int MulesLameSold;

            public bool AnyActivity => Spent != 0 || Earned != 0
                                       || LivestockSold > 0 || LivestockButchered > 0
                                       || WarhorsesLameSold > 0 || MulesLameSold > 0;
        }
    }
}
