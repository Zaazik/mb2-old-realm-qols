using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TOR_QoLs.Behaviors;

namespace TOR_QoLs
{
    public class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "tor.qols";
        private Harmony _harmony;
        private bool _welcomeShown;

        // Версия читается из InformationalVersion сборки (csproj <Version>...).
        private static string ModVersion =>
            typeof(SubModule).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(SubModule).Assembly.GetName().Version?.ToString()
            ?? "unknown";

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            try
            {
                _harmony = new Harmony(HarmonyId);
                _harmony.PatchAll();
            }
            catch (System.Exception ex)
            {
                HarmonyLib.FileLog.Log("[TOR_QoLs] PatchAll THREW: " + ex);
            }
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (game.GameType is Campaign && gameStarterObject is CampaignGameStarter cgs)
            {
                cgs.AddBehavior(new SmartFoodTraderBehavior());
            }
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_welcomeShown) return;
            _welcomeShown = true;
            InformationManager.DisplayMessage(
                new InformationMessage($"TOR QoLs v{ModVersion} loaded",
                    Color.FromUint(0xFF00FF66u)));
        }
    }
}
