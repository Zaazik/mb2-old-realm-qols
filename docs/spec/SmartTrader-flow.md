# SmartTrader — полный флоу

Детальное описание что и когда должно происходить.

## Триггеры

### Триггер 1: вход MainParty в settlement (Town/Village)
Hook: `CampaignEvents.SettlementEntered`.

### Триггер 2: нажатие диагональной стрелки "Sell All" в trade screen
Hook: `SPInventoryVM.ExecuteSellAllItems` (Postfix или Prefix — обсуждаемо, см. issues).

---

## Триггер 1: входим в settlement

Полная последовательность шагов:

### Шаг 0. Pre-checks
- party == MainParty
- settlement IsTown ИЛИ IsVillage (оба валидны)
- Hero.MainHero не null

Если хоть один не выполнен — выходим тихо.

### Шаг 1. Считаем базовые числа

```
totalMen        = party.MemberRoster.TotalManCount       // включает раненых
unmountedInf    = party.Party.NumberOfMenWithoutHorse    // пешие, включая раненых пеших
warhorses       = party.ItemRoster.NumberOfMounts        // боевые лошади
mules           = party.ItemRoster.NumberOfPackAnimals   // мулы/sumpter/pack-camel
dailyFood       = abs(party.FoodChange)                  // дневной расход food
```

Cap'ы:
```
foodTargetDays   = 10
foodBufferDays   = 15
targetWarhorses  = unmountedInf + ceil(0.15 × totalMen)   // sweet spot + 15%
targetMules      = floor(0.45 × totalMen)
```

### Шаг 2. Livestock processing (per-unit decision)

Для каждой единицы livestock в `party.ItemRoster` (где `Item.HorseComponent.IsLiveStock == true`):

```
sellPrice    = town.GetItemPrice(livestock, isSelling=true)
meatPrice    = town.GetItemPrice(DefaultItems.Meat, isSelling=true)
hidePrice    = town.GetItemPrice(DefaultItems.Hides, isSelling=true)
meatCount    = item.HorseComponent.MeatCount
hideCount    = item.HorseComponent.HideCount

butcherValue = meatCount × meatPrice + hideCount × hidePrice

if food_сейчас_меньше_10_дней:
    // мяса не хватает — мясо ценнее, его не на что заменить иначе
    PREFER butcher (даже если sellPrice > butcherValue)
    // потому что иначе пришлось бы покупать food по buy-price (выше чем sell-price)
else:
    if butcherValue > sellPrice → butcher
    else → sell as livestock
```

Применение:
- **Butcher:** `party.ItemRoster.AddToCounts(livestock, -1)` + `+meatCount Meat` + `+hideCount Hides`
- **Sell:** `party.ItemRoster.AddToCounts(livestock, -1)` + `settlement.ItemRoster.AddToCounts(livestock, +1)` + `GiveGoldAction(merchant → MainHero, sellPrice)`

### Шаг 3. Food processing (после livestock — может уже хватить мяса)

Пересчитываем `currentFoodTotal` (теперь с булочным мясом):
```
currentFoodTotal = Σ amount where Item.IsFood == true
foodTypes        = Distinct items count
required         = dailyFood × 10
buffer           = dailyFood × 15
```

```
if currentFoodTotal < required:
    needed = required - currentFoodTotal
    BUY самой дешёвой food по списку settlement.ItemRoster (sort by price asc)
    покупаем до needed (или пока gold не кончится)
elif currentFoodTotal > buffer AND foodTypes ≥ 2:
    excess = currentFoodTotal - buffer
    SELL — берём из инвентаря наиболее обильный food first (sort by amount desc)
    оставляем минимум 1 единицу каждого типа (для морал-бафа разнообразия)
    продаём до excess units
else:
    silent
```

### Шаг 4. Warhorse processing — только lame

```
if warhorses > targetWarhorses:
    candidates = party.ItemRoster where (HorseComponent.IsMount && ItemModifier.StringId == "lame_horse")
    sellExcess = warhorses - targetWarhorses
    sell up to sellExcess from candidates
elif warhorses < unmountedInf:
    RED WARNING: "Не хватает {unmountedInf - warhorses} боевых лошадей"
else:
    silent
```

**ВАЖНО:** на входе в settlement продаются **ТОЛЬКО lame**. Здоровые лошади не трогаются никогда. Wave 2 (продажа здоровых дорогих) — только на Sell All.

