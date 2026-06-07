using TOR_QoLs.Behaviors;
using Xunit;

namespace TOR_QoLs.Tests
{
    public class TargetWarhorsesTests
    {
        [Theory]
        [InlineData(0, 0, 0)]
        [InlineData(2, 2, 3)]    // 2 пеших + ceil(0.3) = 2 + 1 = 3
        [InlineData(10, 5, 7)]   // 5 пеших + ceil(1.5) = 5 + 2 = 7
        [InlineData(100, 50, 65)] // 50 + ceil(15.0) = 50 + 15 = 65
        [InlineData(20, 0, 3)]   // никто не пеший, всё равно buffer ceil(3.0) = 3
        public void Formula(int totalMen, int unmountedInf, int expected)
        {
            Assert.Equal(expected, TraderMath.TargetWarhorses(totalMen, unmountedInf));
        }

        [Fact]
        public void NegativeInputs_Clamped()
        {
            Assert.Equal(0, TraderMath.TargetWarhorses(-5, -3));
        }
    }

    public class DesiredMulesTests
    {
        // Spec (SmartTrader-flow.md строка 41): floor(0.45 × totalMen). Без минимума.
        [Theory]
        [InlineData(0, 0)]
        [InlineData(2, 0)]      // floor(0.9) = 0
        [InlineData(3, 1)]      // floor(1.35) = 1
        [InlineData(10, 4)]     // floor(4.5) = 4
        [InlineData(11, 4)]     // floor(4.95) = 4
        [InlineData(20, 9)]     // floor(9.0) = 9
        [InlineData(21, 9)]     // floor(9.45) = 9
        [InlineData(100, 45)]
        public void Formula(int totalMen, int expected)
        {
            Assert.Equal(expected, TraderMath.DesiredMules(totalMen));
        }
    }

    public class ReserveForLootTests
    {
        // Spec: reserve_for_loot = ceil(0.40 × totalMen). Свободный herd-запас для loot.
        [Theory]
        [InlineData(0, 0)]
        [InlineData(2, 1)]      // ceil(0.8) = 1
        [InlineData(5, 2)]      // ceil(2.0) = 2
        [InlineData(10, 4)]     // ceil(4.0) = 4
        [InlineData(11, 5)]     // ceil(4.4) = 5
        [InlineData(100, 40)]
        public void Formula(int totalMen, int expected)
        {
            Assert.Equal(expected, TraderMath.ReserveForLoot(totalMen));
        }
    }

    public class MountCountTotalTests
    {
        [Fact]
        public void Sum_Of_Both()
        {
            Assert.Equal(7, TraderMath.MountCountTotal(numberOfMounts: 5, numberOfPackAnimals: 2));
            Assert.Equal(0, TraderMath.MountCountTotal(0, 0));
        }
    }

    public class ExcessWarhorsesTests
    {
        [Theory]
        [InlineData(5, 2, 3)]
        [InlineData(2, 2, 0)]
        [InlineData(2, 5, 0)]  // меньше unmounted → no excess
        [InlineData(0, 0, 0)]
        public void Formula(int warhorses, int unmountedInf, int expected)
        {
            Assert.Equal(expected, TraderMath.ExcessWarhorses(warhorses, unmountedInf));
        }
    }

    public class MuleRoomTests
    {
        // Формула: max(0, totalMen - reserve_for_loot - livestock - excessWh).
        // reserve_for_loot = ceil(0.4 × N).
        [Theory]
        [InlineData(10, 0, 0, 6)]    // 10 - 4 - 0 - 0 = 6 (reserve 4)
        [InlineData(2, 27, 3, 0)]    // 2 - 1 - 27 - 3 = -29 → 0
        [InlineData(10, 5, 2, 0)]    // 10 - 4 - 5 - 2 = -1 → 0
        [InlineData(20, 0, 3, 9)]    // 20 - 8 - 0 - 3 = 9
        [InlineData(100, 0, 15, 45)] // 100 - 40 - 0 - 15 = 45
        public void Formula(int totalMen, int livestock, int excessWh, int expected)
        {
            Assert.Equal(expected, TraderMath.MuleRoom(totalMen, livestock, excessWh));
        }
    }

