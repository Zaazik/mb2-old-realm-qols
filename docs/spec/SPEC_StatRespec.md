# SPEC: StatRespec — сброс и осознанное перераспределение характеристик

Отдельный самостоятельный мод для Mount & Blade II: Bannerlord v1.3.15.
Работает и в ваниле, и в The Old Realms (TOR) — математика берётся из активной игровой
модели развития, без зависимости от TOR.

## Мотивация

- Игра авто-раскидывает атрибуты/focus за игрока и спутников
  (`CharacterDevelopmentCampaignBehavior.DailyTickHero → HeroDeveloper.DevelopCharacterStats`),
  раскладку выбирает алгоритм, а не игрок.
- Хочется самому решать, во что вкладывать очки конкретного героя, и иметь возможность
  переиграть уже сделанную (авто)раскладку.
- Нужен инструмент: сбросить героя к базе, осознанно перераспределить атрибуты/focus, а скилы,
  не влезающие в новую раскладку, срезать до достижимого потолка — без читов и без слепого нерфа.

## Модуль (важно: самостоятельный, без зависимостей)

- Новый модуль `StatRespec` (папка-сосед к `TOR_QoLs/`), свой `SubModule.xml` + `StatRespec.csproj`.
- **Без `TOR_Core`. Без Harmony. Без единого патча.** Только ванильные TaleWorlds-DLL.
- Никакого IL-патчинга, никаких приватных полей через reflection при работе (см. Безопасность).
- Load order: после `SandBox` (чтобы существовало меню `town_backstreet`). TOR не требуется.

## Точка входа

Город → «Отправиться в квартал таверн» → меню `town_backstreet` (штатные пункты: Посетить таверну
`town_tavern`, Нанять юнитов `recruit_mercenaries`, Вернуться в центр города `town_backstreet_back`).

- `AddGameMenuOption("town_backstreet", "sr_respec_entry", "Redistribute attributes & focus (10,000 denars)", …, index: 1)`.
  - Условие: видно в городе; если `Hero.MainHero.Gold < 10000` →
    `args.IsEnabled = false; args.Tooltip = "Requires 10,000 denars"`, `return true` (серый).
  - Consequence: `GameMenu.SwitchToMenu("sr_respec_menu")`.
- Наше под-меню `sr_respec_menu` (`AddGameMenu`), три пункта:
  1. **«Hero in my party»** (`sr_pick_party`) → picker по героям `MobileParty.MainParty` (ГГ + спутники в отряде).
  2. **«Clan companion (not in party)»** (`sr_pick_clan`) → picker по героям/companions `Clan.PlayerClan`, которых нет в главном отряде.
  3. **«Back»** (`sr_back`, isLeave) → `SwitchToMenu("town_backstreet")`.
- Дети и disabled-герои в списках не показываются.

## Поток (по шагам)

1. Пункт меню → под-меню → ветка (отряд / клан).
2. Picker: `MBInformationManager.ShowMultiSelectionInquiry` (single-select, портреты + имена).
   Cancel → возврат в под-меню, ничего не произошло.
3. Выбран герой H. **Снимок** состояния H: все атрибуты, focus по всем скилам, skill-XP по всем
   скилам, набор перков, `Level`, `TotalXp`, `UnspentAttributePoints`, `UnspentFocusPoints`.
4. **Сброс H:** атрибуты → 2 (через `RemoveAttribute`/`AddAttribute`); focus → 0 (`RemoveFocus`);
   `UnspentAttributePoints` / `UnspentFocusPoints` = пул (см. Математику).
   `Level`/`TotalXp` НЕ трогаем. Перки на этом шаге НЕ трогаем (снимутся на применении).
5. Открываем нативный экран развития на H: `new CharacterDeveloperState(H)` →
   `Game.Current.GameStateManager.PushState(...)`. Игрок раскидывает атрибуты/focus.
6. Игрок закрывает экран → читаем финальные атрибуты/focus.
7. Считаем обрезку: для каждого скила потолок = достижимый максимум при финальных атр/focus
   (рейтовая формула). Если текущий скил > потолка — будет срезан.
8. **Сводка-попап** перед применением:
   - список ВСЕХ срезаемых скилов (`старое → новое`); если резать нечего — строку не показываем;
   - предупреждение про авто-распределение, если применимо (см. ниже);
   - кнопки Подтвердить / Отмена.
   - Отмена → полный откат по снимку (атрибуты, focus, skill-XP, перки, очки), 0 золота, выход.
9. **Применение:** срезаем скилы (`SetInitialSkillLevel`); сбрасываем все перки (`Hero.ClearPerks()`);
   списываем 10 000 (`Hero.MainHero.ChangeHeroGold(-10000)`). `Level`/`TotalXp` НЕ пересчитываем.
