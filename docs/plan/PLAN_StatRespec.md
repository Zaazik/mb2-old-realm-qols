# StatRespec Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Standalone Bannerlord mod that lets the player reset a chosen hero's attributes (to floor 2) and skill-focus (to 0), re-distribute the freed points on the native character screen, then trim over-cap skills — for 10 000 denars, via a menu option in the tavern district.

**Architecture:** Pure campaign behavior + official menu/inquiry/game-state APIs. NO Harmony patch, NO new save data, NO reflection calls (only a load-time reflection signature check). Skill-trim math and the signature comparison are pure functions (primitive in/out + delegates) unit-tested in isolation; the behavior wires the real game model into those functions. The native `CharacterDeveloperState` screen is reused for redistribution.

**Tech Stack:** C#, net48, SDK-style csproj, xUnit (net48), TaleWorlds vanilla DLLs only. Targets game v1.3.15; works in vanilla and TOR (reads the active `CharacterDevelopmentModel` polymorphically).

**Spec:** `docs/spec/SPEC_StatRespec.md`.

---

## File Structure

New module folder `StatRespec/` (sibling of `TOR_QoLs/`), plus a test project `StatRespec.Tests/`.

| File | Responsibility |
|---|---|
| `StatRespec/StatRespec.csproj` | SDK-style net48 project; references only vanilla TaleWorlds DLLs; outputs to game `Modules\StatRespec\bin\Win64_Shipping_Client`; copies `SubModule.xml`. |
| `StatRespec/SubModule.xml` | Module manifest; load order after SandBox/StoryMode; no TOR dependency. |
| `StatRespec/SubModule.cs` | `MBSubModuleBase`: runs the compatibility check on load; registers `StatRespecBehavior` on campaign start; polls for native-screen close via `OnApplicationTick`. |
| `StatRespec/Math/RespecMath.cs` | **Pure** functions: unspent-pool computation, max-reachable-skill / trim-target via a rate delegate. No TaleWorlds types → linked into the test project. |
| `StatRespec/Compat/SignatureCheck.cs` | **Pure** reflection helper: does a member with the expected signature exist on a type. No TaleWorlds types → linked into the test project. |
| `StatRespec/Compat/CompatibilityCheck.cs` | Runs `SignatureCheck` against the concrete game members the mod calls; exposes `IsCompatible` + `Reason`. References game types (not linked into tests). |
| `StatRespec/HeroSnapshot.cs` | Capture/restore of a hero's attributes, focus, unspent points, perks (everything the flow may touch before apply). |
| `StatRespec/Behaviors/StatRespecBehavior.cs` | The flow: menu option + sub-menu, gold gate, hero pickers, reset, open native screen, on-close trim + summary + confirm/cancel, payment. |
| `StatRespec.Tests/StatRespec.Tests.csproj` | xUnit; links `RespecMath.cs` + `SignatureCheck.cs` directly (no ProjectReference). |
| `StatRespec.Tests/RespecMathTests.cs` | Unit tests for `RespecMath`. |
| `StatRespec.Tests/SignatureCheckTests.cs` | Unit tests for `SignatureCheck` against local stand-in types. |

**Spec refinement (note):** the spec says "if nothing is trimmed, skip the popup." Because the apply step always charges 10 000 and always clears perks, this plan ALWAYS shows the confirm popup (it is the charge/perk-wipe/abort gate); when no skill is trimmed it shows "No skills will be reduced." This is the only deviation from the spec and is called out in Task 8.

---

## Task 1: Module scaffold (builds, loads, prints a line)

**Files:**
- Create: `StatRespec/StatRespec.csproj`
- Create: `StatRespec/SubModule.xml`
- Create: `StatRespec/SubModule.cs`