    public class TargetMulesTests
    {
        [Theory]
        [InlineData(2, 0, 0)]    // desired=2, room=0 → 0 (sweet-spot wins)
        [InlineData(2, 5, 2)]    // desired=2, room=5 → 2 (desired wins)
        [InlineData(9, 3, 3)]    // desired=9 (big party), room=3 → 3 (sweet-spot wins)
        [InlineData(0, 0, 0)]
        [InlineData(0, 5, 0)]    // desired=0 → нет мулов
        public void Formula(int desiredMules, int muleRoom, int expected)
        {
            Assert.Equal(expected, TraderMath.TargetMules(desiredMules, muleRoom));
        }
    }

    public class RequiredFoodTests
    {
        [Theory]
        [InlineData(0.10f, 1)]    // 10 days × 0.1/day = 1
        [InlineData(1.0f, 10)]    // 10
        [InlineData(2.5f, 25)]    // 25
        [InlineData(0.0f, 0)]
        public void Formula(float daily, int expected)
        {
            Assert.Equal(expected, TraderMath.RequiredFood(daily));
        }

        [Fact]
        public void Negative_TreatedAsZero()
        {
            Assert.Equal(0, TraderMath.RequiredFood(-5f));
        }
    }

    public class BufferFoodTests
    {
        [Theory]
        [InlineData(0.10f, 1.5f)]   // 15 days × 0.1
        [InlineData(2f, 30f)]
        public void Formula(float daily, float expected)
        {
            Assert.Equal(expected, TraderMath.BufferFood(daily), 3);
        }
    }

    public class NormalizeDailyConsumptionTests
    {
        [Fact]
        public void Above_Threshold_Returned_AsIs()
        {
            Assert.Equal(0.5f, TraderMath.NormalizeDailyConsumption(0.5f, 10));
        }

        [Fact]
        public void Below_Threshold_Fallback_TotalMenDiv20()
        {
            // 0.05 < 0.1 → fallback. totalMen=40 → max(1, 40/20) = 2
            Assert.Equal(2f, TraderMath.NormalizeDailyConsumption(0.05f, 40));
        }

        [Fact]
        public void Below_Threshold_Minimum_One()
        {
            // 0.05 < 0.1, totalMen=2 → max(1, 0) = 1
            Assert.Equal(1f, TraderMath.NormalizeDailyConsumption(0.05f, 2));
        }
    }

    public class EffectiveButcherValueTests
    {
        [Fact]
        public void NoDeficit_AllMeatBySellPrice()
        {
            // deficit=0 → covered=0, всё мясо по sell-price
            int v = TraderMath.EffectiveButcherValue(meatCount: 4, hideCount: 2,
                meatBuyPrice: 50, meatSellPrice: 30, hideSellPrice: 20,
                remainingDeficit: 0);
            // 4×30 + 2×20 = 120 + 40 = 160
            Assert.Equal(160, v);
        }

        [Fact]
        public void DeficitCoversAllMeat_AllByBuyPrice()
        {
            // deficit >= meatCount → весь meat по buy-price
            int v = TraderMath.EffectiveButcherValue(meatCount: 4, hideCount: 2,
                meatBuyPrice: 50, meatSellPrice: 30, hideSellPrice: 20,
                remainingDeficit: 10);
            // 4×50 + 0×30 + 2×20 = 200 + 40 = 240
            Assert.Equal(240, v);
        }

        [Fact]
        public void PartialDeficit_MixedPricing()
        {
            // deficit=2, meat=4 → covered=2 по buy, остальные 2 по sell
            int v = TraderMath.EffectiveButcherValue(meatCount: 4, hideCount: 2,
                meatBuyPrice: 50, meatSellPrice: 30, hideSellPrice: 20,
                remainingDeficit: 2);
            // 2×50 + 2×30 + 2×20 = 100 + 60 + 40 = 200
            Assert.Equal(200, v);
        }

