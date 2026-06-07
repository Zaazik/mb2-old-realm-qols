using System.Reflection;
using HarmonyLib;
using TaleWorlds.MountAndBlade;
using TOR_Core.BattleMechanics.AI.CastingAI.Components;

namespace TOR_QoLs.Patches
{
    /// <summary>
    /// Vanilla TOR's WizardAIComponent.OnTick:40 проверяет
    ///   Agent.Formation.FiringOrder.OrderType != OrderType.HoldFire
    /// — если formation в HoldFire (или Formation null), wizard НЕ кастит.
    ///
    /// В режиме "Follow Me" игроки обычно ставят HoldFire (чтобы спутники не стреляли
    /// в спину или не разбегались), но это блокирует и магию — компаньон-маг перестаёт
    /// что-либо делать. Это неудобно: hold-fire должен влиять только на стрельбу.
    ///
    /// Patch: для hero-агентов (companions + main hero) FiringOrder.HoldFire игнорируется,
    /// каст всегда дёргается. Для non-hero (моб-маги) — vanilla остаётся.
    /// </summary>
    [HarmonyPatch(typeof(WizardAIComponent), "OnTick")]
    public static class CompanionWizardCastingPatch
    {
        private static readonly FieldInfo AgentField =
            AccessTools.Field(typeof(AgentComponent), "Agent");
        private static readonly FieldInfo DtField =
            AccessTools.Field(typeof(WizardAIComponent), "_dtSinceLastOccasional");
        private static readonly MethodInfo TickOccasionallyMethod =
            AccessTools.Method(typeof(WizardAIComponent), "TickOccasionally");

        [HarmonyPrefix]
        public static bool Prefix(WizardAIComponent __instance, float dt)
        {
            var agent = AgentField?.GetValue(__instance) as Agent;
            if (agent == null || !agent.IsHero)
                return true;  // mob-wizards идут vanilla — respect formation orders

            if (agent.IsPaused)
                return false;

            // Reproduce vanilla _dtSinceLastOccasional logic (TickOccasionally reset'ит сам).
            float current = (float)(DtField?.GetValue(__instance) ?? 0f) + dt;
            DtField?.SetValue(__instance, current);
            if (current >= 3f)
            {
                TickOccasionallyMethod?.Invoke(__instance, null);
            }

            // ALWAYS execute current casting — vanilla здесь проверяла FiringOrder. Игнор.
            var casting = __instance.CurrentCastingBehavior;
            casting?.TacticalBehavior?.Execute();
            casting?.Execute();

            return false;  // skip vanilla
        }
    }
}
