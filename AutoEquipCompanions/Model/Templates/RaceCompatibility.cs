using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TOR_Core.Extensions;
using TOR_Core.Items;

namespace AutoEquipCompanions.Model.Templates
{
   public static class RaceCompatibility
   {
      /// <summary>
      /// true если armor's RaceLock точно совпадает с расой hero (не fallback
      /// через human-compatible). Используется как primary-sort key чтобы
      /// race-specific armor (skeleton/dwarf/orc/etc.) доминировал над generic
      /// human-locked для соответствующих компаньонов.
      /// </summary>
      public static bool IsExactRaceMatch(EquipmentElement candidate, Hero hero)
      {
         if (candidate.IsEmpty || candidate.Item == null) return false;
         if (hero == null || hero.CharacterObject == null) return false;
         if (!candidate.Item.HasArmorComponent) return false;
         try
         {
            var info = candidate.Item.GetTorSpecificDataReadOnly();
            var raceLock = info?.RaceLock;
            if (string.IsNullOrEmpty(raceLock)) return false;
            return hero.CharacterObject.Race == FaceGen.GetRaceOrDefault(raceLock);
         }
         catch
         {
            return false;
         }
      }

      public static bool CanWear(EquipmentElement candidate, Hero hero)
      {
         if (candidate.IsEmpty || candidate.Item == null) return false;
         if (hero == null || hero.CharacterObject == null) return true;
         if (!candidate.Item.HasArmorComponent) return true;
         try
         {
            // Override: TOR не включает skeleton в _humanCompatibleRaces, но wright/death-knights
            // визуально и лорно носят empire/bretonnian armor. Разрешаем human-locked armor скелетам.
            var info = candidate.Item.GetTorSpecificDataReadOnly();
            var raceLock = info?.RaceLock;
            if (raceLock == "human"
                && hero.CharacterObject.Race == FaceGen.GetRaceOrDefault("skeleton"))
               return true;

            return ExtendedItemObjectManager.CanCharacterUseItemBasedOnRace(
               candidate.Item, hero.CharacterObject);
         }
         catch
         {
            return true;
         }
      }
   }
}