        [Fact]
        public void NegativeInputs_TreatedAsZero()
        {
            int v = TraderMath.EffectiveButcherValue(meatCount: -1, hideCount: -1,
                meatBuyPrice: 50, meatSellPrice: 30, hideSellPrice: 20,
                remainingDeficit: -5);
            Assert.Equal(0, v);
        }
    }

    public class DecideUnitTests
    {
        [Fact]
        public void Butcher_When_EffectiveGreater_ThanSell()
        {
            Assert.Equal(TraderMath.LivestockDecision.Butcher,
                TraderMath.DecideUnit(sellPrice: 40, effectiveButcherValue: 50, sellOk: true));
        }

        [Fact]
        public void Sell_When_EffectiveLessOrEqual_AndSellOk()
        {
            Assert.Equal(TraderMath.LivestockDecision.Sell,
                TraderMath.DecideUnit(sellPrice: 40, effectiveButcherValue: 30, sellOk: true));
            Assert.Equal(TraderMath.LivestockDecision.Sell,
                TraderMath.DecideUnit(sellPrice: 40, effectiveButcherValue: 40, sellOk: true));
        }

        [Fact]
        public void Skip_When_Sell_NotOk_And_ButcherWorse()
        {
            Assert.Equal(TraderMath.LivestockDecision.Skip,
                TraderMath.DecideUnit(sellPrice: 40, effectiveButcherValue: 30, sellOk: false));
        }
    }

    public class ComputeAffordableTests
    {
        [Fact]
        public void AllAffordable_FullAmount_GoldDeducted()
        {
            long gold = 1000;
            var result = TraderMath.ComputeAffordable(wantAmount: 5, unitPrice: 100, ref gold);
            Assert.Equal(5, result);
            Assert.Equal(500, gold);
        }

        [Fact]
        public void NotEnoughGold_PartialAmount()
        {
            long gold = 250;
            var result = TraderMath.ComputeAffordable(wantAmount: 10, unitPrice: 100, ref gold);
            Assert.Equal(2, result);  // 250/100 = 2
            Assert.Equal(50, gold);   // 250 - 200
        }

        [Fact]
        public void ZeroGold_ReturnsZero()
        {
            long gold = 0;
            var result = TraderMath.ComputeAffordable(wantAmount: 10, unitPrice: 50, ref gold);
            Assert.Equal(0, result);
            Assert.Equal(0, gold);
        }

        [Fact]
        public void FreeItem_UnitPriceZero_FullAmount_NoGoldChange()
        {
            long gold = 100;
            var result = TraderMath.ComputeAffordable(wantAmount: 20, unitPrice: 0, ref gold);
            Assert.Equal(20, result);
            Assert.Equal(100, gold);  // gold не списан для бесплатной передачи
        }

        [Fact]
        public void NegativeWantAmount_ReturnsZero()
        {
            long gold = 1000;
            var result = TraderMath.ComputeAffordable(wantAmount: -5, unitPrice: 10, ref gold);
            Assert.Equal(0, result);
            Assert.Equal(1000, gold);
        }

        [Fact]
        public void UnitPriceTooHigh_ReturnsZero()
        {
            long gold = 99;
            var result = TraderMath.ComputeAffordable(wantAmount: 5, unitPrice: 100, ref gold);
            Assert.Equal(0, result);
            Assert.Equal(99, gold);  // gold не списан
        }

        [Fact]
        public void TorScenario_SettlementHas2000_Sell10AtPrice300()
        {
            // Реальный сценарий: settlement gold=2000, item 300g. Продастся 6 (1800g), останется 200g.
            long gold = 2000;
            var result = TraderMath.ComputeAffordable(wantAmount: 10, unitPrice: 300, ref gold);
            Assert.Equal(6, result);
            Assert.Equal(200, gold);
        }
    }

    public class SimulateLivestockBatchTests
    {
        [Fact]
        public void NoDeficit_AllSell_WhenButcherWorse()
        {
            // 5 sheep, no deficit, butcher value 26 < sellPrice 42 → all SELL
            var (b, s, d) = TraderMath.SimulateLivestockBatch(
                amount: 5,
                meatCount: 1, hideCount: 0,
                sellPrice: 42, meatBuyPrice: 50, meatSellPrice: 26, hideSellPrice: 58,
                initialDeficit: 0, sellOk: true);
            Assert.Equal(0, b);
            Assert.Equal(5, s);
            Assert.Equal(0, d);
        }