### Шаг 5. Mule processing — только lame (без warning'а)

```
if mules > targetMules:
    candidates = party.ItemRoster where (HorseComponent.IsPackAnimal && lame)
    sellExcess = mules - targetMules
    sell up to sellExcess
else:
    silent  // не докупаем, не warn'им
```

### Шаг 6. Итоговое сообщение в чат

Одно сообщение если AnyActivity:
```
Trader: spent {X}g | earned {Y}g | net {Y-X}g | sold {A} livestock | butchered {B} ({M} meat, {H} hides) | sold {L} lame horses | sold {Q} lame mules | ~{D} days food
```

Цвет: зелёный если `net ≥ 0`, красный если `net < 0`.

Если warhorses < unmountedInf — отдельное **красное** сообщение:
```
⚠ Need {N} more warhorses for optimal speed
```

---

## Триггер 2: Sell All (диагональная стрелка)

### Жалоба пользователя
"При нажатии Sell All все лошади продаются" — потому что vanilla TransferAll продаёт **всё** что не залочено пользователем вручную.

### Что ДОЛЖНО происходить

```
Wave 1: продать ВСЕХ lame warhorses (без оглядки на target)
Wave 2 (после recalc): если warhorses > targetWarhorses → продать самых дорогих unlocked до target

То же для mules: Wave 1 (all lame) + Wave 2 (most expensive unlocked above target)
```

### Подходы (обсуждаемо)

**A) Postfix (текущая попытка):** запускается после vanilla. **Не работает** — vanilla уже всё продала.

**B) Prefix с локами:** залочить cheapest target_count → vanilla продаёт всё остальное. **Не работает** если user сам хочет продать ниже target — мы насильно лочим то что user хочет дампить.

**C) Полная замена Prefix returning false:** наш код полностью заменяет vanilla. Сложно, надо реплицировать non-horse trading логику.

**D) Hook другой функции** (не ExecuteSellAllItems) — если есть событие "user pressed sell all but before transfer executes". Найти.

### Какой выбираем
**Пока неизвестно.** Текущая попытка (Postfix) не работает. Подход B (Prefix+lock) отвергнут пользователем. Нужно обсуждение.

---

## Settings (на момент тестов)

### Price thresholds — ОТКЛЮЧЕНЫ

Изначально были:
- `BuyPriceCap = 1.5` — не покупаем дороже 150% от base value
- `SellPriceFloor = 0.7` — не продаём дешевле 70% от base value

**По запросу пользователя на момент тестов сняты** (effectively disabled).

Причина: пользователь предположил, что TOR может занижать sell-цены через `TORTradeItemPriceFactorModel.GetPrice` (Trade skill дёргает множитель), и наш фильтр 0.7 может отсекать всё подряд при низком навыке.

После теста с отключёнными threshold'ами решим — возвращать ли с учётом calibration или нет.

### Морал-баф разнообразия еды
Сохраняем минимум 1 единицу каждого food-типа при продаже излишка. **Не** убираем — это игровая фича а не bug.

---

## Технические детали

### Транзакции
```csharp
// Buy
settlement.ItemRoster.AddToCounts(element, -count);
party.ItemRoster.AddToCounts(element, count);
GiveGoldAction.ApplyBetweenCharacters(Hero.MainHero, settlement.OwnerClan?.Leader, totalCost);

// Sell
party.ItemRoster.AddToCounts(element, -count);
settlement.ItemRoster.AddToCounts(element, count);
GiveGoldAction.ApplyBetweenCharacters(settlement.OwnerClan?.Leader, Hero.MainHero, totalEarned);

// Butcher (без merchant — конверсия внутри party)
party.ItemRoster.AddToCounts(livestock, -1);
party.ItemRoster.AddToCounts(DefaultItems.Meat, meatCount);
party.ItemRoster.AddToCounts(DefaultItems.Hides, hideCount);
```

### Цены
```csharp
town.GetItemPrice(element, party, isSelling: bool)
// settlement.Town может быть null для village → fallback на Item.Value
```

### Lame detection
```csharp
equipmentElement.ItemModifier?.StringId == "lame_horse"
```

Modifier group `ItemModifierGroup.horse` применим ко **всем** HorseComponent items (включая мулов и livestock).