10. Перки игрок выбирает заново сам через обычный экран персонажа (доступны по финальным скилам).

## Математика

Обрезка скилов считается через активную модель `Campaign.Current.Models.CharacterDevelopmentModel`
(в TOR — `TORCharacterDevelopmentModel`, в ваниле — `DefaultCharacterDevelopmentModel`). Базовый
интерфейс полиморфен → формула обучения подставляется сама, без ссылки на TOR. Пул очков считаем
напрямую от того, что у героя есть (ниже), модель для пула не нужна.

### Floor + пул (считаем от фактических очков героя)

Не выводим из формулы уровня — берём то, что у героя реально есть, и вычитаем floor:

- **Атрибуты:** `total = Σ(текущие значения атрибутов) + UnspentAttributePoints`.
  Floor занимает `attribute_count × 2`, где `attribute_count` читаем динамически (6 в ваниле, 7 в TOR).
  После сброса: каждый атрибут = 2; `UnspentAttributePoints = total − attribute_count·2` (clamp ≥ 0).
  Пример: все 6 атрибутов по 5 + 1 нераспределённый = 31; floor 6·2 = 12; нераспределённых = 31 − 12 = 19.
- **Focus:** `total = Σ(текущий focus по всем скилам) + UnspentFocusPoints`; floor focus = 0.
  После сброса: весь focus = 0; `UnspentFocusPoints = total`.
- Итог: ровно те очки, что у героя есть сейчас (вложенные + нераспределённые), минус floor. Ничего не
  дарим, уровень не трогаем.

### Обрезка скила (рейтовый потолок)

- Потолок скила = минимальное `skill`, при котором
  `CharacterDevelopmentModel.CalculateLearningRate(финальные_атрибуты, focus, skill, skillObject).ResultNumber ≤ 0`.
- Считаем **через активную модель**, а не хардкодом → учитываются перки/TOR-модификаторы кривой.
  База без модификаторов ≈ `14·attr + 40·focus − 10` (проверено: attr 10 + focus 5 → 330).
- `newSkill = min(currentSkill, потолок)`; если меньше — `SetInitialSkillLevel(skill, newSkill)`
  (ставит skill-XP на пол нового уровня).
- `TotalXp`/`Level` после обрезки НЕ пересчитываем — модель «сохраняем уровень». Double-dip остаётся
  чисто теоретическим: стреляет только если игрок намеренно перекачивает срезанный скил
  (само-гринд, невыгодно). Уровень влияет на боевую мощь / квест-гейты / career-перки, поэтому
  не трогаем его осознанно.

### Перки

- На применении снимаются ВСЕ (`Hero.ClearPerks()`), даже валидные.
- Игрок выбирает перки заново сам, уже под финальные скилы.

## Авто-распределение — НЕ подавляем, только предупреждаем

`AutoAllocateClanMemberPerks` — это настройка кампании самого игрока, мод в неё не лезет (не патчит,
не меняет, не хранит). Единственное использование — **read-only для предупреждения**:

- Если выбранный герой — спутник (не главный; главного система авто-раскидки не трогает) И
  `CampaignOptions.AutoAllocateClanMemberPerks == true` И после раскидки остались нераспределённые
  очки → в сводке (шаг 8) показываем: «⚠ Auto-allocation is ON — the game will distribute your unspent
  points (N attribute / M focus) on its next daily tick».
- Дальше дело игрока: дораспределить, выключить опцию в настройках игры, или согласиться.

## Безопасность (не сломать игру / мод / сейв)

- **Патча нет** → нечем сломать рантайм. Меню — официальное API расширения (`AddGameMenuOption`/
  `AddGameMenu`), не Harmony.
- **Новых данных в сейве — НОЛЬ.** Всё пишется в штатные ванильные поля героя; сейв читается и без
  мода; `SaveableTypeDefiner` не нужен; миграций нет.
- **Reflection-вызовов нет.** Все операции — публичный API (см. Технические опоры). Члены, которым
  reflection был бы нужен (`TotalXp`-setter, `SetAttributeValueInternal`, `SetPerkValueInternal`),
  намеренно не используем.
- **Load-time сигнатур-чек.** На старте reflection'ом сверяем, что нужные публичные члены ещё
  существуют с ожидаемой сигнатурой (declaring type + параметры + возврат). Несовпадение/пропажа →
  пункт меню серый с тултипом «несовместимо с этой версией игры», лог с точным mismatch. Reflection
  тут — только для ПРОВЕРКИ, не для вызова.