        [Fact]
        public void Deficit_ForcesFirstUnits_ToButcher_ThenSell()
        {
            // 5 cows, meatCount=4 каждая, deficit=10.
            // unit#0: covered=4 → ebv=4×50+0×30+0=200, sell=180 → BUTCHER, deficit=6
            // unit#1: covered=4 → ebv=200, sell=180 → BUTCHER, deficit=2
            // unit#2: covered=2 → ebv=2×50+2×30+0=160, sell=180 → SELL
            // unit#3: covered=0 → ebv=0+4×30+0=120, sell=180 → SELL
            // unit#4: covered=0 → ebv=120, sell=180 → SELL
            var (b, s, d) = TraderMath.SimulateLivestockBatch(
                amount: 5,
                meatCount: 4, hideCount: 0,
                sellPrice: 180, meatBuyPrice: 50, meatSellPrice: 30, hideSellPrice: 0,
                initialDeficit: 10, sellOk: true);
            Assert.Equal(2, b);
            Assert.Equal(3, s);
            Assert.Equal(2, d);
        }

        [Fact]
        public void SellNotOk_NoDeficit_AllSkip()
        {
            // sellOk=false, butcher не выгоднее → SKIP первого, break
            var (b, s, d) = TraderMath.SimulateLivestockBatch(
                amount: 10,
                meatCount: 1, hideCount: 0,
                sellPrice: 50, meatBuyPrice: 50, meatSellPrice: 25, hideSellPrice: 0,
                initialDeficit: 0, sellOk: false);
            Assert.Equal(0, b);
            Assert.Equal(0, s);
            Assert.Equal(0, d);
        }

        [Fact]
        public void SellNotOk_ButButcherWins_StillButchers()
        {
            // sellOk=false но butcher value > sellPrice → BUTCHER (sellOk не блокирует butcher)
            var (b, s, _) = TraderMath.SimulateLivestockBatch(
                amount: 3,
                meatCount: 5, hideCount: 0,
                sellPrice: 100, meatBuyPrice: 50, meatSellPrice: 30, hideSellPrice: 0,
                initialDeficit: 100, sellOk: false);
            // ebv = 5×50 = 250 > 100 → BUTCHER
            Assert.Equal(3, b);
            Assert.Equal(0, s);
        }

        [Fact]
        public void ZeroAmount_NoOps()
        {
            var (b, s, d) = TraderMath.SimulateLivestockBatch(
                amount: 0, meatCount: 4, hideCount: 2,
                sellPrice: 100, meatBuyPrice: 50, meatSellPrice: 30, hideSellPrice: 20,
                initialDeficit: 10, sellOk: true);
            Assert.Equal(0, b);
            Assert.Equal(0, s);
            Assert.Equal(10, d);
        }
    }

    /// <summary>
    /// Spec constants: что описано в docs/spec/SmartTrader-flow.md и SPEC_SmartFoodTrader.md.
    /// Если эти числа поменялись в коде без обновления spec — test поймает.
    /// </summary>
    public class SpecConstantsTests
    {
        [Fact]
        public void FoodMinDays_Is_10()                      => Assert.Equal(10f, TraderMath.FoodMinDays);
        [Fact]
        public void FoodMaxDays_Is_15()                      => Assert.Equal(15f, TraderMath.FoodMaxDays);
        [Fact]
        public void WarhorseBufferPct_Is_15Percent()         => Assert.Equal(0.15f, TraderMath.WarhorseBufferPct);
        [Fact]
        public void MuleTargetPct_Is_45Percent()             => Assert.Equal(0.45f, TraderMath.MuleTargetPct);
        [Fact]
        public void BuyPriceCapFood_Is_150Percent()          => Assert.Equal(1.5f, TraderMath.BuyPriceCapFood);

