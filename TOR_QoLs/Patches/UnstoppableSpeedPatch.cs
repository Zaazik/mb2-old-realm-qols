using HarmonyLib;
using TaleWorlds.CampaignSystem;

namespace TOR_QoLs.Patches
{
    /// <summary>
    /// Vanilla имеет два варианта каждой скорости: Stoppable* и Unstoppable*.
    /// Stoppable* — game в любой момент может авто-pause (encounter check,
    /// menu trigger, route change и т.п.). Это и есть тот fake-trigger когда
    /// "нет опасности в радиусе но скорость скидывается на x1".
    ///
    /// Patch перенаправляет любое Stoppable* setting на Unstoppable* —
    /// пользователь сохраняет контроль над pause через Space (Stop остаётся
    /// как был, не трогаем). Auto-pause при подходе врага тоже отключается —
    /// это сознательный trade-off (предупреждение уходит, но игрок сам видит).
    /// </summary>
    [HarmonyPatch(typeof(Campaign), "set_TimeControlMode")]
    public static class UnstoppableSpeedPatch
    {
        [HarmonyPrefix]
        public static void Prefix(ref CampaignTimeControlMode value)
        {
            if (value == CampaignTimeControlMode.StoppablePlay)
                value = CampaignTimeControlMode.UnstoppablePlay;
            else if (value == CampaignTimeControlMode.StoppableFastForward)
                value = CampaignTimeControlMode.UnstoppableFastForward;
        }
    }
}
