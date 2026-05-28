using System;

namespace TOR_QoLs.Behaviors
{
    /// <summary>
    /// Pure functions для торговых решений — без зависимости на TaleWorlds типы,
    /// чтобы их можно было покрыть unit-тестами без mock'ов game-state.
    ///
    /// Все формулы которые ранее были inline в SmartFoodTraderBehavior / SellAllPostfixPatch
    /// вынесены сюда. Сами обработчики просто дёргают эти функции и применяют результат
    /// к party.ItemRoster / TransferCommand'ам.
    /// </summary>
    public static class TraderMath
    {
        // Константы — должны совпадать с константами в обработчиках.
        // Spec (docs/spec/SmartTrader-flow.md, SPEC_SmartFoodTrader.md):
        //   target_warhorses  = U + 0.15 × N     (sweet spot + 15% buffer)
        //   target_mules      = floor(0.45 × N)  (45% buffer)
        //   reserve_for_loot  = ceil(0.40 × N)   (свободный herd-запас, мы не лезем)
        //   herd_budget_for_trim = N - reserve_for_loot  (всё что выше — лишнее)
        public const float WarhorseBufferPct = 0.15f;
        public const float MuleTargetPct = 0.45f;
        public const float ReserveForLootPct = 0.40f;
        public const float FoodMinDays = 10f;
        public const float FoodMaxDays = 15f;

        // Floors калиброваны под TOR — TOR's TORTradeItemPriceFactorModel давит цены
        // до 50% от value при низком Trade skill, spec'овские 0.7 отсекают всё подряд.
        public const float SellFloorFood = 0.3f;
        public const float SellFloorLivestock = 0.3f;
        public const float SellFloorHorse = 0.3f;
        public const float SellFloorWave2 = 0.3f;   // SellAll Wave 2 — тот же floor (под TOR)
        public const float BuyPriceCapFood = 1.5f;

        /// <summary>
        /// target_warhorses = unmounted_infantry + ceil(0.15 × totalMen).
        /// Sweet spot для боевых лошадей — каждому пешему достаётся, плюс 15% buffer.
        ///
        /// Integer math (а не float × ceil) чтобы избежать float-imprecision —
        /// 0.15f × 100 в IEEE 754 = 15.00000006 → ceil=16, давало бы лишнего warhorse.
        /// </summary>
        public static int TargetWarhorses(int totalMen, int unmountedInf)
        {
            if (totalMen < 0) totalMen = 0;
            if (unmountedInf < 0) unmountedInf = 0;
            // ceil(0.15 × n) = ceil(15n / 100) = (15n + 99) / 100 (int division)
            int buffer = (totalMen * 15 + 99) / 100;
            return unmountedInf + buffer;
        }

        /// <summary>
        /// desired_mules = floor(0.45 × totalMen).
        /// Spec (docs/spec/SmartTrader-flow.md): floor, без минимума.
        /// </summary>
        public static int DesiredMules(int totalMen)
        {
            if (totalMen < 0) totalMen = 0;
            return (totalMen * 45) / 100;  // floor(0.45 × n) integer math
        }

        /// <summary>
        /// reserve_for_loot = ceil(0.40 × totalMen). Свободный herd-запас, который
        /// мы оставляем под loot из боя — туда не trim'ом ни мулов ни warhorse-excess.
        /// </summary>
        public static int ReserveForLoot(int totalMen)
        {
            if (totalMen <= 0) return 0;
            return (totalMen * 4 + 9) / 10;  // ceil(0.4 × n)
        }

        /// <summary>
        /// mount_count_total = NumberOfMounts + NumberOfPackAnimals. Удобный helper
        /// для UI/диагностики.
        /// </summary>
        public static int MountCountTotal(int numberOfMounts, int numberOfPackAnimals)
        {
            return numberOfMounts + numberOfPackAnimals;
        }

        /// <summary>
        /// excess_warhorses = max(0, warhorses - unmounted_infantry).
        /// Количество боевых лошадей сверх посадочного минимума — они входят в herd-budget.
        /// </summary>
        public static int ExcessWarhorses(int warhorsesNow, int unmountedInf)
        {
            return Math.Max(0, warhorsesNow - unmountedInf);
        }

        /// <summary>
        /// mule_room = max(0, totalMen - reserve_for_loot - livestock - excess_warhorses).
        /// Sweet-spot: оставляем 40% N свободного herd под loot, всё остальное trim'ится.
        /// </summary>
        public static int MuleRoom(int totalMen, int livestockNow, int excessWarhorses)
        {
            int reserve = ReserveForLoot(totalMen);
            return Math.Max(0, totalMen - reserve - livestockNow - excessWarhorses);
        }

        /// <summary>
        /// target_mules = min(desired, room). Sweet-spot имеет приоритет:
        /// если herd-budget уже забит livestock'ом и excess warhorses — мулов до 0.
        /// </summary>
        public static int TargetMules(int desiredMules, int muleRoom)
        {
            return Math.Min(desiredMules, muleRoom);
        }

        /// <summary>
        /// required_food = ceil(dailyConsumption × FoodMinDays). Сколько food-единиц нужно
        /// чтобы продержаться 10 дней.
        /// </summary>
        public static int RequiredFood(float dailyConsumption)
        {
            if (dailyConsumption < 0f) dailyConsumption = 0f;
            return (int)Math.Ceiling(dailyConsumption * FoodMinDays);
        }

        /// <summary>
        /// buffer_food = dailyConsumption × FoodMaxDays. Свыше этого — продаём излишек.
        /// </summary>
        public static float BufferFood(float dailyConsumption)
        {
            if (dailyConsumption < 0f) dailyConsumption = 0f;
            return dailyConsumption * FoodMaxDays;
        }

