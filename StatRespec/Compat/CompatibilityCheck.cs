using System;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.Core;

namespace StatRespec.Compat
{
    /// <summary>
    /// Load-time guard. Verifies (via reflection, signature-exact) that every public game member
    /// the mod CALLS still exists. If anything drifted, IsCompatible = false and the menu option
    /// is shown disabled — never a mid-flow MissingMethodException.
    /// </summary>
    public static class CompatibilityCheck
    {
        public static bool IsCompatible { get; private set; }
        public static string Reason { get; private set; } = "";

        public static void Run()
        {
            var sb = new StringBuilder();

            void Method(Type t, string name, Type ret, params Type[] ps)
            {
                if (!SignatureCheck.MethodMatches(t, name, ret, ps))
                    sb.AppendLine($"{t?.FullName}.{name}({string.Join(",", System.Array.ConvertAll(ps, p => p.Name))})->{ret.Name}");
            }
            void Prop(Type t, string name, Type pt, bool setter)
            {
                if (!SignatureCheck.PropertyMatches(t, name, pt, setter))
                    sb.AppendLine($"{t?.FullName}.{name}:{pt.Name}{(setter ? "(set)" : "")}");
            }

            Method(typeof(Hero), "GetAttributeValue", typeof(int), typeof(CharacterAttribute));
            Method(typeof(Hero), "GetSkillValue", typeof(int), typeof(SkillObject));
            Method(typeof(Hero), "GetPerkValue", typeof(bool), typeof(PerkObject));
            Method(typeof(Hero), "ClearPerks", typeof(void));
            Method(typeof(Hero), "ChangeHeroGold", typeof(void), typeof(int));

            Method(typeof(HeroDeveloper), "GetFocus", typeof(int), typeof(SkillObject));
            Method(typeof(HeroDeveloper), "AddAttribute", typeof(void), typeof(CharacterAttribute), typeof(int), typeof(bool));
            Method(typeof(HeroDeveloper), "RemoveAttribute", typeof(void), typeof(CharacterAttribute), typeof(int));
            Method(typeof(HeroDeveloper), "AddFocus", typeof(void), typeof(SkillObject), typeof(int), typeof(bool));
            Method(typeof(HeroDeveloper), "RemoveFocus", typeof(void), typeof(SkillObject), typeof(int));
            Method(typeof(HeroDeveloper), "AddPerk", typeof(void), typeof(PerkObject));
            Method(typeof(HeroDeveloper), "SetInitialSkillLevel", typeof(void), typeof(SkillObject), typeof(int));
            Prop(typeof(HeroDeveloper), "UnspentAttributePoints", typeof(int), setter: true);
            Prop(typeof(HeroDeveloper), "UnspentFocusPoints", typeof(int), setter: true);

            // Trim path: the most version-fragile call (the active CharacterDevelopmentModel,
            // overridden by TOR) plus the members it reads. Guarding these greys the menu on a
            // future drift instead of resetting the hero and then throwing mid-flow.
            Method(typeof(CharacterDevelopmentModel), "CalculateLearningRate", typeof(ExplainedNumber),
                typeof(IReadOnlyPropertyOwner<CharacterAttribute>), typeof(int), typeof(int), typeof(SkillObject), typeof(bool));
            Prop(typeof(Hero), "CharacterAttributes", typeof(IReadOnlyPropertyOwner<CharacterAttribute>), setter: false);
            Prop(typeof(CampaignOptions), "AutoAllocateClanMemberPerks", typeof(bool), setter: false);

            Reason = sb.ToString();
            IsCompatible = Reason.Length == 0;
        }
    }
}