- **Feature-boundary try/catch.** Весь флоу обёрнут; `MissingMethodException`/`TypeLoadException`/
  любая ошибка → лог + полный откат по снимку, золото не списано. Хот-код фейлит safe + лог;
  респек-флоу фейлит громко + откат.
- **Снимок/восстановление** гарантируют, что отмена/сбой не оставят выбранного героя в полусброшенном
  состоянии. Откат покрывает **только выбранного героя**.
- Сохранение посреди флоу невозможно (UI-стейты меню/инквайри/экрана не дают сейвить).

### Известное ограничение (принято)

Нативный экран развития — кланово-широкий, на нём есть переключалка героев. Сброс / обрезка / откат
относятся **только к выбранному герою**; если на этом экране переключиться на другого героя и поправить
его очки — это обычная игра, и нашим «Отмена» такие правки НЕ откатываются. Запрет переключения
потребовал бы кастомного UI, от которого отказались (Q3=C). В обычном сценарии (открылся на выбранном,
покрутил его, закрыл) ограничение не проявляется.

## Тесты

- Unit-тесты на чистую математику `RespecMath` (как `TOR_QoLs.Tests`): расчёт пула (`Σ очков − floor`,
  floor = `attribute_count·2`; clamp ≥ 0); поиск рейтового потолка (через мок-модель); решение об
  обрезке (`min(current, потолок)`); граничные случаи (floor 2 / focus 0 → потолок; attr 10 / focus 5
  → 330; скил ниже потолка не трогаем; пример 31 − 12 = 19).
- Сигнатур-чек проверяется юнитом: при подменённой/отсутствующей сигнатуре — фича помечается несовместимой.
- Сам флоу (меню, picker, нативный экран, оплата, откат) — ручной прогон в игре (UI/GameState не юнитятся).

## Локализация

Текст в игре — английский, через `TextObject`.

## Технические опоры (проверено декомпиляцией v1.3.15 / исходниками TOR_Core)

- Меню: вход `town_backstreet` из `town` (`SwitchToMenu("town_backstreet")`); три штатных пункта совпали с игрой.
- Формула: `CalculateLearningLimit = max(0,(avgAttr−1)·10)+focus·30`;
  `CalculateLearningRate = 1.25·(1+0.4·avgAttr+focus+overLimit)`, `overLimit = −1−0.1·(skill−limit)` при `skill>limit`, clamp ≥0.
- `TotalXp` — чистый накопитель (`GainRawXp`); из скилов пересчитывается только при инициализации героя.
- Уровень влияет: боевая мощь `Level/4+1`, мораль-резист, цена найма, бонус хилки, гейт квестов (≤15/20), TOR career-перки.
- Авто-распределение: `CharacterDevelopmentCampaignBehavior.DailyTickHero → DevelopCharacterStats`; флаг `CampaignOptions.AutoAllocateClanMemberPerks` (public static).
- Нативный экран: `CharacterDeveloperState(Hero)` (public ctor) + `GameStateManager.CreateState/PushState` (public); `CharacterDeveloperVM` берёт `Clan.PlayerClan.Heroes`+`Companions`, аллокация без `IsMainHero`-проверок → работает для спутников.
- Доступность API (всё public, прямой вызов): `Hero.GetAttributeValue/GetSkillValue/GetPerkValue/ClearPerks/ChangeHeroGold/Gold/Level`;
  `HeroDeveloper.GetFocus/GetSkillXp/AddAttribute/RemoveAttribute/RemoveFocus/SetInitialSkillLevel/AddPerk/UnspentAttributePoints(set)/UnspentFocusPoints(set)/TotalXp(get)`;
  `PerkObject.All`; модель `CalculateLearningRate`. Перечисление атрибутов — через `Attributes.All` (динамический count 6/7).

## Компоненты (файлы модуля `StatRespec`)

- `SubModule.xml` — манифест, load order после SandBox, без зависимости от TOR.
- `StatRespec.csproj` — SDK-style, net48, ссылки только на ванильные TaleWorlds-DLL.
- `SubModule.cs` — `MBSubModuleBase`; `OnGameStart` регистрирует `StatRespecBehavior`; на старте гоняет сигнатур-чек.
- `Behaviors/StatRespecBehavior.cs` — пункт меню + под-меню, picker'ы, открытие нативного экрана,
  хук закрытия, сводка-обрезка + предупреждение, оплата, снимок/откат.
- `Behaviors/RespecMath.cs` — пул (`Σ очков − floor`), поиск рейтового потолка через модель, решение об обрезке.
- `Compat/CompatibilityCheck.cs` — load-time сигнатур-чек публичных членов; флаг совместимости.
- `StatRespec.Tests/` — unit-тесты `RespecMath` и сигнатур-чека.
