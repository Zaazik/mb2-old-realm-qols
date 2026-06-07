using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.Core;

namespace StatRespec
{
    /// <summary>Pre-respec state of one hero, enough to fully roll back on cancel.</summary>
    public sealed class HeroSnapshot
    {
        private readonly Hero _hero;
        private readonly Dictionary<CharacterAttribute, int> _attributes = new Dictionary<CharacterAttribute, int>();
        private readonly Dictionary<SkillObject, int> _focus = new Dictionary<SkillObject, int>();
        private readonly List<PerkObject> _perks = new List<PerkObject>();
        private readonly int _unspentAttr;
        private readonly int _unspentFocus;

        private HeroSnapshot(Hero hero)
        {
            _hero = hero;
            var dev = hero.HeroDeveloper;
            foreach (var a in Attributes.All) _attributes[a] = hero.GetAttributeValue(a);
            foreach (var s in Skills.All) _focus[s] = dev.GetFocus(s);
            foreach (var p in PerkObject.All) if (hero.GetPerkValue(p)) _perks.Add(p);
            _unspentAttr = dev.UnspentAttributePoints;
            _unspentFocus = dev.UnspentFocusPoints;
        }

        public static HeroSnapshot Capture(Hero hero) => new HeroSnapshot(hero);

        public void Restore()
        {
            var dev = _hero.HeroDeveloper;

            foreach (var kv in _attributes)
            {
                int cur = _hero.GetAttributeValue(kv.Key);
                if (cur > kv.Value) dev.RemoveAttribute(kv.Key, cur - kv.Value);
                else if (cur < kv.Value) dev.AddAttribute(kv.Key, kv.Value - cur, false);
            }

            foreach (var kv in _focus)
            {
                int cur = dev.GetFocus(kv.Key);
                if (cur > 0) dev.RemoveFocus(kv.Key, cur);
                if (kv.Value > 0) dev.AddFocus(kv.Key, kv.Value, false);
            }

            _hero.ClearPerks();
            foreach (var p in _perks) dev.AddPerk(p);

            dev.UnspentAttributePoints = _unspentAttr;
            dev.UnspentFocusPoints = _unspentFocus;
        }
    }
}
