using AutoEquipCompanions.Model.Templates.Shield;
using Xunit;

namespace TOR_QoLs.Tests
{
    public class EhpShieldScoringTests
    {
        [Fact]
        public void ZeroInputs_ReturnsZero()
        {
            Assert.Equal(0.0, EhpShieldScoring.Score(0, 0, 0));
        }

        [Fact]
        public void NegativeInputs_TreatedAsZero()
        {
            Assert.Equal(0.0, EhpShieldScoring.Score(-100, -10, -50));
        }

        [Fact]
        public void ArmorZero_LengthZero_ScoreEquals_HP()
        {
            // armor=0, length=0 → multipliers=1, score=HP
            Assert.Equal(500.0, EhpShieldScoring.Score(500, 0, 0));
        }

        [Fact]
        public void ArmorScaling_8Percent_Per_8Armor()
        {
            var bare = EhpShieldScoring.Score(100, 0, 0);
            var armored = EhpShieldScoring.Score(100, 8, 0);
            Assert.Equal(108.0, armored, 3);
            Assert.Equal(bare * 1.08, armored, 3);
        }

        [Fact]
        public void LengthScaling_60Percent_At_Length60()
        {
            var noLen = EhpShieldScoring.Score(100, 0, 0);
            var len60 = EhpShieldScoring.Score(100, 0, 60);
            Assert.Equal(160.0, len60, 3);
            Assert.Equal(noLen * 1.60, len60, 3);
        }

        [Fact]
        public void HpIncreases_Score_Linearly()
        {
            var hp100 = EhpShieldScoring.Score(100, 10, 60);
            var hp200 = EhpShieldScoring.Score(200, 10, 60);
            Assert.Equal(hp100 * 2.0, hp200, 3);
        }

        // Реальные с моба: vanilla Reinforced Round (530 HP с modifier) vs TOR Legendary Ornate Metal (730 HP)
        [Fact]
        public void RealShield_TorLegendary_730HP_Beats_VanillaReinforced_530HP()
        {
            var vanilla = EhpShieldScoring.Score(hitPoints: 530, bodyArmor: 1, weaponLength: 70);
            var torLeg  = EhpShieldScoring.Score(hitPoints: 730, bodyArmor: 8, weaponLength: 60);

            // vanilla = 530 × 1.01 × 1.70 = 910.01
            // torLeg  = 730 × 1.08 × 1.60 = 1261.44
            Assert.Equal(910.01, vanilla, 2);
            Assert.Equal(1261.44, torLeg, 2);
            Assert.True(torLeg > vanilla);
        }

        [Fact]
        public void HP_Still_Dominates_Length()
        {
            // shield с маленькой длиной но большим HP — должен бить shield с длиной но малым HP
            var hpBig = EhpShieldScoring.Score(hitPoints: 700, bodyArmor: 0, weaponLength: 40);
            var lenBig = EhpShieldScoring.Score(hitPoints: 400, bodyArmor: 0, weaponLength: 100);
            // hpBig   = 700 × 1.40 = 980
            // lenBig  = 400 × 2.00 = 800
            Assert.True(hpBig > lenBig);
        }
    }
}
