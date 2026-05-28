# Mount & Blade II: Bannerlord + The Old Realms (TOR)

## Установка игры

- Steam-инсталл: `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\`
- Текущая ветка под TOR (как было выставлено пользователем 2026-05-28): **v1.3.15** через Steam → Properties → Betas. Не `Standard public` (1.4.5) — мод под него ещё не пересобран.

## Мод — The Old Realms (Warhammer Fantasy total conversion)

- Текущий релиз: **"War in the Mountains" v1.14** (патч 2026-05-15, база v1.12 от 2026-04-27)
- Требует Bannerlord **v1.3.15**
- Старая ветка "Season of Doom" v0.5.1 (ноябрь 2024, под 1.2.11) — не использовать
- Установка через Steam Workshop, не Nexus, не ModDB

### Workshop-коллекция (Subscribe to all одним кликом)
`https://steamcommunity.com/sharedfiles/filedetails/?id=2887131163`

### Workshop-IDs (4 мода)
| ID | Что |
|---|---|
| `2859188632` | Bannerlord.Harmony (зависимость, патчит .NET-рантайм) |
| `3025574678` | TOR_Core |
| `3025575223` | TOR_Armory |
| `3025579210` | TOR_Environment |

Папки качаются в `D:\SteamLibrary\steamapps\workshop\content\261550\<ID>\`.

## Правильный load order в лаунчере

Harmony **всегда** выше Native — иначе патчи не накатятся, мод упадёт:

```
1. Bannerlord.Harmony     ← первым, патчит рантайм
2. Native
3. SandboxCore
4. Sandbox
5. StoryMode
6. CustomBattle
7. TOR_Armory
8. TOR_Environment
9. TOR_Core               ← последним, тянет данные из Armory+Environment
```

Внутри TOR-блока порядок жёсткий: Armory → Environment → Core.

## GitHub (для контрибьюшна / изучения)

- Org: https://github.com/TheOldRealms
- Активный репо (под 1.3+): https://github.com/TheOldRealms/TOR_Core (default branch `development`, C#, GPL-3.0, последний коммит май 2026)
- Wiki: https://github.com/TheOldRealms/TOR_Core/wiki
- В репо только код — без ассетов (модели/текстуры), их размер не для git. Чтобы играть — Workshop.

## Шейдер-кэш (чистить при обновлении мода или необъяснимых артефактах)

Три места — все три:
1. `C:\ProgramData\Mount and Blade II Bannerlord\Shaders\` — удалить **папку целиком**
2. `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Shaders\D3D11\compressed_shaders_cache.sack` — удалить файл
3. `D:\SteamLibrary\steamapps\workshop\content\261550\{3025574678,3025575223,3025579210}\shaders\D3D11\compressed_shader.cache.sack` — удалить файл в каждой TOR-папке (Harmony пропустить)

Папки `shaders\D3D11\` создаются **только после первого запуска игры с модом**. До этого — сносить нечего, и это норма.

## Логи и краши

- Краш-логи: `C:\ProgramData\Mount and Blade II Bannerlord\crashes\<timestamp>\`
- Игровые логи: `C:\ProgramData\Mount and Blade II Bannerlord\logs\`
- **TOR-логи**: `D:\SteamLibrary\steamapps\workshop\content\261550\3025574678\Logs\YYYY\<Month>\<DD>\TOR_log*.txt` — тут самое полезное про что упало именно в TOR
- Save-файлы: `C:\Users\Admin\Documents\Mount and Blade II Bannerlord\Game Saves\`

При сломе мода — первым делом смотреть TOR_log и потом краш-лог, а не вслепую переустанавливать.

## Первый запуск с TOR

- Компилит шейдеры на главном меню — **до часа**, не закрывать
- После первого успешного запуска создаются `shaders\D3D11\` в каждой TOR-папке
- Между мажорными версиями TOR сейвы несовместимы — начинать новую кампанию

## Анти-паттерны (наблюдения по сессии 2026-05-28)

- Перед первым запуском игры — папки `C:\ProgramData\Mount and Blade II Bannerlord\` ещё не существует. Шаги чистки шейдер-кэша при свежей установке = no-op, не паниковать.
- Удаление workshop-папок (`Remove-Item -Recurse -Force`) может ловить access denied на DLL, если мод или TOR-логгер ещё держит handle. Решение: повторить команду, либо предварительно убить процессы Bannerlord/Steam.
- Bannerlord-папка в ProgramData называется `Mount and Blade II Bannerlord` (через `and`), в SteamLibrary — `Mount & Blade II Bannerlord` (через `&`). Не перепутать.
- SubModule.xml — canonical имя `SubModule.xml` (заглавная M). Некоторые репы (AutoEquipCompanions) кладут `Submodule.xml` маленькой m — переименовать или фиксить копирование при билде.
- В TOR-репе `D:\tmp\TOR_Core\bin\Win64_Shipping_Client\` уже лежат prebuilt `TOR_Core.dll` + `0Harmony.dll` — можно линковать наши моды на них без необходимости иметь установленный Workshop (полезно если Workshop ещё качается / снесли мод).

## Персонаж в текущем сейве (`save051.sav`, 2026-05-28 03:55)

- **Имя:** Joerg, уровень 1, день 0.17
- **Керер:** Necrarch (по `necrarch_body_armour_001` в visual)
- НЕ Necromancer → нет `ArkaynePassive1` → штраф от веса брони на регене работает
- Бэкап сейва: `save051.backup-2026-05-28.sav` рядом в Game Saves

## Cheat mode

- `cheat_mode = 1` в `C:\Users\Admin\Documents\Mount and Blade II Bannerlord\Configs\engine_config.txt` (выставлен 2026-05-28 для тестов)
- В игре консоль: `Alt + ~` (или `Alt + Ё` на ru-раскладке)
- Полезные команды:
  - `campaign.give_gold_to_main_hero 1000000`
  - `campaign.give_xp_to_main_hero 100000`
  - `campaign.add_focus_points_to_main_hero 50`
  - `campaign.add_renown_to_main_hero 1000`
  - `campaign.add_attribute_points_to_main_hero 20`
  - `help` / `help campaign`
- После тестов вернуть в `cheat_mode = 0`

## Custom-моды (свои билды)

### TOR_QoLs — наш Harmony-мод
- **Source / git repo:** `C:\Users\Admin\RiderProjects\mb2-old-realm-qols\` (origin `https://github.com/Zaazik/mb2-old-realm-qols`, branch `master`)
- **Deploy:** `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\TOR_QoLs\`
- **Target:** net48, SDK-style csproj
- **Build:** `dotnet build -c Release` или `build.bat`
- **Что делает:** Harmony-патчи на TOR_Core + native, плюс CampaignBehavior:
  1. `Patches/CivilianWindsWeightPatch.cs` — **Transpiler** на `TORAbilityModel.GetWindsRechargeRate`. Подменяет `baseCharacter.Equipment` → `baseCharacter.HeroObject.CivilianEquipment` в IL → штраф от веса брони считается с civilian-сета. Иммунитеты Arkayne/WardenOfTalsyn/companion-vampires сохраняются. Гоча: `Equipment` property живёт на `BasicCharacterObject` (родитель `CharacterObject`); для надёжности матча проверяем оба типа в PropertyGetter.
  2. `Patches/FixMagicItemHighlightPatch.cs` — **Prefix** на `ExtendedItemObjectManager.HasMagicItemId`. Чинит TOR-баг: модифицированные magic-предметы теряли фиолетовую подсветку из-за `entry.Key.EndsWith(modifier.StringId)`. Правильная проверка — есть ли у base entry `ItemTraits`. Reflection для доступа к private `_itemToInfoMap`.
  3. `Patches/SkillBasedCanUsePatch.cs` — **Postfix** с `[Priority.Last]` на `SPInventoryVM.RefreshCharacterCanUseItem`. AND-ит CanCharacterUseItem с native skill-difficulty check (TOR-патч перетирал его race-проверкой). Возвращает красную подсветку для предметов где skill < Difficulty.
  4. `Behaviors/SmartFoodTraderBehavior.cs` — **CampaignBehavior**, hook на `CampaignEvents.SettlementEntered`. При входе MainParty в Town/Village докупает food до 10 дней / продаёт излишек выше 15 дней. Использует `party.FoodChange`, `settlement.Town.GetItemPrice`, прямые ItemRoster + `GiveGoldAction.ApplyBetweenCharacters`. Сохраняет минимум 1 единицу каждого food-типа для морал-бафа. Цены: buy ≤ 1.5× value, sell ≥ 0.7× value.
- **Раньше был** `CivilianWindsRegenPatch.cs` (swap источника `WindsOfMagicRegen` трейтов на civilian) — удалён, потому что трейты на магический реген обычно лежат именно на боевой экипировке.

### Планируемые доработки (см. `docs/spec/SPEC_SmartFoodTrader.md` в этом же repo)

- **Livestock-логика:** sell-as-livestock vs butcher через `HorseComponent.MeatCount/HideCount`. Native Butcher API в Bannerlord НЕТ — реализуем вручную через `ItemRoster.AddToCounts(livestock, -1)` + добавление meat/hides items. Canonical item IDs `"meat"`, `"hides"`. Выбор max(benefit_sell, benefit_butcher) с учётом food-deficit.
- **Horse/Mule управление при входе settlement:** target_warhorses = `unmounted_infantry × 1.15`, target_mules = `totalMen × 0.45`, reserve_for_loot = `totalMen × 0.40`. Только продажа lame/sick (модификатор `lame_horse`), не докупаем. Красное сообщение если warhorses < unmounted_infantry. Wounded учитываются (formula based on `NumberOfMenWithoutHorse` который включает раненых).
- **Sell All Postfix patch:** на `SPInventoryVM.ExecuteTransferAllItem` (точное имя при имплементации) с `[Priority.Last]`. Wave 1 (lame/sick) + Wave 2 (самых дорогих unlocked) для warhorses и mules. Native locks через `CampaignUIHelper.GetItemLockStringID` + `IViewDataTracker.GetInventoryLocks()`.

### Полезные API/механики Bannerlord (по ходу разобрались)

- **Party speed (DefaultPartySpeedCalculatingModel):** `CavalryEffect=+0.3`, `MountedFootMenEffect=+0.15`, `HerdEffect=-0.4` (penalty константа). Herd-штраф = `-0.3 × ((herd_size - totalMen) / totalMen)`, capped at `-0.8`. Срабатывает только когда `herd_size > totalMen`.
- **Herd состав:** `NumberOfPackAnimals + NumberOfLivestockAnimals + max(0, NumberOfMounts - unmounted_infantry)`.
- **Item categories по HorseComponent:** `IsRideable`+`!IsPackAnimal` = боевой mount, `IsRideable`+`IsPackAnimal` = pack animal (мул, sumpter, camel-pack), `!IsRideable`+`!IsPackAnimal` = livestock (cow/sheep/hog).
- **TroopRoster:** `TotalManCount` включает раненых (`= TotalRegulars + TotalHeroes`). `TotalWounded` отдельный счётчик. `NumberOfMenWithoutHorse` тоже включает раненых пеших.
- **Lame status:** ItemModifier `lame_horse` в `Native/ModuleData/item_modifiers.xml`. Group `ItemModifierGroup.horse` применим ко всем HorseComponent-предметам (включая мулов). price_factor=0.1.
- **CampaignEvents для settlement:** `CampaignEvents.SettlementEntered` (НЕ `OnSettlementEnteredEvent`). Signature: `Action<MobileParty, Settlement, Hero>`.
- **GiveGoldAction.ApplyBetweenCharacters(from, to, amount)** — proper money transfer. `from`/`to` могут быть null (для market transactions).
- **Load order:** **после** TOR_Core, иначе патч не найдёт уже-загруженный класс.

### AutoEquipCompanions — кастомный форк mwsaari/AutoEquipCompanions
- **Source:** `C:\Users\Admin\RiderProjects\mb2-old-realm-qols\AutoEquipCompanions\` (subdir в общем repo с TOR_QoLs)
- **Build csproj:** `AutoEquipCompanions.Build.csproj` (свой SDK-style, оригинал был под Linux с путями `/mnt/ssd2/...`)
- **Deploy:** `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord\Modules\AutoEquipCompanions\`
- **Dependencies:** Newtonsoft.Json 13.0.4 (через NuGet PackageReference, копируется в bin при билде)
- **Harmony НЕ нужен** (мод его не использует)
- **Что делает:** авто-одевание героев в пати (включая main hero) на закрытии инвентаря или вручную через кнопку справа сверху. Шаблоны: Default, Infantry/Cavalry/Bow/Crossbow Captain, Horse Archer. Только BattleEquipment, civilian/stealth не трогает.
- **Source-карта (если будем форкать/редактить):**
  - `AutoEquipBehavior.cs` — campaign behavior, цепляет UI
  - `Model/AutoEquipModel.cs` — главная логика (строки 30-37 — фильтр героев включает MainHero)
  - `Model/Templates/Character/*` — пресеты ролей
  - `Model/Templates/{Armor,Weapon,Mount,Shield}/*` — slot-шаблоны
  - `Model/Saving/*` — JSON-сериализация per-character настроек
  - `ViewModel/AutoEquipOverlayVM*.cs` — v1/v2 UI overlay
  - `Tests/` — unit-тесты, в рантайме не нужны

### Build-окружение
- **.NET SDK:** 8.0 и 10.0 установлены (`dotnet --list-sdks`)
- **MSBuild standalone** не установлен — билдим через `dotnet build`
- **Target Framework:** **net48** для всех Bannerlord-модов (игра под .NET Framework 4.8). `net472` не подходит — Harmony/TOR_Core собраны под net48 и резолверу не нравится понижение.
- **Workshop DLL для линковки можно не ждать:** prebuilt TOR_Core.dll + 0Harmony.dll лежат в `D:\tmp\TOR_Core\bin\Win64_Shipping_Client\`.