- [ ] **Step 1: Create `StatRespec/StatRespec.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>12</LangVersion>
    <AssemblyName>StatRespec</AssemblyName>
    <RootNamespace>StatRespec</RootNamespace>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
    <AppendRuntimeIdentifierToOutputPath>false</AppendRuntimeIdentifierToOutputPath>
    <Deterministic>false</Deterministic>
    <Configuration Condition=" '$(Configuration)' == '' ">Release</Configuration>

    <GameDir>D:\SteamLibrary\steamapps\common\Mount &amp; Blade II Bannerlord</GameDir>
    <GameBin>$(GameDir)\bin\Win64_Shipping_Client</GameBin>
    <ModuleDir>$(GameDir)\Modules\StatRespec</ModuleDir>
    <OutputPath>$(ModuleDir)\bin\Win64_Shipping_Client\</OutputPath>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="TaleWorlds.Core"><HintPath>$(GameBin)\TaleWorlds.Core.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="TaleWorlds.CampaignSystem"><HintPath>$(GameBin)\TaleWorlds.CampaignSystem.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="TaleWorlds.CampaignSystem.ViewModelCollection"><HintPath>$(GameBin)\TaleWorlds.CampaignSystem.ViewModelCollection.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="TaleWorlds.Core.ViewModelCollection"><HintPath>$(GameBin)\TaleWorlds.Core.ViewModelCollection.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="TaleWorlds.Library"><HintPath>$(GameBin)\TaleWorlds.Library.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="TaleWorlds.Localization"><HintPath>$(GameBin)\TaleWorlds.Localization.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="TaleWorlds.MountAndBlade"><HintPath>$(GameBin)\TaleWorlds.MountAndBlade.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="TaleWorlds.ObjectSystem"><HintPath>$(GameBin)\TaleWorlds.ObjectSystem.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="TaleWorlds.DotNet"><HintPath>$(GameBin)\TaleWorlds.DotNet.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="TaleWorlds.LinQuick"><HintPath>$(GameBin)\TaleWorlds.LinQuick.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>

  <Target Name="CopySubModuleXml" AfterTargets="Build">
    <Copy SourceFiles="SubModule.xml" DestinationFolder="$(ModuleDir)" />
  </Target>

</Project>
```

- [ ] **Step 2: Create `StatRespec/SubModule.xml`**

```xml
<Module>
  <Name value="StatRespec" />
  <Id value="StatRespec" />
  <Version value="v1.0.0" />
  <SingleplayerModule value="true" />
  <ModuleCategory value="Singleplayer" />
  <DependedModules>
    <DependedModule Id="Native" />
    <DependedModule Id="SandBoxCore" />
    <DependedModule Id="Sandbox" />
    <DependedModule Id="StoryMode" />
  </DependedModules>
  <SubModules>
    <SubModule>
      <Name value="StatRespec" />
      <DLLName value="StatRespec.dll" />
      <SubModuleClassType value="StatRespec.SubModule" />
      <Tags />
    </SubModule>
  </SubModules>
</Module>
```

- [ ] **Step 3: Create `StatRespec/SubModule.cs` (minimal)**

```csharp
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
```

- [ ] **Step 4: Build**

Run: `dotnet build StatRespec/StatRespec.csproj -c Release`
Expected: `Build succeeded`, `StatRespec.dll` written under `Modules\StatRespec\bin\Win64_Shipping_Client\`, `SubModule.xml` copied to `Modules\StatRespec\`.

- [ ] **Step 5: Manual load check**

Enable `StatRespec` in the launcher (any position after StoryMode), start the game. Expected: green "StatRespec loaded" message on the main menu.

- [ ] **Step 6: Commit**

```bash
git add StatRespec/StatRespec.csproj StatRespec/SubModule.xml StatRespec/SubModule.cs
git commit -m "feat(StatRespec): module scaffold that loads"
```

---

## Task 2: RespecMath (pure pool + trim math) — TDD

**Files:**
- Create: `StatRespec/Math/RespecMath.cs`
- Create: `StatRespec.Tests/StatRespec.Tests.csproj`
- Create: `StatRespec.Tests/RespecMathTests.cs`

- [ ] **Step 1: Create the test project `StatRespec.Tests/StatRespec.Tests.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>12</LangVersion>
    <IsPackable>false</IsPackable>
    <RootNamespace>StatRespec.Tests</RootNamespace>
    <AssemblyName>StatRespec.Tests</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <!-- Link pure helpers directly (repo convention) — no ProjectReference, so tests
       don't depend on the deployed DLL that the running game locks. -->
  <ItemGroup>
    <Compile Include="..\StatRespec\Math\RespecMath.cs" Link="RespecMath.cs" />
    <Compile Include="..\StatRespec\Compat\SignatureCheck.cs" Link="SignatureCheck.cs" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write the failing tests `StatRespec.Tests/RespecMathTests.cs`**

