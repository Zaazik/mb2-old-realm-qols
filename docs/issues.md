# Жалобы пользователя / открытые вопросы

Список того что не работает или вызывает вопросы. Со ссылками на код.

---

## Активные

### #1 Sell All продаёт ВСЕХ лошадей
**Жалоба:** "при кнопке продать все, все кони продаются"

**Корневая причина:**
Vanilla `SPInventoryVM.TransferAll(isBuy: false)` итерирует `RightItemListVM` и продаёт ВСЁ что не залочено (включая всех лошадей). Мой Postfix запускается после, когда инвентарь уже пустой по лошадям.

**Текущий статус:** не пофикшено.

**Обсуждаемые подходы:**
- (A) Postfix — **не работает**, ничего не остаётся к моменту запуска
- (B) Prefix + native lock самых дешёвых target_count — **отвергнуто пользователем**, ломает если user хочет продать ниже target
- (C) Полная замена через Prefix return false — сложно, надо реплицировать vanilla
- (D) Hook другой функции — надо искать

**Решение:** не определено, ждёт обсуждения.

---

### #2 Livestock butchering не срабатывает
**Жалоба:** "не работает забив скота"

**Возможная причина:**
- `butcherValue > sellPrice` редко true в vanilla economy (livestock цена выше суммы meat+hide)
- Нет food-deficit бонуса в формуле (mod не учитывает что если food < target — butcher был бы выгоднее)

**Текущий статус:** mod есть, но не триггерится потому что условие почти всегда false.

**Решение запланированное:**
Добавить food-deficit бонус: если food < required, prefer butcher даже если sellPrice > butcherValue (потому что сэкономили на покупке мяса по buy-price который выше sell).

---

### #3 Sell food / sell horse Wave 1 не работают на входе
**Жалоба:** "работает пока только авто покупка еды"

**Возможная причина:**
Условия для других веток не выполняются в текущей сессии:
- Food sell: `currentFood > buffer (15 дней) && foodTypes ≥ 2` — может не быть избытка или одни тип
- Horse Wave 1: `warhorses > target && хотя бы один lame_horse` — может не быть lame
- Mule Wave 1: то же что и horse

**Текущий статус:** возможно код работает корректно, просто условия не срабатывают.

**Решение запланированное:**
Добавить диагностические FileLog.Log сообщения которые показывают для каждой ветки: "Triggered: yes/no, reason: ..."

Тогда увидим точно почему ничего не делается.

---

### #4 Возможно цена-фильтры (0.7×value, 1.5×value) ломают всё
**Жалоба:** "убери на время тестов треш холды на цену"

**Возможная причина:**
TOR-мод имеет `TORTradeItemPriceFactorModel` который понижает sell-цены equipment'а на 50% при 0 Trade skill, восстанавливая постепенно до vanilla при Trade 300. Наш фильтр `sellPrice ≥ 0.7 × value` может отсекать всё подряд при низком Trade skill, потому что реальная цена занижена.

Файл: `TOR_Core/Models/TORTradeItemPriceFactorModel.cs`
Константы:
- `EQUIPMENT_SELL_PRICE_BASE_MULTIPLIER = 0.5f`
- `EQUIPMENT_TRADE_SKILL_BONUS_PER_LEVEL = 0.0073f`

Применяется к: weapons, armor, horse harness (НЕ к horses-как-items, НЕ к food, НЕ к livestock как trade goods).

**Текущий статус:** для тестов отключаем (или ставим в no-op значения) `BuyPriceCap` и `SellPriceFloor`.

---

### #5 IsLocked-подход для Sell All ломается на over-cap
**Жалоба:** "если у тебя будет овер кап лошадей ты их тогда не продашь"

**Уточнение:** если у user 200 лошадей при target 100, и мы лочим cheapest 100 → vanilla продаёт верхние 100. Это окей.

НО: если user сам захочет продать **ниже** target (например, специально дампит лошадей для денег) → наш авто-лок мешает.

**Решение:** не определено.

---

## Решённые

### #R1 Skill-difficulty подсветка
TOR-патч `SPInventoryVM.RefreshCharacterCanUseItem` перетирал нативный skill-check race-проверкой. Наш `SkillBasedCanUsePatch.cs` с `[Priority.Last]` AND-ит результат с native skill-difficulty чеком.

### #R2 Magic item highlight bug
`ExtendedItemObjectManager.HasMagicItemId` имел баг с `entry.Key.EndsWith(modifier.StringId)` — модифицированные magic-предметы не подсвечивались фиолетовым. `FixMagicItemHighlightPatch.cs` чинит.

### #R3 Civilian weight для регена маны
`CivilianWindsWeightPatch.cs` — Transpiler swap'нул `baseCharacter.Equipment` → `baseCharacter.HeroObject.CivilianEquipment` в `TORAbilityModel.GetWindsRechargeRate`. Маг может носить тяжёлую боевую броню без потери регена.

### #R4 AEC: same-type filter по WeaponClass вместо ItemType
Иначе AEC меняет посох на меч (оба OneHandedWeapon). Теперь по WeaponClass — посохи остаются посохами.

### #R5 AEC: magic amplifier scoring
В `SameTypeWeaponTemplate.GetScore` добавлен `ampSum × 100000` чтобы amp-предметы доминировали над non-amp при равных value.

### #R6 AEC: torch stat-stick differentiation
`item_usage="torch"` (Death Wizard Staff и т.п.) теперь не свапается с реальным оружием одной WeaponClass.

### #R7 AEC: EHP-based armor scoring
`DefaultArmorTemplate.GetScore` считает effective HP (armor + HealthMax × armor_multiplier × phys_resist_multiplier). Магические резы намеренно игнорируются.

### #R8 AEC: MainHero-first
`AutoEquipModel.AutoEquipCompanions` сортирует по `OrderByDescending(h => h == Hero.MainHero)` — главгерой одевается первым из общего пула.
