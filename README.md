# TOR QoLs — Quality-of-Life Mod for The Old Realms

Сборник QoL-фиксов и фичей для мода **The Old Realms** (TOR) на Mount & Blade II: Bannerlord v1.3.15.

## Что внутри

### Harmony патчи

| Файл | Что патчит | Что делает |
|---|---|---|
| `Patches/CivilianWindsWeightPatch.cs` | `TORAbilityModel.GetWindsRechargeRate` (Transpiler) | Штраф к регену маны от веса брони считается с **CivilianEquipment**, не с BattleEquipment. Маг может носить тяжёлую боевую броню без потери регена, держа лёгкую гражданку. |
| `Patches/FixMagicItemHighlightPatch.cs` | `ExtendedItemObjectManager.HasMagicItemId` (Prefix) | Чинит TOR-баг: модифицированные magic-предметы (Сбалансированное / Заточенное) теряли фиолетовую подсветку в инвентаре. |
| `Patches/SkillBasedCanUsePatch.cs` | `SPInventoryVM.RefreshCharacterCanUseItem` (Postfix, Priority.Last) | Возвращает красную подсветку для предметов с невзятым skill-difficulty (TOR-патч перетирал нативный skill-чек race-проверкой). |
| `Patches/SellAllPostfixPatch.cs` | `SPInventoryVM.ExecuteSellAllItems` (Postfix, Priority.Last) | После нажатия "Sell All" в trade screen: Wave 1 — продаёт всех lame warhorses/mules; Wave 2 — продаёт самых дорогих unlocked до target_count. Уважает native locks. |

### Campaign Behavior

| Файл | Что делает |
|---|---|
| `Behaviors/SmartFoodTraderBehavior.cs` | При входе MainParty в Town/Village: <br>• Food: докупка до 10 дней / продажа излишка свыше 15 дней (минимум 1 каждого типа для морал-бафа разнообразия) <br>• Livestock: per-unit выбор butcher vs sell по выгоде (использует `HorseComponent.MeatCount/HideCount`) <br>• Warhorses: продажа lame/sick если > `unmounted×1.15` <br>• Mules: продажа lame/sick если > `totalMen×0.45` <br>• Красное warning если warhorses < unmounted_infantry <br>• Итоговое сообщение в чат |

## Установка

Mod-папка: `<Bannerlord install>/Modules/TOR_QoLs/`

Зависимости (load order должен быть выше нашего мода):
- `Native`
- `Bannerlord.Harmony` (workshop)
- `TOR_Core` (The Old Realms)

В лаунчере порядок:
```
... Harmony → Native → ... → TOR_Core → TOR_QoLs
```

## Сборка

Требования: .NET SDK 8+, цель `net48`.

```bash
dotnet build -c Release
```

Build кладёт DLL прямо в `<Bannerlord install>/Modules/TOR_QoLs/bin/Win64_Shipping_Client/` через переменную `$(GameDir)` в csproj.

Если установка Bannerlord не по умолчанию — поправь `<GameDir>` в `TOR_QoLs.csproj`:
```xml
<GameDir>D:\SteamLibrary\steamapps\common\Mount &amp; Blade II Bannerlord</GameDir>
```

## Версия

Auto-stamped из UTC build time: `1.0.YYMMDD.HHMM` (через csproj). На главном меню видно зелёным `TOR QoLs v<version> loaded`.

## Планы

См. `SPEC_SmartFoodTrader.md` — спецификация дальнейших доработок.

## Лицензия

TBD (выберешь свою).
