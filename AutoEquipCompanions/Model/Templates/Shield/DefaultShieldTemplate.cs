namespace AutoEquipCompanions.Model.Templates.Shield
{
   public class DefaultShieldTemplate : BaseShieldTemplate
   {
      public static readonly DefaultShieldTemplate Instance = new DefaultShieldTemplate();

      public override string Name => "default_shield";
      public override string DisplayName => "Shield";
      // Scoring по EHP × coverage (HP × (1 + armor/100) × length / 100) — см. EhpShieldScoring.
      // Default раньше был ItemValue, что выбирало дорогие "ornate" щиты с маленьким length.
      public override ShieldField ComparisonField => ShieldField.Ehp;
   }
}
