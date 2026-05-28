# Mount & Blade II: Bannerlord — The Old Realms Mods

Сборник модов для **Mount & Blade II: Bannerlord v1.3.15** в связке с **The Old Realms (TOR)** total conversion.

## Содержимое репо

### [`TOR_QoLs/`](TOR_QoLs/) — Quality-of-Life patches + Smart Food Trader

Свой мод, набор Harmony патчей и Campaign Behaviors:
- **CivilianWindsWeightPatch** — weight-малус регена маны от civilian-сета (можно носить тяжёлую боевую броню без потери магии)
- **FixMagicItemHighlightPatch** — чинит фиолетовую подсветку для модифицированных magic-предметов
- **SkillBasedCanUsePatch** — красная подсветка для предметов с невзятым skill-difficulty
- **SellAllPostfixPatch** — после "Sell All" в trade: продаёт lame лошадей и излишек дорогих unlocked
- **SmartFoodTraderBehavior** — при входе в settlement: food upkeep + livestock butcher/sell + horse cleanup

См. [TOR_QoLs/SPEC_SmartFoodTrader.md](TOR_QoLs/SPEC_SmartFoodTrader.md) для деталей.

### [`AutoEquipCompanions/`](AutoEquipCompanions/) — кастомный форк AEC

Форк [mwsaari/AutoEquipCompanions](https://github.com/mwsaari/AutoEquipCompanions) с TOR-специфичными правками:
- **WeaponClass matching** — same-type фильтр теперь по `WeaponClass` (sword/mace/axe/bow/etc.) а не по общему `ItemType`. Топоры остаются топорами, посохи — посохами.
- **Magic amplifier scoring** — `GetScore` для оружия учитывает суммарный `AmplifierTuple.DamageAmplifier` со всех трейтов предмета. Magic-amped items приоритезируются.
- **Torch stat-stick differentiation** — `item_usage="torch"` (Death Wizard Staff и т.п.) не свапается с реальным оружием того же class.
- **EHP-based armor scoring** — `DefaultArmorTemplate.GetScore` считает effective HP (база HP + HealthMax трейты × armor multiplier × phys resist multiplier). Магические резы намеренно игнорируются.
- **MainHero-first ordering** — главгерой одевается первым из общего пула.
- **TOR_Core dependency** — добавлен reference для доступа к `GetTraits()` extension и `AmplifierTuple`.

Билд через свой `AutoEquipCompanions.Build.csproj` (SDK-style, под Windows; оригинальный csproj был с Linux-путями).

## Сборка

Требования:
- .NET SDK 8+
- Bannerlord установлен где-то (по умолчанию `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord`)

### TOR_QoLs

```bash
cd TOR_QoLs
dotnet build -c Release
```

DLL деплоится в `<game>/Modules/TOR_QoLs/`.

### AutoEquipCompanions

```bash
cd AutoEquipCompanions
dotnet build AutoEquipCompanions.Build.csproj -c Release
```

DLL деплоится в `<game>/Modules/AutoEquipCompanions/`.

Если установка Bannerlord не по умолчанию — поправь `<GameDir>` в соответствующем csproj.

## Установка в игре

Load order в лаунчере (после native):
```
Bannerlord.Harmony
Native
SandBoxCore
Sandbox
StoryMode
CustomBattle
TOR_Armory
TOR_Environment
TOR_Core
AutoEquipCompanions
TOR_QoLs
```

## Лицензия

- `TOR_QoLs/` — TBD
- `AutoEquipCompanions/` — наследует лицензию upstream (см. `AutoEquipCompanions/README.md`)