```csharp
using System;
using StatRespec.Math;
using Xunit;

namespace StatRespec.Tests
{
    public class RespecMathTests
    {
        [Theory]
        [InlineData(30, 1, 6, 19)]   // spec example: 6 attrs at 5 + 1 unspent, floor 12 -> 19
        [InlineData(14, 0, 7, 0)]    // TOR: 7 attrs at 2 -> 14, floor 14 -> 0
        [InlineData(10, 0, 6, 0)]    // clamp: 10 - 12 = -2 -> 0
        public void UnspentAttributes(int sumAttr, int unspent, int count, int expected)
        {
            Assert.Equal(expected, RespecMath.UnspentAttributesAfterReset(sumAttr, unspent, count));
        }

        [Theory]
        [InlineData(8, 2, 10)]
        [InlineData(0, 0, 0)]
        public void UnspentFocus(int sumFocus, int unspent, int expected)
        {
            Assert.Equal(expected, RespecMath.UnspentFocusAfterReset(sumFocus, unspent));
        }

        [Fact]
        public void MaxReachableSkill_returnsFirstValueWhereRateNonPositive()
        {
            // rate > 0 below 18, exactly 0 at 18 (mimics attr 2 / focus 0 ceiling)
            Func<int, float> rate = v => 18 - v;
            Assert.Equal(18, RespecMath.MaxReachableSkill(rate, 1023));
        }

        [Fact]
        public void TrimTarget_doesNotTouchSkillBelowCeiling()
        {
            // ceiling 330 (attr 10 / focus 5): rate 1 below 330, 0 at/above 330
            Func<int, float> rate = v => v < 330 ? 1f : 0f;
            Assert.Equal(200, RespecMath.TrimTarget(200, rate, 1023)); // 200 < 330 -> unchanged
            Assert.Equal(330, RespecMath.TrimTarget(400, rate, 1023)); // 400 -> 330
        }

        [Fact]
        public void TrimTarget_cutsToCeiling()
        {
            Func<int, float> rate = v => 18 - v;
            Assert.Equal(18, RespecMath.TrimTarget(200, rate, 1023));
            Assert.Equal(10, RespecMath.TrimTarget(10, rate, 1023));
        }
    }
}
```

- [ ] **Step 3: Run tests — verify they fail (no RespecMath yet)**

Run: `dotnet test StatRespec.Tests/StatRespec.Tests.csproj`
Expected: compile error / FAIL — `RespecMath` does not exist.

- [ ] **Step 4: Implement `StatRespec/Math/RespecMath.cs`**

```csharp
using System;

namespace StatRespec.Math
{
    /// <summary>
    /// Pure respec math. No TaleWorlds types — unit-tested in isolation; the game model
    /// is injected as a delegate (rateAt) by the behavior.
    /// </summary>
    public static class RespecMath
    {
        public const int AttributeFloor = 2;

        /// Pool of unspent attribute points after reset:
        /// keep the hero's actual total, lock attributeCount*2 into the floor.
        public static int UnspentAttributesAfterReset(int sumOfCurrentAttributes, int currentUnspentAttributes, int attributeCount)
        {
            int total = sumOfCurrentAttributes + currentUnspentAttributes;
            int unspent = total - attributeCount * AttributeFloor;
            return unspent < 0 ? 0 : unspent;
        }

        /// Focus floor is 0, so the whole pool (placed + unspent) becomes unspent.
        public static int UnspentFocusAfterReset(int sumOfCurrentFocus, int currentUnspentFocus)
        {
            int unspent = sumOfCurrentFocus + currentUnspentFocus;
            return unspent < 0 ? 0 : unspent;
        }

        /// Highest reachable skill value = the smallest value where the learning rate is &lt;= 0
        /// (the skill climbs until the rate hits 0). rateAt(v) = learning rate when the skill is v.
        public static int MaxReachableSkill(Func<int, float> rateAt, int maxSearch)
        {
            for (int v = 0; v <= maxSearch; v++)
            {
                if (rateAt(v) <= 0f)
                    return v;
            }
            return maxSearch;
        }

        public static int TrimTarget(int currentSkill, Func<int, float> rateAt, int maxSearch)
        {
            int ceiling = MaxReachableSkill(rateAt, maxSearch);
            return currentSkill < ceiling ? currentSkill : ceiling;
        }
    }
}
```

