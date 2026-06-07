using System.Collections.Generic;
using System.Linq;
using StatRespec.Compat;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.GameState;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StatRespec.Behaviors
{
    public class StatRespecBehavior : CampaignBehaviorBase
    {
        public const int RespecCost = 10000;

        private HeroSnapshot _snapshot;
        private Hero _activeHero;
        private bool _awaitingScreen;
        private bool _screenSeen;

        public static StatRespecBehavior Instance { get; private set; }

        public StatRespecBehavior() { Instance = this; }

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
        }

        public override void SyncData(IDataStore dataStore) { }

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddGameMenuOption("town_backstreet", "sr_respec_entry",
                new TextObject("Redistribute attributes & focus (10,000 denars)").ToString(),
                EntryCondition, EntryConsequence, false, 1, false);

            starter.AddGameMenu("sr_respec_menu",
                new TextObject("Whom do you wish to retrain?").ToString(),
                null, GameMenu.MenuOverlayType.SettlementWithBoth, GameMenu.MenuFlags.None, null);

            starter.AddGameMenuOption("sr_respec_menu", "sr_pick_party",
                new TextObject("Hero in my party").ToString(),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                a => OpenPicker(partyOnly: true), false, 0, false);

            starter.AddGameMenuOption("sr_respec_menu", "sr_pick_clan",
                new TextObject("Clan companion (not in party)").ToString(),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Submenu; return true; },
                a => OpenPicker(partyOnly: false), false, 1, false);

            starter.AddGameMenuOption("sr_respec_menu", "sr_back",
                new TextObject("Back").ToString(),
                a => { a.optionLeaveType = GameMenuOption.LeaveType.Leave; return true; },
                a => GameMenu.SwitchToMenu("town_backstreet"), true, -1, false);
        }

        private bool EntryCondition(MenuCallbackArgs args)
        {
            args.optionLeaveType = GameMenuOption.LeaveType.Submenu;
            if (!CompatibilityCheck.IsCompatible)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("Incompatible with this game version");
                return true;
            }
            if (Hero.MainHero.Gold < RespecCost)
            {
                args.IsEnabled = false;
                args.Tooltip = new TextObject("Requires 10,000 denars");
            }
            return true;
        }

        private void EntryConsequence(MenuCallbackArgs args) => GameMenu.SwitchToMenu("sr_respec_menu");

        private static List<Hero> PartyHeroes()
        {
            return MobileParty.MainParty.MemberRoster.GetTroopRoster()
                .Where(e => e.Character != null && e.Character.HeroObject != null
                            && e.Character.HeroObject.IsActive && !e.Character.HeroObject.IsChild)
                .Select(e => e.Character.HeroObject)
                .ToList();
        }

        private static List<Hero> CollectHeroes(bool partyOnly)
        {
            var party = PartyHeroes();
            if (partyOnly) return party;
            var inParty = new HashSet<Hero>(party);
            return Clan.PlayerClan.Heroes.Concat(Clan.PlayerClan.Companions)
                .Where(h => h != null && h.IsActive && !h.IsChild && !inParty.Contains(h))
                .Distinct().ToList();
        }

        private void OpenPicker(bool partyOnly)
        {
            var heroes = CollectHeroes(partyOnly);
            if (heroes.Count == 0)
            {
                InformationManager.DisplayMessage(new InformationMessage("No eligible heroes here."));
                return;
            }
            var elements = heroes.Select(h => new InquiryElement(
                h, h.Name.ToString(),
                new CharacterImageIdentifier(CampaignUIHelper.GetCharacterCode(h.CharacterObject)),
                true, h.Level.ToString())).ToList();

            MBInformationManager.ShowMultiSelectionInquiry(new MultiSelectionInquiryData(
                new TextObject("Select a hero to retrain").ToString(), string.Empty,
                elements, true, 1, 1,
                GameTexts.FindText("str_done").ToString(),
                new TextObject("Cancel").ToString(),
                OnHeroPicked, null), true);
        }

        private void OnHeroPicked(List<InquiryElement> selected)
        {
            if (selected == null || selected.Count == 0) return;
            var hero = selected[0].Identifier as Hero;
            if (hero == null) return;
            StartRespec(hero);
        }

        private void StartRespec(Hero hero)
        {
            if (_awaitingScreen) return;
            try
            {
                _snapshot = HeroSnapshot.Capture(hero);
                _activeHero = hero;
                ResetHero(hero);
                var state = Game.Current.GameStateManager.CreateState<CharacterDeveloperState>(hero);
                Game.Current.GameStateManager.PushState(state);
                _screenSeen = false;
                _awaitingScreen = true;
            }
            catch (System.Exception ex)
            {
                TaleWorlds.Library.Debug.Print("[StatRespec] StartRespec failed: " + ex);
                Abort();
            }
        }

        private void ResetHero(Hero hero)
        {
            var dev = hero.HeroDeveloper;
            int attrCount = Attributes.All.Count();
            int sumAttr = Attributes.All.Sum(a => hero.GetAttributeValue(a));
            int sumFocus = Skills.All.Sum(s => dev.GetFocus(s));

            int unspentAttr = StatRespec.Math.RespecMath.UnspentAttributesAfterReset(sumAttr, dev.UnspentAttributePoints, attrCount);
            int unspentFocus = StatRespec.Math.RespecMath.UnspentFocusAfterReset(sumFocus, dev.UnspentFocusPoints);

            foreach (var a in Attributes.All)
            {
                int cur = hero.GetAttributeValue(a);
                if (cur > StatRespec.Math.RespecMath.AttributeFloor) dev.RemoveAttribute(a, cur - StatRespec.Math.RespecMath.AttributeFloor);
                else if (cur < StatRespec.Math.RespecMath.AttributeFloor) dev.AddAttribute(a, StatRespec.Math.RespecMath.AttributeFloor - cur, false);
            }
            foreach (var s in Skills.All)
            {
                int f = dev.GetFocus(s);
                if (f > 0) dev.RemoveFocus(s, f);
            }
            dev.UnspentAttributePoints = unspentAttr;
            dev.UnspentFocusPoints = unspentFocus;
        }

        private void Abort()
        {
            _awaitingScreen = false;
            try { _snapshot?.Restore(); }
            catch (System.Exception ex) { TaleWorlds.Library.Debug.Print("[StatRespec] Restore failed: " + ex); }
            _snapshot = null;
            _activeHero = null;
        }

        /// Called every frame by SubModule.OnApplicationTick.
        public void PollScreenClose()
        {
            if (!_awaitingScreen) return;
            var gsm = Game.Current?.GameStateManager;
            if (gsm == null) return;
            // Detect by STACK MEMBERSHIP, not topmost: the developer screen can push sub-screens
            // (banner editor, party screen) on top of itself; treating "not topmost" as "closed"
            // would fire the confirm prematurely. Closed = the state is gone from the stack.
            bool developerOnStack = gsm.GameStates.Any(s => s is CharacterDeveloperState);
            if (developerOnStack) { _screenSeen = true; return; }
            if (_screenSeen)
            {
                _awaitingScreen = false;
                OnScreenClosed();
            }
        }

        private void OnScreenClosed()
        {
            var hero = _activeHero;
            if (hero == null) { _snapshot = null; return; }

            try
            {
                var dev = hero.HeroDeveloper;
                var model = Campaign.Current.Models.CharacterDevelopmentModel;

                var trims = new List<(SkillObject skill, int from, int to)>();
                foreach (var s in Skills.All)
                {
                    int cur = hero.GetSkillValue(s);
                    int focus = dev.GetFocus(s);
                    int target = StatRespec.Math.RespecMath.TrimTarget(
                        cur,
                        v => model.CalculateLearningRate(hero.CharacterAttributes, focus, v, s, false).ResultNumber,
                        1023);
                    if (target < cur) trims.Add((s, cur, target));
                }

                var body = new System.Text.StringBuilder();
                if (trims.Count == 0)
                    body.AppendLine("No skills will be reduced.");
                else
                {
                    body.AppendLine("These skills exceed your new build and will be reduced:");
                    foreach (var t in trims) body.AppendLine($"  {t.skill.Name}: {t.from} -> {t.to}");
                }
                body.AppendLine();
                body.AppendLine("All perks will be reset (re-pick them afterwards).");
                body.AppendLine($"Cost: {RespecCost} denars.");

                if (hero != Hero.MainHero
                    && CampaignOptions.AutoAllocateClanMemberPerks
                    && (dev.UnspentAttributePoints > 0 || dev.UnspentFocusPoints > 0))
                {
                    body.AppendLine();
                    body.AppendLine($"Auto-allocation is ON: the game will distribute your unspent points "
                        + $"({dev.UnspentAttributePoints} attribute / {dev.UnspentFocusPoints} focus) on its next daily tick.");
                }

                var trimsCopy = trims;
                InformationManager.ShowInquiry(new InquiryData(
                    "Confirm respec", body.ToString(), true, true,
                    new TextObject("Confirm").ToString(), new TextObject("Cancel").ToString(),
                    () => Apply(hero, trimsCopy), () => Abort()), true);
            }
            catch (System.Exception ex)
            {
                // R10: the trim/summary boundary runs from the tick poll. Any failure here
                // (e.g. a polymorphic-model quirk) must log + fully roll back the reset hero,
                // never leave them half-reset. Gold is only charged in Apply, so none is charged.
                TaleWorlds.Library.Debug.Print("[StatRespec] OnScreenClosed failed: " + ex);
                Abort();
            }
        }

        private void Apply(Hero hero, List<(SkillObject skill, int from, int to)> trims)
        {
            try
            {
                var dev = hero.HeroDeveloper;
                foreach (var t in trims) dev.SetInitialSkillLevel(t.skill, t.to);
                hero.ClearPerks();
                Hero.MainHero.ChangeHeroGold(-RespecCost);
            }
            catch (System.Exception ex)
            {
                TaleWorlds.Library.Debug.Print("[StatRespec] Apply failed: " + ex);
                Abort();
                return;
            }
            _snapshot = null;
            _activeHero = null;
            InformationManager.DisplayMessage(new InformationMessage(
                $"{hero.Name} retrained.", Color.FromUint(0xFF00FF66u)));
        }
    }
}