        // Floors калиброваны под TOR (а не spec 0.7) — TOR's TORTradeItemPriceFactorModel
        // давит цены equipment до 50%. Все floors 0.3.
        [Fact]
        public void SellFloorFood_TOR_Calibrated()         => Assert.Equal(0.3f, TraderMath.SellFloorFood);
        [Fact]
        public void SellFloorLivestock_TOR_Calibrated()    => Assert.Equal(0.3f, TraderMath.SellFloorLivestock);
        [Fact]
        public void SellFloorHorse_TOR_Calibrated()        => Assert.Equal(0.3f, TraderMath.SellFloorHorse);
        [Fact]
        public void SellFloorWave2_TOR_Calibrated()        => Assert.Equal(0.3f, TraderMath.SellFloorWave2);

        // reserve_for_loot Pct constant.
        [Fact]
        public void ReserveForLootPct_Is_40Percent()       => Assert.Equal(0.40f, TraderMath.ReserveForLootPct);
    }

    /// <summary>
    /// Spec шаг 3 (SmartTrader-flow.md): food action decision.
    /// </summary>
    public class FoodActionTests
    {
        [Fact]
        public void Buy_When_Below_Required()
        {
            Assert.Equal(TraderMath.FoodAction.Buy,
                TraderMath.ComputeFoodAction(currentFood: 5, requiredFood: 10, bufferFood: 15, distinctFoodKinds: 3));
        }

        [Fact]
        public void Sell_When_Above_Buffer_And_Two_Or_More_Kinds()
        {
            Assert.Equal(TraderMath.FoodAction.Sell,
                TraderMath.ComputeFoodAction(currentFood: 20, requiredFood: 10, bufferFood: 15, distinctFoodKinds: 2));
        }

        [Fact]
        public void Silent_When_Above_Buffer_But_Only_One_Kind()
        {
            // Spec шаг 3: SELL требует foodTypes ≥ 2 для морал-diversity.
            Assert.Equal(TraderMath.FoodAction.Silent,
                TraderMath.ComputeFoodAction(currentFood: 20, requiredFood: 10, bufferFood: 15, distinctFoodKinds: 1));
        }

        [Fact]
        public void Silent_Between_Required_And_Buffer()
        {
            Assert.Equal(TraderMath.FoodAction.Silent,
                TraderMath.ComputeFoodAction(currentFood: 12, requiredFood: 10, bufferFood: 15, distinctFoodKinds: 3));
        }

        [Fact]
        public void Silent_Exactly_At_Buffer()
        {
            // > buffer, не >= — точно на границе silent
            Assert.Equal(TraderMath.FoodAction.Silent,
                TraderMath.ComputeFoodAction(currentFood: 15, requiredFood: 10, bufferFood: 15, distinctFoodKinds: 3));
        }

        [Fact]
        public void Buy_Priority_Over_Sell_At_Edge()
        {
            // currentFood < required имеет приоритет, даже если > buffer (нелогично, но spec
            // явно "if .. elif", и required < buffer всегда).
            Assert.Equal(TraderMath.FoodAction.Buy,
                TraderMath.ComputeFoodAction(currentFood: 9, requiredFood: 10, bufferFood: 15, distinctFoodKinds: 3));
        }
    }

    /// <summary>
    /// Spec шаг 4 (SmartTrader-flow.md): horse action на entry.
    /// "ВАЖНО (строка 107): на входе продаются ТОЛЬКО lame. Здоровые не трогаются."
    /// </summary>
    public class HorseActionTests
    {
        [Fact]
        public void SellLame_When_Above_Target()
        {
            Assert.Equal(TraderMath.HorseAction.SellLame,
                TraderMath.ComputeHorseAction(warhorses: 5, targetWarhorses: 3, unmountedInf: 2));
        }

        [Fact]
        public void WarnInsufficient_When_Below_UnmountedInf()
        {
            Assert.Equal(TraderMath.HorseAction.WarnInsufficient,
                TraderMath.ComputeHorseAction(warhorses: 1, targetWarhorses: 5, unmountedInf: 3));
        }

