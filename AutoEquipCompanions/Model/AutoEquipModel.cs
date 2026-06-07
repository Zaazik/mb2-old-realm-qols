using AutoEquipCompanions.Model.Saving;
using AutoEquipCompanions.Model.Templates;
using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Inventory;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace AutoEquipCompanions.Model
{
   public class AutoEquipModel
   {
      private readonly InventoryLogic _inventoryLogic;
      private readonly HashSet<string> _lockedItems;

      public AutoEquipModel(InventoryLogic inventoryLogic)
      {
         _inventoryLogic = inventoryLogic;
         var tracker = Campaign.Current.GetCampaignBehavior<IViewDataTracker>();
         _lockedItems = new HashSet<string>(tracker.GetInventoryLocks());
      }

      private IEnumerable<ItemRosterElement> Items => MobileParty.MainParty.ItemRoster
         .Where(x => !_lockedItems.Contains(CampaignUIHelper.GetItemLockStringID(x.EquipmentElement)));

      public void AutoEquipCompanions(Dictionary<string, CharacterSettings> characterSettings)
      {
         var heroes = MobileParty.MainParty.MemberRoster
            .GetTroopRoster()
            .Where(x => x.Character.IsHero)
            .Select(x => x.Character.HeroObject)
            .Where(x => !characterSettings.ContainsKey(x.StringId) || characterSettings[x.StringId].CharacterToggle)
            // MainHero первым — забирает лучший шмот, остальные одеваются из остатков.
            .OrderByDescending(h => h == Hero.MainHero)
            .ThenBy(h => h.StringId);
         foreach (var hero in heroes)
         {
            var hasUpgraded = false;
            try
            {
               var heroSettings = characterSettings.TryGetValue(hero.StringId, out var setting)
                  ? setting
                  : new CharacterSettings().Initialize();
               foreach (var (slot, template) in heroSettings.Template.Slots.Where(x => heroSettings[x.Slot]))
               {
                  var current = hero.BattleEquipment.GetEquipmentFromSlot(slot);
                  var replacement = GetBestReplacement(hero, slot, template, current);
                  if (replacement != null)
                  {
                     DoEquip(hero, slot, replacement.Value);
                     hasUpgraded = true;
                  }
                  else if (!current.IsEmpty && !template.IsValidFor(current, slot, hero))
                  {
                     DoUnequip(hero, slot, current);
                     hasUpgraded = true;
                  }
               }
            }
            catch (Exception ex)
            {
               InformationManager.DisplayMessage(new InformationMessage($"{ex.Message}"));
            }
            finally
            {
               if (hasUpgraded)
               {
                  var pronoun = hero.IsFemale ? "her" : "his";
                  InformationManager.DisplayMessage(new InformationMessage($"{hero.Name} upgraded {pronoun} equipment"));
               }
            }
         }
      }

      private ItemRosterElement? GetBestReplacement(Hero hero, EquipmentIndex slot, ISlotTemplate template, EquipmentElement current)
      {
         var allItems = Items.ToList();
         var validItems = allItems
            .Where(x => template.IsValidFor(x.EquipmentElement, slot, hero))
            .ToList();
         // Composite sort: сначала race-specific armor (skeleton-skeleton, dwarf-dwarf и т.п.),
         // потом лучшие по EHP. Чтобы skeleton companion получил skeleton armor даже если
         // human-locked в инвентаре имеет чуть выше score.
         var ordered = validItems
            .OrderByDescending(x => Templates.RaceCompatibility.IsExactRaceMatch(x.EquipmentElement, hero) ? 1 : 0)
            .ThenByDescending(x => template.GetScore(x.EquipmentElement))
            .ToList();
         HarmonyLib.FileLog.Log(
            $"[AEC] {hero.Name} slot={slot} tmpl={template.Name} inv={allItems.Count} valid={validItems.Count} curEmpty={current.IsEmpty} curItem={current.Item?.StringId ?? "null"} curScore={template.GetScore(current):F2}");
         if (ordered.Count > 0)
         {
            var top = ordered[0];
            var topScore = template.GetScore(top.EquipmentElement);
            var curScore = template.GetScore(current);
            var isBetter = template.IsBetterThan(top.EquipmentElement, current);
            HarmonyLib.FileLog.Log(
               $"[AEC]   top={top.EquipmentElement.Item?.StringId} topScore={topScore:F2} curScore={curScore:F2} isBetter={isBetter}");
         }
         var best = ordered
            .TakeWhile(x => IsBetterComposite(template, x.EquipmentElement, current, hero))
            .Cast<ItemRosterElement?>()
            .FirstOrDefault();
         if (best.HasValue)
            HarmonyLib.FileLog.Log($"[AEC]   → picked {best.Value.EquipmentElement.Item?.StringId}");
         else if (ordered.Count > 0)
            HarmonyLib.FileLog.Log($"[AEC]   → no pick (TakeWhile empty)");
         return best;
      }

      // Composite compare: race-specific upgrade приоритет над EHP.
      // Иначе TakeWhile отказался бы от skeleton armor когда у hero current=human armor с большим EHP.
      private static bool IsBetterComposite(ISlotTemplate template, EquipmentElement candidate, EquipmentElement current, Hero hero)
      {
         var candMatch = Templates.RaceCompatibility.IsExactRaceMatch(candidate, hero);
         var curMatch = Templates.RaceCompatibility.IsExactRaceMatch(current, hero);
         if (candMatch && !curMatch) return true;   // race-specific upgrade
         if (!candMatch && curMatch) return false;  // не downgrade в generic
         return template.IsBetterThan(candidate, current);
      }

      private void DoEquip(Hero character, EquipmentIndex slot, ItemRosterElement replacement)
      {
         _inventoryLogic.AddTransferCommand(
            TransferCommand.Transfer(
               1,
               InventoryLogic.InventorySide.PlayerInventory,
               InventoryLogic.InventorySide.BattleEquipment,
               replacement,
               EquipmentIndex.None,
               slot,
               character.CharacterObject));
      }

      private void DoUnequip(Hero character, EquipmentIndex slot, EquipmentElement item)
      {
         _inventoryLogic.AddTransferCommand(
            TransferCommand.Transfer(
               1,
               InventoryLogic.InventorySide.BattleEquipment,
               InventoryLogic.InventorySide.PlayerInventory,
               new ItemRosterElement(item, 1),
               slot,
               EquipmentIndex.None,
               character.CharacterObject));
      }
   }
}
