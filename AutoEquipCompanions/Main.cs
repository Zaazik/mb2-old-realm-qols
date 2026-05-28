using AutoEquipCompanions.Model.Saving;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace AutoEquipCompanions
{
   public class Main : MBSubModuleBase
   {

      private AutoEquipBehavior _behavior;
      private bool _welcomeShown;
      public static GameSettings GameSettings { get; private set; } = new GameSettings();

      protected override void OnSubModuleLoad()
      {
         base.OnSubModuleLoad();
         GameSettings.Load();
         CampaignSettings.Initialize();
      }

      protected override void OnBeforeInitialModuleScreenSetAsRoot()
      {
         base.OnBeforeInitialModuleScreenSetAsRoot();
         if (_welcomeShown) return;
         _welcomeShown = true;
         InformationManager.DisplayMessage(
            new InformationMessage("Auto Equip Companions loaded",
               Color.FromUint(0xFF66FFFFu)));
      }

      protected override void InitializeGameStarter(Game game, IGameStarter starterObject)
      {
         if (starterObject is CampaignGameStarter campaignGameStarter)
         {
            _behavior = new AutoEquipBehavior();
            campaignGameStarter.AddBehavior(_behavior);
         }
      }

      public override void OnGameEnd(Game game)
      {
         _behavior?.UnregisterEvents();
      }
   }
}