        [Fact]
        public void Silent_When_Between_UnmountedInf_And_Target()
        {
            Assert.Equal(TraderMath.HorseAction.Silent,
                TraderMath.ComputeHorseAction(warhorses: 3, targetWarhorses: 5, unmountedInf: 2));
        }

        [Fact]
        public void Silent_Exactly_At_Target()
        {
            Assert.Equal(TraderMath.HorseAction.Silent,
                TraderMath.ComputeHorseAction(warhorses: 3, targetWarhorses: 3, unmountedInf: 2));
        }

        [Fact]
        public void Silent_Exactly_At_UnmountedInf()
        {
            // warhorses == unmounted — нет warn'а (spec: warhorses < unmounted)
            Assert.Equal(TraderMath.HorseAction.Silent,
                TraderMath.ComputeHorseAction(warhorses: 3, targetWarhorses: 5, unmountedInf: 3));
        }
    }

    /// <summary>
    /// Spec шаг 5 (SmartTrader-flow.md): mule action на entry.
    /// </summary>
    public class MuleActionTests
    {
        [Fact]
        public void SellLame_When_Above_Target()
        {
            Assert.Equal(TraderMath.MuleAction.SellLame,
                TraderMath.ComputeMuleAction(mules: 9, targetMules: 0));
        }

        [Fact]
        public void Silent_When_At_Or_Below_Target()
        {
            // spec: mules < target → silent (не докупаем, не warn'им)
            Assert.Equal(TraderMath.MuleAction.Silent,
                TraderMath.ComputeMuleAction(mules: 0, targetMules: 2));
            Assert.Equal(TraderMath.MuleAction.Silent,
                TraderMath.ComputeMuleAction(mules: 2, targetMules: 2));
        }
    }

    /// <summary>
    /// Spec шаг 3 sell branch: "оставляем минимум 1 единицу каждого типа".
    /// </summary>
    public class FoodDiversityTests
    {
        [Theory]
        [InlineData(0, 0)]       // нет ни одной — нечего продать
        [InlineData(1, 0)]       // 1 — оставляем, ничего на продажу
        [InlineData(5, 4)]       // 5 → продаём 4, оставляем 1
        [InlineData(100, 99)]
        public void FoodUnitsAvailableForSale_KeepsOne(int amount, int expectedAvailable)
        {
            Assert.Equal(expectedAvailable, TraderMath.FoodUnitsAvailableForSale(amount));
        }
    }

    /// <summary>
    /// Economic decision при deficit: код взвешивает sell+buy vs butcher (как user
    /// сформулировал: "взвесить продать/забить/пополнить что выгоднее"). Если sellPrice
    /// после покрытия дефицита покупкой остаётся выгоднее — SELL.
    /// </summary>
    public class LivestockEconomicDecisionTests
    {
        [Fact]
        public void Sheep_At_Deficit_Sell_If_NetOfBuyMeat_Still_Profitable()
        {
            // sheep example: meat=1, hide=0, sellPrice=42, meatSellPrice=26, meatBuyPrice=31
            // EBV при deficit=10 = 1×31 = 31. SellPrice=42 > 31 → SELL.
            // Net: 42 (sell) - 31 (купить 1 meat) = 11 gold профит ВНЕ зависимости от butcher.
            int ebv = TraderMath.EffectiveButcherValue(meatCount: 1, hideCount: 0,
                meatBuyPrice: 31, meatSellPrice: 26, hideSellPrice: 0, remainingDeficit: 10);
            Assert.Equal(31, ebv);
            Assert.Equal(TraderMath.LivestockDecision.Sell,
                TraderMath.DecideUnit(sellPrice: 42, effectiveButcherValue: 31, sellOk: true));
        }

