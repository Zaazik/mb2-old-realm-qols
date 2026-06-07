using StatRespec.Compat;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace StatRespec
{
    public class SubModule : MBSubModuleBase
    {
        private bool _welcomeShown;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            CompatibilityCheck.Run();
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            // Register even when incompatible: the menu option then appears greyed with an
            // "incompatible" tooltip (handled in EntryCondition), matching the spec. A disabled
            // option can't start the flow, so a drifted member is never actually called.
            if (game.GameType is Campaign
                && gameStarterObject is CampaignGameStarter cgs)
            {
                cgs.AddBehavior(new StatRespec.Behaviors.StatRespecBehavior());
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            StatRespec.Behaviors.StatRespecBehavior.Instance?.PollScreenClose();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_welcomeShown) return;
            _welcomeShown = true;
            if (CompatibilityCheck.IsCompatible)
                InformationManager.DisplayMessage(new InformationMessage("StatRespec loaded", Color.FromUint(0xFF00FF66u)));
            else
                InformationManager.DisplayMessage(new InformationMessage(
                    "StatRespec: incompatible game version, feature disabled. Missing:\n" + CompatibilityCheck.Reason,
                    Color.FromUint(0xFFFF3333u)));
        }
    }
}
