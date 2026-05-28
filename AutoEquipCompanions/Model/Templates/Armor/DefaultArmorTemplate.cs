using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TOR_Core.BattleMechanics.DamageSystem;
using TOR_Core.Extensions;
using TOR_Core.Items;

namespace AutoEquipCompanions.Model.Templates.Armor
{
   public class DefaultArmorTemplate : BaseArmorTemplate
   {
      public static readonly DefaultArmorTemplate Instance = new DefaultArmorTemplate();

      public override string Name => "default_armor";
      public override string DisplayName => "Armor";
      public override ArmorField ComparisonField => ArmorField.ArmorTotal;

      public override IEnumerable<EquipmentIndex> LegalSlots { get; } = new[]
      {
         EquipmentIndex.Head,
         EquipmentIndex.Cape,
         EquipmentIndex.Body,
         EquipmentIndex.Gloves,
         EquipmentIndex.Leg
      };

      // Базовое HP мы не знаем точно (зависит от уровня героя), используем константу
      // для относительного сравнения шмоток между собой.
      private const double BaseHP = 100.0;

      // EHP-формула для брони:
      //   ehp = (HP_base + HP_trait) × armor_multiplier × phys_resist_multiplier
      //   armor_multiplier = 1 + armorSum / 100        (каждые 100 брони ≈ ×1 HP)
      //   phys_resist_multiplier = 1 / (1 - phys_res)  (20% res → ×1.25)
      //
      // Магические резы НЕ учитываем (по запросу). Только Physical и All (ward save).
      // Tiebreaker: количество трейтов (как у оружия).
      public override double GetScore(EquipmentElement candidate)
      {
         if (candidate.IsEmpty) return 0;

         double armorSum = candidate.GetModifiedHeadArmor()
                         + candidate.GetModifiedBodyArmor()
                         + candidate.GetModifiedArmArmor()
                         + candidate.GetModifiedLegArmor();

         double healthMaxTrait = 0;
         double physResTrait = 0;
         int traitCount = 0;

         var traits = candidate.Item?.GetTraits();
         if (traits != null)
         {
            traitCount = traits.Count;
            foreach (var trait in traits)
            {
               if (trait == null) continue;

               // HP-бонус от StatsTuple
               if (trait.StatsTuple != null
                   && trait.StatsTuple.StatType == ItemTraitStatType.HealthMax)
               {
                  healthMaxTrait += trait.StatsTuple.Value;
               }

               // Physical/All resistance от ResistanceTuple (магические — игнор по требованию)
               if (trait.ResistanceTuple != null)
               {
                  var dt = trait.ResistanceTuple.ResistedDamageType;
                  if (dt == DamageType.Physical || dt == DamageType.All)
                     physResTrait += trait.ResistanceTuple.ReductionPercent;
               }
            }
         }

         // Cap резиста чтобы не делить на ноль и не было ботвы при огромных стаках.
         physResTrait = Math.Min(physResTrait, 0.95);

         double armorMultiplier = 1.0 + armorSum / 100.0;
         double resMultiplier = 1.0 / (1.0 - physResTrait);
         double ehp = (BaseHP + healthMaxTrait) * armorMultiplier * resMultiplier;

         // Tiebreaker по числу трейтов (мягкий — не перебивает реальную разницу EHP).
         return ehp + traitCount * 0.5;
      }
   }
}
