namespace AutoEquipCompanions.Model.Templates.Shield
{
   /// <summary>
   /// Pure-math scoring для щитов по effective HP * coverage.
   ///
   /// Идея: ItemValue-scoring (default) выбирает самый дорогой — это часто кривое
   /// решение (Ornate Metal стоит 1500g но имеет length=60, тогда как vanilla
   /// Reinforced Round имеет length=70 при цене ~600g — он шире, лучше для строя).
   ///
   /// Формула:
   ///     score = HP × (1 + armor/100) × (1 + length/100)
   ///
   /// - HP — главная составляющая
   /// - armor multiplier (1 + a/100) — armor=8 даёт +8% к score
   /// - length factor (1 + length/100) — coverage; length=60 → +60%, length=70 → +70%
   ///
   /// Не учитывает: TOR ItemTrait bonuses (ShieldHealth/ShieldDamage из magical traits),
   ///                weight, shield bash damage.
   /// </summary>
   public static class EhpShieldScoring
   {
      public static double Score(int hitPoints, int bodyArmor, int weaponLength)
      {
         if (hitPoints < 0) hitPoints = 0;
         if (bodyArmor < 0) bodyArmor = 0;
         if (weaponLength < 0) weaponLength = 0;

         double armorMultiplier = 1.0 + bodyArmor / 100.0;
         double lengthMultiplier = 1.0 + weaponLength / 100.0;
         return hitPoints * armorMultiplier * lengthMultiplier;
      }
   }
}
