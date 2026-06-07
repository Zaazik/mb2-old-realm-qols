using System;
using StatRespec.Math;
using Xunit;

namespace StatRespec.Tests
{
    public class RespecMathTests
    {
        [Theory]
        [InlineData(30, 1, 6, 19)]   // spec example: 6 attrs at 5 + 1 unspent, floor 12 -> 19
        [InlineData(14, 0, 7, 0)]    // TOR: 7 attrs at 2 -> 14, floor 14 -> 0
        [InlineData(10, 0, 6, 0)]    // clamp: 10 - 12 = -2 -> 0
        public void UnspentAttributes(int sumAttr, int unspent, int count, int expected)
        {
            Assert.Equal(expected, RespecMath.UnspentAttributesAfterReset(sumAttr, unspent, count));
        }

        [Theory]
        [InlineData(8, 2, 10)]
        [InlineData(0, 0, 0)]
        public void UnspentFocus(int sumFocus, int unspent, int expected)
        {
            Assert.Equal(expected, RespecMath.UnspentFocusAfterReset(sumFocus, unspent));
        }

        [Fact]
        public void MaxReachableSkill_returnsFirstValueWhereRateNonPositive()
        {
            // rate > 0 below 18, exactly 0 at 18 (mimics attr 2 / focus 0 ceiling)
            Func<int, float> rate = v => 18 - v;
            Assert.Equal(18, RespecMath.MaxReachableSkill(rate, 1023));
        }

        [Fact]
        public void TrimTarget_doesNotTouchSkillBelowCeiling()
        {
            // ceiling 330 (attr 10 / focus 5): rate 1 below 330, 0 at/above 330
            Func<int, float> rate = v => v < 330 ? 1f : 0f;
            Assert.Equal(200, RespecMath.TrimTarget(200, rate, 1023)); // 200 < 330 -> unchanged
            Assert.Equal(330, RespecMath.TrimTarget(400, rate, 1023)); // 400 -> 330
        }

        [Fact]
        public void TrimTarget_cutsToCeiling()
        {
            Func<int, float> rate = v => 18 - v;
            Assert.Equal(18, RespecMath.TrimTarget(200, rate, 1023));
            Assert.Equal(10, RespecMath.TrimTarget(10, rate, 1023));
        }
    }
}