        /// <summary>
        /// Гарантирует адекватный dailyConsumption даже если party.FoodChange ≈ 0
        /// (бывает у пустых отрядов сразу после боя): fallback на totalMen / 20.
        /// </summary>
        public static float NormalizeDailyConsumption(float foodChangeAbs, int totalMen)
        {
            if (foodChangeAbs < 0f) foodChangeAbs = 0f;
            if (foodChangeAbs >= 0.1f) return foodChangeAbs;
            return Math.Max(1f, totalMen / 20f);
        }

        /// <summary>
        /// effectiveButcherValue для ОДНОЙ livestock-единицы с учётом food-deficit бонуса:
        /// мясо которое покрывает дефицит оценивается по meatBuyPrice (т.к. иначе пришлось
        /// бы его покупать), остальное — по meatSellPrice. Hides всегда по sell.
        /// </summary>
        public static int EffectiveButcherValue(int meatCount, int hideCount,
            int meatBuyPrice, int meatSellPrice, int hideSellPrice, int remainingDeficit)
        {
            if (meatCount < 0) meatCount = 0;
            if (hideCount < 0) hideCount = 0;
            if (remainingDeficit < 0) remainingDeficit = 0;
            int covered = Math.Min(meatCount, remainingDeficit);
            return covered * meatBuyPrice
                 + (meatCount - covered) * meatSellPrice
                 + hideCount * hideSellPrice;
        }

        public enum LivestockDecision { Skip, Butcher, Sell }

        public enum FoodAction { Buy, Sell, Silent }
        public enum HorseAction { SellLame, WarnInsufficient, Silent }
        public enum MuleAction { SellLame, Silent }

        /// <summary>
        /// Spec (docs/spec/SmartTrader-flow.md шаг 3):
        ///   if currentFood &lt; required → BUY
        ///   elif currentFood &gt; buffer AND foodKinds ≥ 2 → SELL
        ///   else → SILENT
        /// </summary>
        public static FoodAction ComputeFoodAction(int currentFood, int requiredFood, float bufferFood, int distinctFoodKinds)
        {
            if (currentFood < requiredFood) return FoodAction.Buy;
            if (currentFood > bufferFood && distinctFoodKinds >= 2) return FoodAction.Sell;
            return FoodAction.Silent;
        }

        /// <summary>
        /// Spec (шаг 4):
        ///   if warhorses &gt; target → SellLame
        ///   elif warhorses &lt; unmountedInf → WarnInsufficient (red)
        ///   else → Silent
        ///
        /// ВАЖНО (spec строка 107): на входе продаются ТОЛЬКО lame. Здоровые не трогаются.
        /// Wave 2 (продажа non-lame до target) — только на Sell All.
        /// </summary>
        public static HorseAction ComputeHorseAction(int warhorses, int targetWarhorses, int unmountedInf)
        {
            if (warhorses > targetWarhorses) return HorseAction.SellLame;
            if (warhorses < unmountedInf) return HorseAction.WarnInsufficient;
            return HorseAction.Silent;
        }

        /// <summary>
        /// Spec (шаг 5):
        ///   if mules &gt; target → SellLame
        ///   else → Silent (не докупаем, не warn'им)
        /// </summary>
        public static MuleAction ComputeMuleAction(int mules, int targetMules)
        {
            if (mules > targetMules) return MuleAction.SellLame;
            return MuleAction.Silent;
        }

        /// <summary>
        /// Spec (шаг 3 sell branch): "оставляем минимум 1 единицу каждого типа".
        /// Возвращает сколько единиц можно отдать из конкретного entry, оставляя 1 для diversity.
        /// </summary>
        public static int FoodUnitsAvailableForSale(int entryAmount)
        {
            return Math.Max(0, entryAmount - 1);
        }

        /// <summary>
        /// Решение для одной livestock-единицы: BUTCHER если butcher выгоднее sell,
        /// иначе SELL если sell ≥ floor, иначе SKIP.
        /// </summary>
        public static LivestockDecision DecideUnit(int sellPrice, int effectiveButcherValue, bool sellOk)
        {
            if (effectiveButcherValue > sellPrice) return LivestockDecision.Butcher;
            if (sellOk) return LivestockDecision.Sell;
            return LivestockDecision.Skip;
        }

        /// <summary>
        /// Симулирует обработку amount единиц livestock одного типа, разбивая на
        /// butcherCount + sellCount. Останавливается при первом SKIP. Возвращает
        /// результат батча + обновлённый remainingDeficit.
        /// </summary>
        public static (int butcherCount, int sellCount, int newDeficit) SimulateLivestockBatch(
            int amount,
            int meatCount, int hideCount,
            int sellPrice, int meatBuyPrice, int meatSellPrice, int hideSellPrice,
            int initialDeficit, bool sellOk)
        {
            if (amount < 0) amount = 0;
            int butcherCount = 0;
            int sellCount = 0;
            int deficit = Math.Max(0, initialDeficit);

            for (int i = 0; i < amount; i++)
            {
                int ebv = EffectiveButcherValue(meatCount, hideCount, meatBuyPrice, meatSellPrice, hideSellPrice, deficit);
                var decision = DecideUnit(sellPrice, ebv, sellOk);
                if (decision == LivestockDecision.Butcher)
                {
                    butcherCount++;
                    deficit = Math.Max(0, deficit - meatCount);
                }
                else if (decision == LivestockDecision.Sell)
                {
                    sellCount++;
                }
                else
                {
                    break;
                }
            }

            return (butcherCount, sellCount, deficit);
        }
    }
}