- [ ] **Step 5: Run tests — verify they pass**

Run: `dotnet test StatRespec.Tests/StatRespec.Tests.csproj`
Expected: all tests PASS.

- [ ] **Step 6: Commit**

```bash
git add StatRespec/Math/RespecMath.cs StatRespec.Tests/StatRespec.Tests.csproj StatRespec.Tests/RespecMathTests.cs
git commit -m "feat(StatRespec): pure respec math + unit tests"
```

---

## Task 3: SignatureCheck (pure reflection helper) — TDD

**Files:**
- Create: `StatRespec/Compat/SignatureCheck.cs`
- Create: `StatRespec.Tests/SignatureCheckTests.cs`

- [ ] **Step 1: Write the failing tests `StatRespec.Tests/SignatureCheckTests.cs`**

```csharp
using StatRespec.Compat;
using Xunit;

namespace StatRespec.Tests
{
    public class SignatureCheckTests
    {
        private class Target
        {
            public void DoIt(int a, string b) { }
            public int Ret() => 0;
        }

        [Fact]
        public void MethodMatches_true_whenSignatureMatches()
        {
            Assert.True(SignatureCheck.MethodMatches(typeof(Target), "DoIt", typeof(void), typeof(int), typeof(string)));
            Assert.True(SignatureCheck.MethodMatches(typeof(Target), "Ret", typeof(int)));
        }

        [Fact]
        public void MethodMatches_false_whenNameMissing()
        {
            Assert.False(SignatureCheck.MethodMatches(typeof(Target), "Nope", typeof(void)));
        }

        [Fact]
        public void MethodMatches_false_whenParamsDiffer()
        {
            Assert.False(SignatureCheck.MethodMatches(typeof(Target), "DoIt", typeof(void), typeof(string)));
        }

        [Fact]
        public void MethodMatches_false_whenReturnDiffers()
        {
            Assert.False(SignatureCheck.MethodMatches(typeof(Target), "Ret", typeof(string)));
        }

        [Fact]
        public void MethodMatches_false_whenTypeNull()
        {
            Assert.False(SignatureCheck.MethodMatches(null, "DoIt", typeof(void)));
        }
    }
}
```

- [ ] **Step 2: Run tests — verify they fail**

Run: `dotnet test StatRespec.Tests/StatRespec.Tests.csproj --filter SignatureCheckTests`
Expected: compile error / FAIL — `SignatureCheck` does not exist.

- [ ] **Step 3: Implement `StatRespec/Compat/SignatureCheck.cs`**

```csharp
using System;
using System.Reflection;

namespace StatRespec.Compat
{
    /// <summary>Pure reflection helper: verify a member exists with the exact expected signature.</summary>
    public static class SignatureCheck
    {
        private const BindingFlags AllInstanceAndStatic =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static bool MethodMatches(Type declaringType, string name, Type returnType, params Type[] paramTypes)
        {
            if (declaringType == null) return false;
            MethodInfo m = declaringType.GetMethod(name, AllInstanceAndStatic, null, paramTypes ?? Type.EmptyTypes, null);
            return m != null && m.ReturnType == returnType;
        }

        public static bool PropertyMatches(Type declaringType, string name, Type propertyType, bool needsSetter)
        {
            if (declaringType == null) return false;
            PropertyInfo p = declaringType.GetProperty(name, AllInstanceAndStatic);
            if (p == null || p.PropertyType != propertyType) return false;
            return !needsSetter || p.GetSetMethod(nonPublic: true) != null;
        }
    }
}
```

- [ ] **Step 4: Run tests — verify they pass**

Run: `dotnet test StatRespec.Tests/StatRespec.Tests.csproj`
Expected: all tests PASS (RespecMath + SignatureCheck).

- [ ] **Step 5: Commit**

```bash
git add StatRespec/Compat/SignatureCheck.cs StatRespec.Tests/SignatureCheckTests.cs
git commit -m "feat(StatRespec): pure signature-check helper + unit tests"
```

---

## Task 4: CompatibilityCheck against concrete game members

