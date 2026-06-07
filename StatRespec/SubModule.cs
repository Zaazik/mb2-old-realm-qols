using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace StatRespec
{
    public class SubModule : MBSubModuleBase
    {
        private bool _welcomeShown;

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            if (_welcomeShown) return;
            _welcomeShown = true;
            InformationManager.DisplayMessage(
                new InformationMessage("StatRespec loaded", Color.FromUint(0xFF00FF66u)));
        }
    }
}
