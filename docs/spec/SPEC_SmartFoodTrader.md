# SmartFoodTrader — спека доработки

Расширение существующего `Behaviors/SmartFoodTraderBehavior.cs` + новый Postfix-патч на Sell All.

## Цели

При входе MainParty в Town/Village автоматически:
1. Управлять food-запасом (уже есть)
2. Обрабатывать livestock — забивать или продавать в зависимости от выгоды
3. Продавать lame/sick лошадей и мулов
4. Предупреждать красным если не хватает боевых лошадей

На нажатие диагональной стрелки "Transfer All" в trade screen (Postfix):
5. После vanilla отработал — Wave 1 (lame/sick) + Wave 2 (самых дорогих unlocked) для лошадей и мулов

---

## 1. Food (текущее, уже работает)

```
dailyConsumption = abs(party.FoodChange)
target = 10 дней
buffer = 15 дней

if currentFood < target:
    купить cheapest до target, цена ≤ 1.5 × value
if currentFood > buffer && foodTypes ≥ 2:
    продать excess, начиная с наиболее обильной, оставить ≥1 каждого типа
    цена ≥ 0.7 × value
```

---

## 2. Livestock (Hogs/Sheep/Cattle)

Livestock считается в `herd_size`. Каждая единица — выбор:

**Sell as livestock:**
```
benefit_sell = town.GetItemPrice(livestock, isSelling=true)
              + (-cost_to_buy_food_equivalent если еды не хватает)
```

**Butcher manually:**
```
horseComp = item.HorseComponent  // IsLiveStock=true
meatYield  = horseComp.MeatCount
hideYield  = horseComp.HideCount
benefit_butcher = meatYield × marketPrice(meat)
                + hideYield × marketPrice(hides)
                + (saved cost — мясо идёт в food, не надо докупать)
```

**Решение:** `max(benefit_sell, benefit_butcher)`

**Реализация butcher (нет в vanilla API):**
```csharp
party.ItemRoster.AddToCounts(livestockElement, -1);
party.ItemRoster.AddToCounts(meatItem, meatYield);
party.ItemRoster.AddToCounts(hideItem, hideYield);
```
Canonical item IDs: `meat`, `hides` (проверить в Native/ModuleData/items.xml).

**Цена-фильтр для продажи:** ≥ 0.7 × base value.

**Порядок шагов:**
1. Посчитать food-deficit (до 10 дней)
2. Для каждого livestock-юнита решить sell vs butcher
3. Применить решения (продать или забить)
4. Пересчитать food-deficit с учётом полученного мяса
5. Доделать food-флоу (докупка/продажа избытка)

---

## 3. Лошади и мулы — на входе settlement (auto)

**Формулы:**
```
N = totalMenCount          (party.MemberRoster.TotalManCount, ВКЛЮЧАЕТ раненых)
U = unmounted_infantry     (party.NumberOfMenWithoutHorse, ВКЛЮЧАЕТ раненых пеших)

target_warhorses = U + 0.15 × N    (sweet spot + 15% buffer)
target_mules     = 0.45 × N
reserve_for_loot = 0.40 × N        (свободный herd-слот, не лезем)
```

**Безопасный herd-buffer (без штрафа скорости):**
```
herd_size = numberOfPackAnimals + numberOfLivestockAnimals 
          + max(0, numberOfMounts - U)
herd_penalty starts when herd_size > totalMenCount
penalty = -0.3 × ((herd_size - totalMenCount) / totalMenCount), capped at -0.8
```

**Действия на entry:**

| Состояние | Действие |
|---|---|
| `warhorses < U` (по `NumberOfMounts`) | Красное сообщение: "Не хватает X боевых лошадей" |
| `warhorses > target_warhorses` | **Wave 1**: продать lame/sick до target_warhorses |
| `warhorses в [U, target_warhorses]` | Тишина |
| `mules > target_mules` (по `NumberOfPackAnimals`) | **Wave 1**: продать lame/sick мулов до target_mules |
| `mules < target_mules` | Тишина (не докупаем) |

**Wave 1 detection:** `equipmentElement.ItemModifier?.StringId == "lame_horse"`.

**Цена-фильтр:** ≥ 0.7 × base value.

**Wave 2 НЕ запускается на входе.**

**Раненые** автоматически учитываются — `NumberOfMenWithHorse/WithoutHorse` итерируют roster по `Number` (включает wounded). Wounded пехота требует лошадь после выздоровления → не продаём преждевременно.

---

## 4. Sell All (Postfix patch)

**Hook:** `Harmony [HarmonyPostfix] [HarmonyPriority(Priority.Last)]` на метод "Transfer All" / диагональной стрелки в `SPInventoryVM`.

Точное имя метода найти при имплементации (likely `ExecuteTransferAllItem` или похожий).

**После того как vanilla и другие моды отработают:**

```
// Warhorses
remaining_warhorses = party.ItemRoster.NumberOfMounts
Wave 1: продать всех с lame_horse modifier (без оглядки на target)
recalc
Wave 2: если remaining > target_warhorses → продать самых дорогих unlocked до target_warhorses

// Mules
remaining_mules = party.ItemRoster.NumberOfPackAnimals
Wave 1: продать всех с lame_horse modifier
recalc
Wave 2: если remaining > target_mules → продать самых дорогих unlocked до target_mules
```

Цена-фильтр: ≥ 0.7 × base value.

**Native locks:** Skip items locked via vanilla mechanism.
```csharp
var tracker = Campaign.Current.GetCampaignBehavior<IViewDataTracker>();
var lockedItems = tracker.GetInventoryLocks();
bool isLocked = lockedItems.Contains(CampaignUIHelper.GetItemLockStringID(element));
```

---

## 5. Итоговое сообщение в чат

После всех операций — единое сообщение:

```
Food: spent X, earned Y, days_of_food = D
Livestock: sold S head, butchered B head, gained M meat / H hides
Warhorses: sold L lame, earned X g
Mules: sold M lame, earned Y g
```

Красным цветом — отдельная строка, если warhorses < U:
```
⚠ Не хватает {U - warhorses} боевых лошадей до оптимума
```

---

## Открытые при имплементации

- Точное имя метода для Sell All Postfix-патча (через ilspycmd на `TaleWorlds.CampaignSystem.ViewModelCollection.dll`)
- Точные item IDs для meat/hides (через grep `Native/ModuleData/items.xml`)
- Проверить корректность `MBObjectManager.Instance.GetObject<ItemObject>("meat")` или альтернативного API

---

## Файлы

```
TOR_QoLs/
├── Behaviors/
│   └── SmartFoodTraderBehavior.cs   ← основная логика food + livestock + horses
├── Patches/
│   └── SellAllPostfixPatch.cs        ← Wave 1+2 на Sell All
└── SubModule.cs                       ← регистрация behavior
```

Build: `dotnet build -c Release` → деплой в `Modules/TOR_QoLs/`.