**Files:**
- Create: `StatRespec/Compat/CompatibilityCheck.cs`

- [ ] **Step 1: Implement `StatRespec/Compat/CompatibilityCheck.cs`**

```csharp
using System;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace StatRespec.Compat
{
    /// <summary>
    /// Load-time guard. Verifies (via reflection, signature-exact) that every public game member
    /// the mod CALLS still exists. If anything drifted, IsCompatible = false and the menu option
    /// is shown disabled — never a mid-flow MissingMethodException.
    /// </summary>
    public static class CompatibilityCheck
    {
        public static bool IsCompatible { get; private set; }
        public static string Reason { get; private set; } = "";

        public static void Run()
        {
            var sb = new StringBuilder();

            void Method(Type t, string name, Type ret, params Type[] ps)
            {
                if (!SignatureCheck.MethodMatches(t, name, ret, ps))
                    sb.AppendLine($"{t?.FullName}.{name}({string.Join(",", System.Array.ConvertAll(ps, p => p.Name))})->{ret.Name}");
            }
            void Prop(Type t, string name, Type pt, bool setter)
            {
                if (!SignatureCheck.PropertyMatches(t, name, pt, setter))
                    sb.AppendLine($"{t?.FullName}.{name}:{pt.Name}{(setter ? "(set)" : "")}");
            }

            Method(typeof(Hero), "GetAttributeValue", typeof(int), typeof(CharacterAttribute));
            Method(typeof(Hero), "GetSkillValue", typeof(int), typeof(SkillObject));
            Method(typeof(Hero), "GetPerkValue", typeof(bool), typeof(PerkObject));
            Method(typeof(Hero), "ClearPerks", typeof(void));
            Method(typeof(Hero), "ChangeHeroGold", typeof(void), typeof(int));

            Method(typeof(HeroDeveloper), "GetFocus", typeof(int), typeof(SkillObject));
            Method(typeof(HeroDeveloper), "AddAttribute", typeof(void), typeof(CharacterAttribute), typeof(int), typeof(bool));
            Method(typeof(HeroDeveloper), "RemoveAttribute", typeof(void), typeof(CharacterAttribute), typeof(int));
            Method(typeof(HeroDeveloper), "AddFocus", typeof(void), typeof(SkillObject), typeof(int), typeof(bool));
            Method(typeof(HeroDeveloper), "RemoveFocus", typeof(void), typeof(SkillObject), typeof(int));
            Method(typeof(HeroDeveloper), "AddPerk", typeof(void), typeof(PerkObject));
            Method(typeof(HeroDeveloper), "SetInitialSkillLevel", typeof(void), typeof(SkillObject), typeof(int));
            Prop(typeof(HeroDeveloper), "UnspentAttributePoints", typeof(int), setter: true);
            Prop(typeof(HeroDeveloper), "UnspentFocusPoints", typeof(int), setter: true);

            Reason = sb.ToString();
            IsCompatible = Reason.Length == 0;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build StatRespec/StatRespec.csproj -c Release`
Expected: `Build succeeded`.

- [ ] **Step 3: Wire into `SubModule.cs` (run on load, log result)**

Modify `StatRespec/SubModule.cs` — replace the body with:

```csharp
using StatRespec.Compat;
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
```

- [ ] **Step 4: Manual check**

Start the game. Expected on the current v1.3.15: green "StatRespec loaded" (compatibility passes).

- [ ] **Step 5: Commit**

```bash
git add StatRespec/Compat/CompatibilityCheck.cs StatRespec/SubModule.cs
git commit -m "feat(StatRespec): load-time signature compatibility check"
```

---

## Task 5: HeroSnapshot (capture + restore)

**Files:**
- Create: `StatRespec/HeroSnapshot.cs`

Restore only needs what the flow touches BEFORE apply: attributes, focus, unspent points, perks. Skills/level/TotalXp are not modified before apply, so they are not snapshotted.

