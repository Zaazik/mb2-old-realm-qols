using AutoEquipCompanions.Model.Templates;
using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TOR_Core.Extensions;
using TOR_Core.Items;

namespace AutoEquipCompanions.Model.Templates.Weapon
{
   public class SameTypeWeaponTemplate : BaseWeaponTemplate
   {
      public static readonly SameTypeWeaponTemplate Instance = new SameTypeWeaponTemplate();

      // Множитель для magic-amplifier'а. Amp 0.05 → 5000 score, доминирует над ItemValue (~1000).
      private const double AmplifierWeight = 100000.0;

      public override string Name => "same_type_weapon";
      public override string DisplayName => "Match Current Type";
      public override WeaponField ComparisonField => WeaponField.Value;
      public override IEnumerable<ItemObject.ItemTypeEnum> AllowedItemTypes { get; } = Array.Empty<ItemObject.ItemTypeEnum>();

      public override double GetScore(EquipmentElement candidate)
      {
         if (candidate.IsEmpty) return 0;

         // Базовый скор — ItemValue из BaseWeaponTemplate.
         double baseScore = base.GetScore(candidate);

         // Суммарный magic amplifier + количество трейтов.
         double ampSum = 0;
         int traitCount = 0;
         var traits = candidate.Item?.GetTraits();
         if (traits != null)
         {
            traitCount = traits.Count;
            foreach (var trait in traits)
            {
               if (trait?.AmplifierTuple != null)
                  ampSum += trait.AmplifierTuple.DamageAmplifier;
            }
         }

         // Приоритет: amp >> наличие любого trait'а >> ItemValue.
         // traitCount * 0.5 — мягкий tiebreaker: ломает точное равенство ItemValue,
         // но не перебивает реальную разницу в стоимости (ItemValue целочисленный).
         return ampSum * AmplifierWeight + traitCount * 0.5 + baseScore;
      }

      public override bool IsValidFor(EquipmentElement candidate, EquipmentIndex slot, Hero hero)
      {
         if (candidate.IsEmpty || !candidate.Item.HasWeaponComponent)
            return false;

         var current = hero.BattleEquipment.GetEquipmentFromSlot(slot);
         if (current.IsEmpty)
            return false;

         if (!IsSameEffectiveType(candidate, current))
            return false;

         // Torch stat-sticks (TOR magic offhand staves: Death Wizard, Staff of Volans, etc.)
         // имеют item_usage = "torch" и формально классифицированы OneHandedWeapon.
         // Не давать их свапать на реальное оружие того же type'а, и наоборот.
         if (IsTorchStatStick(current) != IsTorchStatStick(candidate))
            return false;

         var heroIsMounted = !hero.BattleEquipment[EquipmentIndex.Horse].IsEmpty;
         if (heroIsMounted)
         {
            if (!WeaponHelpers.RequiresNoMount(current) && WeaponHelpers.RequiresNoMount(candidate))
               return false;

            if (WeaponHelpers.IsCouchable(current) && !WeaponHelpers.IsCouchable(candidate))
               return false;
         }

         return MeetsDifficultyRequirement(candidate, hero);
      }

      private static bool IsTorchStatStick(EquipmentElement element)
      {
         var usage = element.Item?.PrimaryWeapon?.ItemUsage;
         return !string.IsNullOrEmpty(usage) && usage == "torch";
      }

      private static bool IsSameEffectiveType(EquipmentElement candidate, EquipmentElement current)
      {
         // Сравнение по WeaponClass (точнее чем ItemType):
         //   OneHandedSword != OneHandedMace != OneHandedAxe != OneHandedPolearm
         //   TwoHandedSword != TwoHandedAxe != TwoHandedMace != TwoHandedPolearm
         //   и т.д.
         // Это удерживает топоры в слоте топоров, маки в слоте маков, посохи в слоте посохов.
         var candidateClass = candidate.Item?.PrimaryWeapon?.WeaponClass;
         var currentClass = current.Item?.PrimaryWeapon?.WeaponClass;
         if (candidateClass != currentClass) return false;

         // Bastard sword carve-out — оставляем только в рамках того же WeaponClass.
         // (Бастард-меч всё равно TwoHandedSword по WeaponClass; не подмешивает 1H-мечи.)
         return true;
      }

      private static ItemObject.ItemTypeEnum GetEffectiveType(EquipmentElement element)
      {
         if (Main.GameSettings.BastardSwordsAreOneHanded
             && element.Item.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon
             && IsBastardSword(element))
            return ItemObject.ItemTypeEnum.OneHandedWeapon;
         return element.Item.ItemType;
      }
   }
}