        [Fact]
        public void Cow_At_Deficit_Butcher_If_MeatBuy_Bonus_Wins()
        {
            // Cow: meat=4, hide=2. sellPrice=180, meatSellPrice=30, meatBuyPrice=80, hideSellPrice=40
            // EBV при deficit=10: 4×80 + 0×30 + 2×40 = 320 + 80 = 400 > 180 → BUTCHER.
            int ebv = TraderMath.EffectiveButcherValue(meatCount: 4, hideCount: 2,
                meatBuyPrice: 80, meatSellPrice: 30, hideSellPrice: 40, remainingDeficit: 10);
            Assert.Equal(400, ebv);
            Assert.Equal(TraderMath.LivestockDecision.Butcher,
                TraderMath.DecideUnit(sellPrice: 180, effectiveButcherValue: 400, sellOk: true));
        }
    }

    /// <summary>
    /// Реальные сценарии для проверки end-to-end вычислений.
    /// </summary>
    public class RealSessionScenario
    {
        [Fact]
        public void SmallParty_TwoPersons_NoTrim()
        {
            // 2 пеших, 5 warhorses, 9 mules, 27 sheep.
            // target_wh = 2 + ceil(0.3) = 3
            // desired_mules = floor(0.9) = 0
            // reserve = ceil(0.8) = 1
            // После livestock+wh processing: livestock=0 (butcher/sell), wh_after=3, excessWh=1
            // muleRoom = max(0, 2 - 1 - 0 - 1) = 0
            // targetMules = min(0, 0) = 0
            int totalMen = 2, unmountedInf = 2;
            Assert.Equal(3, TraderMath.TargetWarhorses(totalMen, unmountedInf));
            Assert.Equal(0, TraderMath.DesiredMules(totalMen));
            Assert.Equal(1, TraderMath.ReserveForLoot(totalMen));
            int excessWh = TraderMath.ExcessWarhorses(3, unmountedInf);  // = 1
            int room = TraderMath.MuleRoom(totalMen, livestockNow: 0, excessWarhorses: excessWh);
            Assert.Equal(0, room);
            Assert.Equal(0, TraderMath.TargetMules(TraderMath.DesiredMules(totalMen), room));
        }

        [Fact]
        public void MediumParty_TenPersons_FullBuffers()
        {
            // 10 пеших — target_wh = 10 + ceil(1.5) = 12, desired_mules = floor(4.5) = 4
            // reserve = ceil(4.0) = 4, herd_budget = 6
            // После wh-trim до 12: excessWh = max(0, 12 - 10) = 2
            // muleRoom = max(0, 10 - 4 - 0 - 2) = 4
            // targetMules = min(4, 4) = 4
            int totalMen = 10, unmountedInf = 10;
            Assert.Equal(12, TraderMath.TargetWarhorses(totalMen, unmountedInf));
            Assert.Equal(4, TraderMath.DesiredMules(totalMen));
            Assert.Equal(4, TraderMath.ReserveForLoot(totalMen));
            int excessWh = TraderMath.ExcessWarhorses(12, unmountedInf);  // = 2
            int room = TraderMath.MuleRoom(totalMen, livestockNow: 0, excessWarhorses: excessWh);
            Assert.Equal(4, room);
            Assert.Equal(4, TraderMath.TargetMules(TraderMath.DesiredMules(totalMen), room));
        }

        [Fact]
        public void LargeParty_HundredPersons()
        {
            // 100 пеших — target_wh = 100 + 15 = 115, desired_mules = floor(45) = 45
            // reserve = 40, herd_budget = 60
            // После wh-trim до 115: excessWh = 15
            // muleRoom = max(0, 100 - 40 - 0 - 15) = 45
            // targetMules = min(45, 45) = 45
            int totalMen = 100, unmountedInf = 100;
            Assert.Equal(115, TraderMath.TargetWarhorses(totalMen, unmountedInf));
            Assert.Equal(45, TraderMath.DesiredMules(totalMen));
            Assert.Equal(40, TraderMath.ReserveForLoot(totalMen));
            int excessWh = TraderMath.ExcessWarhorses(115, unmountedInf);  // = 15
            int room = TraderMath.MuleRoom(totalMen, livestockNow: 0, excessWarhorses: excessWh);
            Assert.Equal(45, room);
            Assert.Equal(45, TraderMath.TargetMules(TraderMath.DesiredMules(totalMen), room));
        }
    }
}