- [ ] **Step 1: Implement `StatRespec/HeroSnapshot.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace StatRespec
{
    /// <summary>Pre-respec state of one hero, enough to fully roll back on cancel.</summary>
    public sealed class HeroSnapshot
    {
        private readonly Hero _hero;
        private readonly Dictionary<CharacterAttribute, int> _attributes = new Dictionary<CharacterAttribute, int>();
        private readonly Dictionary<SkillObject, int> _focus = new Dictionary<SkillObject, int>();
        private readonly List<PerkObject> _perks = new List<PerkObject>();
        private readonly int _unspentAttr;
        private readonly int _unspentFocus;

        private HeroSnapshot(Hero hero)
        {
            _hero = hero;
            var dev = hero.HeroDeveloper;
            foreach (var a in Attributes.All) _attributes[a] = hero.GetAttributeValue(a);
            foreach (var s in Skills.All) _focus[s] = dev.GetFocus(s);
            foreach (var p in PerkObject.All) if (hero.GetPerkValue(p)) _perks.Add(p);
            _unspentAttr = dev.UnspentAttributePoints;
            _unspentFocus = dev.UnspentFocusPoints;
        }

        public static HeroSnapshot Capture(Hero hero) => new HeroSnapshot(hero);

        public void Restore()
        {
            var dev = _hero.HeroDeveloper;

            foreach (var kv in _attributes)
            {
                int cur = _hero.GetAttributeValue(kv.Key);
                if (cur > kv.Value) dev.RemoveAttribute(kv.Key, cur - kv.Value);
                else if (cur < kv.Value) dev.AddAttribute(kv.Key, kv.Value - cur, false);
            }

            foreach (var kv in _focus)
            {
                int cur = dev.GetFocus(kv.Key);
                if (cur > 0) dev.RemoveFocus(kv.Key, cur);
                if (kv.Value > 0) dev.AddFocus(kv.Key, kv.Value, false);
            }

            _hero.ClearPerks();
            foreach (var p in _perks) dev.AddPerk(p);

            dev.UnspentAttributePoints = _unspentAttr;
            dev.UnspentFocusPoints = _unspentFocus;
        }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build StatRespec/StatRespec.csproj -c Release`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add StatRespec/HeroSnapshot.cs
git commit -m "feat(StatRespec): hero snapshot/restore for cancel"
```

---

## Task 6: Behavior skeleton — menu option, sub-menu, gold gate, pickers

**Files:**
- Create: `StatRespec/Behaviors/StatRespecBehavior.cs`
- Modify: `StatRespec/SubModule.cs` (register the behavior)

- [ ] **Step 1: Create `StatRespec/Behaviors/StatRespecBehavior.cs` (menu + pickers; flow stub)**

```csharp
using System.Collections.Generic;
using System.Linq;
using StatRespec.Compat;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.ViewModelCollection;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.Information;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace StatRespec.Behaviors
{
    public class StatRespecBehavior : CampaignBehaviorBase
    {
        public const int RespecCost = 10000;

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
                null, GameOverlays.MenuOverlayType.SettlementWithBoth, GameMenu.MenuFlags.None, null);

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
                new ImageIdentifier(CampaignUIHelper.GetCharacterCode(h.CharacterObject)),
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
            StartRespec(hero); // implemented in Task 7
        }

        // Filled in by Tasks 7-8:
        private void StartRespec(Hero hero) { }
    }
}
```

- [ ] **Step 2: Register the behavior in `SubModule.cs`**

Add to `StatRespec/SubModule.cs` — add `OnGameStart` override (keep existing members):

```csharp
        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (CompatibilityCheck.IsCompatible
                && game.GameType is Campaign
                && gameStarterObject is CampaignGameStarter cgs)
            {
                cgs.AddBehavior(new StatRespec.Behaviors.StatRespecBehavior());
            }
        }
