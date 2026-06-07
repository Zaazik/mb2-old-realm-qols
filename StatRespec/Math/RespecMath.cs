using System;

namespace StatRespec.Math
{
    /// <summary>
    /// Pure respec math. No TaleWorlds types — unit-tested in isolation; the game model
    /// is injected as a delegate (rateAt) by the behavior.
    /// </summary>
    public static class RespecMath
    {
        public const int AttributeFloor = 2;

        /// Pool of unspent attribute points after reset:
        /// keep the hero's actual total, lock attributeCount*2 into the floor.
        public static int UnspentAttributesAfterReset(int sumOfCurrentAttributes, int currentUnspentAttributes, int attributeCount)
        {
            int total = sumOfCurrentAttributes + currentUnspentAttributes;
            int unspent = total - attributeCount * AttributeFloor;
            return unspent < 0 ? 0 : unspent;
        }

        /// Focus floor is 0, so the whole pool (placed + unspent) becomes unspent.
        public static int UnspentFocusAfterReset(int sumOfCurrentFocus, int currentUnspentFocus)
        {
            int unspent = sumOfCurrentFocus + currentUnspentFocus;
            return unspent < 0 ? 0 : unspent;
        }

        /// Highest reachable skill value = the smallest value where the learning rate is &lt;= 0
        /// (the skill climbs until the rate hits 0). rateAt(v) = learning rate when the skill is v.
        public static int MaxReachableSkill(Func<int, float> rateAt, int maxSearch)
        {
            for (int v = 0; v <= maxSearch; v++)
            {
                if (rateAt(v) <= 0f)
                    return v;
            }
            return maxSearch;
        }

        public static int TrimTarget(int currentSkill, Func<int, float> rateAt, int maxSearch)
        {
            int ceiling = MaxReachableSkill(rateAt, maxSearch);
            return currentSkill < ceiling ? currentSkill : ceiling;
        }
    }
}