```

- [ ] **Step 3: Build**

Run: `dotnet build StatRespec/StatRespec.csproj -c Release`
Expected: `Build succeeded`.

- [ ] **Step 4: Manual check**

Enter a town → "Go to the tavern district". Expected: a new option "Redistribute attributes & focus (10,000 denars)" (greyed with tooltip if gold < 10 000). Click it → sub-menu with the two pick options + Back. Each pick opens a portrait list; selecting closes the inquiry (no effect yet). Back returns to the tavern district.

- [ ] **Step 5: Commit**

```bash
git add StatRespec/Behaviors/StatRespecBehavior.cs StatRespec/SubModule.cs
git commit -m "feat(StatRespec): tavern-district menu, sub-menu, gold gate, hero pickers"
```

---

## Task 7: Reset + open native screen + detect close

**Files:**
- Modify: `StatRespec/Behaviors/StatRespecBehavior.cs`
- Modify: `StatRespec/SubModule.cs` (poll screen close in `OnApplicationTick`)

- [ ] **Step 1: Implement reset + open screen + close-detection in `StatRespecBehavior.cs`**

Add fields and replace the `StartRespec` stub:

```csharp
        private HeroSnapshot _snapshot;
        private Hero _activeHero;
        private bool _awaitingScreen;
        private bool _screenSeen;

        public static StatRespecBehavior Instance { get; private set; }

        public StatRespecBehavior() { Instance = this; }

        private void StartRespec(Hero hero)
        {
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
                FileLog.Log("[StatRespec] StartRespec failed: " + ex);
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
            catch (System.Exception ex) { FileLog.Log("[StatRespec] Restore failed: " + ex); }
            _snapshot = null;
            _activeHero = null;
        }

        /// Called every frame by SubModule.OnApplicationTick.
        public void PollScreenClose()
        {
            if (!_awaitingScreen) return;
            var gsm = Game.Current?.GameStateManager;
            if (gsm == null) return;
            bool isDeveloper = gsm.ActiveState is CharacterDeveloperState;
            if (isDeveloper) { _screenSeen = true; return; }
            if (_screenSeen)
            {
                _awaitingScreen = false;
                OnScreenClosed(); // implemented in Task 8
            }
        }

        // Filled in by Task 8:
        private void OnScreenClosed() { }
```

Add the using at the top of the file:

```csharp
using TaleWorlds.Core; // already present; ensure CharacterDeveloperState namespace:
using TaleWorlds.CampaignSystem; // CharacterDeveloperState lives here
```

- [ ] **Step 2: Poll from `SubModule.OnApplicationTick`**

Add to `StatRespec/SubModule.cs`:

```csharp
        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            StatRespec.Behaviors.StatRespecBehavior.Instance?.PollScreenClose();
        }
```

- [ ] **Step 3: Build**

Run: `dotnet build StatRespec/StatRespec.csproj -c Release`
Expected: `Build succeeded`.

- [ ] **Step 4: Manual check**

Pick a hero → the native Character Developer screen opens on that hero, attributes show 2 each, focus 0, and the unspent-points pool matches `Σ(old attrs)+oldUnspent − attrCount*2` / `Σ(old focus)+oldUnspentFocus`. Redistribute, close the screen. (No trim/charge yet — that is Task 8; the screen close is silently detected.)

- [ ] **Step 5: Commit**

```bash
git add StatRespec/Behaviors/StatRespecBehavior.cs StatRespec/SubModule.cs
git commit -m "feat(StatRespec): reset hero, open native developer screen, detect close"
```

---

## Task 8: Trim + summary/confirm + apply (perks, gold) / cancel

**Files:**
- Modify: `StatRespec/Behaviors/StatRespecBehavior.cs`

Confirm popup is ALWAYS shown (it is the charge + perk-wipe + abort gate); when no skill is trimmed it says so. This refines the spec's "skip popup if nothing trims".

- [ ] **Step 1: Implement `OnScreenClosed`, summary, apply, cancel**

Replace the `OnScreenClosed` stub and add helpers:

```csharp
        private void OnScreenClosed()
        {
            var hero = _activeHero;
            if (hero == null) { _snapshot = null; return; }

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
                FileLog.Log("[StatRespec] Apply failed: " + ex);
                Abort();
                return;
            }
            _snapshot = null;
            _activeHero = null;
            InformationManager.DisplayMessage(new InformationMessage(
                $"{hero.Name} retrained.", Color.FromUint(0xFF00FF66u)));
        }
```

Ensure these usings are present at the top of the file:

```csharp
using TaleWorlds.CampaignSystem; // CampaignOptions, CharacterDeveloperState, Campaign
```

- [ ] **Step 2: Build**

Run: `dotnet build StatRespec/StatRespec.csproj -c Release`
Expected: `Build succeeded`.

- [ ] **Step 3: Manual check — full happy path**

Town → tavern district → respec option → pick a companion → screen opens reset → put e.g. 10 in a magic attribute + focus in 2-3 skills → close. Confirm popup lists the skills that exceed the new build (or "No skills will be reduced"), the perk-reset note, the 10 000 cost, and (for a companion with leftover points and auto-allocate ON) the warning. Confirm → skills trimmed, perks cleared, 10 000 deducted, "retrained" message. Verify on the character screen.

- [ ] **Step 4: Manual check — cancel path**

Repeat, but on the confirm popup press Cancel. Expected: attributes, focus, unspent points and perks return to exactly the pre-respec state; gold unchanged.

- [ ] **Step 5: Commit**

```bash
git add StatRespec/Behaviors/StatRespecBehavior.cs
git commit -m "feat(StatRespec): trim over-cap skills, confirm summary, apply/cancel, payment"
```

---

## Task 9: README + load-order note

**Files:**
- Create: `StatRespec/README.md`
- Modify: `README.md` (repo root — add StatRespec section)

- [ ] **Step 1: Write `StatRespec/README.md`** — what it does, the tavern-district entry, the 10 000 cost, "respec only the selected hero" limitation, no TOR/Harmony dependency, build command (`dotnet build StatRespec/StatRespec.csproj -c Release`), deploy path.

- [ ] **Step 2: Add a `StatRespec/` bullet to the repo root `README.md`** under "Содержимое репо", and add `StatRespec` to the load-order list (after StoryMode; TOR not required).

- [ ] **Step 3: Commit**

```bash
git add StatRespec/README.md README.md
git commit -m "docs(StatRespec): module README + load order"
```

---

## Self-Review

**Spec coverage:**
- Standalone module, no TOR/Harmony/patch → Task 1 csproj/SubModule.xml (no TOR_Core/0Harmony refs).
- Entry in `town_backstreet`, gold-gated, sub-menu (party / clan-out-of-party / back) → Task 6.
- Reset attrs→2, focus→0, pool from actual points (Σ − floor, dynamic attr count) → Task 2 (math) + Task 7 (apply to hero).
- Redistribute on native screen → Task 7.
- Trim via rate-based ceiling through the active model → Task 2 (math) + Task 8 (model wired in).
- Preserve level (no TotalXp/level writes) → Tasks 7-8 never touch level/TotalXp.
- Full perk reset on apply → Task 8 (`ClearPerks`).
- Auto-allocate warning, no suppression → Task 8 (`CampaignOptions.AutoAllocateClanMemberPerks`, read-only).
- 10 000 charged only on apply → Task 8 (`ChangeHeroGold` in `Apply`).
- Snapshot/restore on cancel → Task 5 + Task 8 cancel path.
- No reflection calls; load-time signature check → Task 3 (pure) + Task 4 (game members) + menu gate in Task 6.
- "respec only the selected hero" known limitation → inherent (only `_activeHero` is snapshotted/trimmed); documented in Task 9 README.
- Tests → Tasks 2-3 (RespecMath, SignatureCheck unit tests).

**Placeholder scan:** none — the only intentionally-empty stubs (`StartRespec`, `OnScreenClosed`) are explicitly filled in the next task and labeled as such.

**Type consistency:** `RespecMath.{UnspentAttributesAfterReset, UnspentFocusAfterReset, MaxReachableSkill, TrimTarget, AttributeFloor}`, `SignatureCheck.{MethodMatches, PropertyMatches}`, `CompatibilityCheck.{Run, IsCompatible, Reason}`, `HeroSnapshot.{Capture, Restore}`, `StatRespecBehavior.{Instance, PollScreenClose, RespecCost}` — used consistently across tasks.

**To verify during execution (API specifics that drift across game versions — the signature check guards them, but confirm at first build):**
- `GameOverlays.MenuOverlayType.SettlementWithBoth` namespace/name and the exact `AddGameMenu`/`AddGameMenuOption` overloads.
- `CampaignUIHelper.GetCharacterCode` + `ImageIdentifier(string)` / `InquiryElement` ctor.
- `CharacterDeveloperState` lives in `TaleWorlds.CampaignSystem`; `GameStateManager.ActiveState` for close-detection.
- `hero.CharacterAttributes` is the `IReadOnlyPropertyOwner<CharacterAttribute>` accepted by `CalculateLearningRate`.
- `Attributes.All` / `Skills.All` / `PerkObject.All` enumerations.
