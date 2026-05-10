# Spell-as-Item: Design

**Branch:** `spellAsItem`
**Date:** 2026-05-10
**Tier:** B (reusable goods items wired to existing SpEffects). The items are scroll-flavored in name but use ER's non-consuming goods semantics — `isConsume = 0`, so using one applies its SpEffect without removing the item from inventory.
**Scope:** Replace the `AddSpell` / `RemoveSpell` stubs in `Dialog.cs` and `PapyrusEMEVD.cs` so that Morrowind Spell and Power records become reusable goods items in Elden Ring, wired to the SpEffect rows that `SpeffManager` already generates.

## Problem

Five of Morrowind's seven spell-record types (Ability, Curse, Disease, Blight, Corprus) already round-trip through this codebase: each gets a backing event flag, and `AddSpell` / `RemoveSpell` flips the flag, which a maintenance script reads to apply or remove the SpEffect. See `Dialog.cs:1127` and `PapyrusEMEVD.cs:991`.

The remaining two — **Spell** and **Power** — fall through to a stub that emits nothing. See `Dialog.cs:1116-1147` and `PapyrusEMEVD.cs:980-1012`. The TODO comment cites missing work to map spells to inventory items.

Concrete impact: any quest beat that grants or removes a Spell or Power silently no-ops. That includes Mages Guild rewards, trainer beats, punitive spell-strip events, and main-quest grants.

## Approach

Generate one **Goods item** per Spell/Power, marked reusable (`isConsume = 0`), capped at one per inventory (`maxRepositoryNum = 1`), and wired to the SpEffect that `SpeffManager` already produces (`refId_default = spell.row`). Replace the AddSpell/RemoveSpell stubs with calls that route through the same `AwardItemLot` / `GetOrRegisterRemoveItem` flows the working AddItem/RemoveItem paths already use.

Damage and other unmapped Morrowind effects produce inert SpEffects today (`SpeffManager.cs:218-267` only handles seven cases). We accept this — the scroll still gets generated and granted, and its description is suffixed with `[unimplemented]` so the inert scrolls form a self-documenting backlog of effects to map next. This was an explicit decision: generate the item regardless, surface the gap in the description, defer effect-mapping work to a separate branch.

## Architecture

One new pass inside `ItemManager`. No new manager class.

- `ItemManager` constructor gains a `SpeffManager` parameter (becomes its 7th dependency — wiring change in `Main.cs`).
- New private method `RegisterSpellScrolls()` runs at the end of ItemManager construction, after the existing item-build phase.
- New private dictionary `spellScrolls : Dictionary<string, ItemInfo>` keyed by spell id.
- New public accessor `ItemManager.GetSpellScroll(string spellId) : ItemInfo` (returns null on miss).
- Stub sites in `Dialog.cs:1116-1147` and `PapyrusEMEVD.cs:980-1012` are replaced with calls that mirror the existing AddItem/RemoveItem flows at `Dialog.cs:1106-1112` and `Dialog.cs:883-902`.
- The `Cast` branch at `Dialog.cs:1148-1158` and `PapyrusEMEVD.cs:1021-1033` is **not** modified — it already works via `GiveSpEffectToPlayer(spell.row)`.

## Data flow

### Build-time

1. `SpeffManager` constructs as today (no change). Each Spell/Power gets an SpEffect row stored on `SpeffSpell.row`.
2. `ItemManager` constructs. After existing item processing, `RegisterSpellScrolls()` walks `speffManager.GetSpeffBySpellType(Spell)` and `GetSpeffBySpellType(Power)`. For each:
   - Clone reusable Goods template row.
   - Override fields per the table below.
   - `paramanager.AddOrReplaceRow(EquipParamGoods, row)`.
   - Build `ItemInfo` of type `Goods` with the new row id.
   - `spellScrolls[spell.id] = itemInfo`.
3. Layout/Dialog/Papyrus passes generate scripts. The previously empty AddSpell/RemoveSpell branches now emit AwardItemLot / event-flag instructions.

### Runtime

**`AddSpell "fireball"` in dialog →**
```
ItemInfo scroll = itemManager.GetSpellScroll("fireball");
int lotRow = paramanager.GenerateAddItemLot(scroll, 1);
// emitted: AwardItemLot(lotRow)
```
Player gets one "Scroll of Fireball". Subsequent calls hit the `maxRepositoryNum=1` cap silently.

**`RemoveSpell "fireball"` →**
```
ItemInfo scroll = itemManager.GetSpellScroll("fireball");
Script.Flag removeFlag = scriptManager.common.GetOrRegisterRemoveItem(scroll, 1);
// emitted: SetEventFlag({removeFlag.id}, FlagState.On)
```
Existing common-event remove-item handler picks up the flag and calls `RemoveItemFromPlayer(Goods, scrollRow, 1)`.

**`Use scroll` (player action) →** ER's standard goods-use logic fires `refId_default` → applies the SpEffect → buff/heal/inert effect for its duration. `isConsume=0` keeps the scroll in inventory.

**`Cast "fireball"` (NPC at player) →** unchanged.

## Goods row construction

**Template:** clone an existing reusable-buff Goods row from base ER. Selected at implementation time by inspection — the chosen template only contributes audio/menu-category defaults since the meaningful fields are all overwritten.

**Field overrides per scroll:**

| Field | Value |
|---|---|
| `refId_default` | `spell.row` (existing SpEffect id) |
| `isConsume` | `0` |
| `maxNum` | `1` |
| `maxRepositoryNum` | `1` |
| `iconId` | from `IconManager.BuffInfo` for the spell's primary effect (mirrors `SpeffManager.cs:205-206`); `-1` if no match |
| name (FMG) | `"Scroll of {SpellName}"` |
| description (FMG) | auto-generated effect summary; suffixed with `[unimplemented]` if no effect cases mapped |
| `goodsCategory` | template default |

Naming is the same pattern for both Spell and Power records — no cosmetic differentiation. If once-per-day Power semantics are added later, they will be distinguished behaviorally and naming can follow then.

**ID range:** scrolls allocate from a contiguous block in EquipParamGoods that doesn't collide with vanilla rows or other generated items. Exact base id is locked in at implementation time by inspecting `Paramanager`'s existing allocation conventions and choosing the next free band.

## Error handling

**Null-spell guard (latent bug fix):** `Dialog.cs:1118` calls `GetSpellSpeff(...)` then accesses `.spellType` with no null check. A typo'd or deleted spell reference NullRefs at build. Fix: guard with `if (spell == null) { Lort.Log warning; break; }` in AddSpell, RemoveSpell, and the adjacent Cast block. Same fix applied to `PapyrusEMEVD.cs`.

**Scroll lookup miss:** `GetSpellScroll(id)` returns null only if a spell exists but RegisterSpellScrolls didn't process it. Defensive: log warning and skip emitting AwardItemLot. Asymmetric with the existing `GetItem` at `Dialog.cs:897` (which throws on miss) — for scrolls a missing entry shouldn't kill the build, since the SpEffect-only path was the working baseline.

**Inert scrolls (unmapped effects):** generated as designed. Description suffix `[unimplemented]` makes them greppable from FMG dump. No build-time warning — the count would be in the hundreds.

**Duplicate AddSpell calls:** handled by `maxRepositoryNum=1`. Game silently caps. No script-side dedup.

**RemoveSpell when player doesn't have scroll:** `RemoveItemFromPlayer` no-ops on missing items. Matches Morrowind's "remove from known list, skip if not known" semantics.

**Save-game compatibility:** new scrolls only appear when AddSpell beats fire on this build. Existing save-files in mid-progress will not retroactively get scrolls for past quest beats — same compat story every other build has.

**Build-time failure modes:**
- Goods template row not found → throw with clear message naming the template id; build fails fast.
- ID range collision at insertion → `AddOrReplaceRow` surfaces immediately.

**Working paths untouched:** Ability/Curse/Disease/Blight/Corprus/Cast all branch before the new code. The diff is purely additive on the Spell/Power branch. No regression risk for the five working types.

## Testing

No meaningful unit-test coverage exists at this layer — `JortPob.Tests` is sparse and ItemManager/SpeffManager have heavy constructor dependencies that block isolated tests without refactor. Verification is build-time + in-game.

### Build-time

1. Full `Convert()` completes without exceptions.
2. New goods row count == count of SpeffSpell of type Spell or Power.
3. FMG text bank contains "Scroll of X" entries; descriptions end with `[unimplemented]` for unmapped-effect spells.
4. Diff emitted EMEVD/ESD around AddSpell/RemoveSpell call sites — confirm `AwardItemLot(...)` and `SetEventFlag(...)` instructions now appear where the stubs were.

### In-game smoke tests (manual)

1. **Mapped-effect grant:** trigger an AddSpell beat for a Restore-family spell. Verify scroll appears, use it, verify HP increases.
2. **Unmapped-effect grant:** same flow with a damage spell. Verify scroll appears with `[unimplemented]` description; using it does nothing visible.
3. **RemoveSpell:** trigger a script that removes a spell. Verify scroll disappears from inventory.
4. **Reusability:** use a buff scroll twice. Verify buff reapplies and scroll persists.
5. **Duplicate grant:** trigger AddSpell for the same spell twice. Verify exactly one scroll in inventory.

### Regression

6. NPC casts a spell at player (existing Cast path). Verify SpEffect still applies.
7. Quest beat involving Ability/Curse/Disease. Verify the maintenance-script flag flow still works.

### Pre-PR gate

Before opening the PR, run the existing test suite (`dotnet test` against `JortPob.Tests`) and confirm passing. The suite is sparse today (one test in `DescribeUtility`), but passing the existing tests is a hard floor — non-negotiable, surfaced to the user before push.

### Out of scope

Automated tests for param-row construction, FMG generation, and stub emission. The build itself is the test for those. Adding focused unit tests requires an ItemManager refactor and is tracked separately.

## Out-of-scope (intentionally)

- Mapping additional MagicEffect cases (Damage*, Paralyze, Charm, Levitate, etc.) to SpEffect fields. Tracked as a follow-up; this branch surfaces the inert scrolls so the backlog is visible.
- Mana-cost gating on scroll use. Reusable + free is acknowledged as more permissive than Morrowind's magicka-cost model; revisit when (if) cost-gating is introduced.
- Once-per-day Power semantics. Powers are reusable in this branch like Spells; per agreed scope.
- Generating real ER `Magic` table sorcery/incantation entries (tier-3 work).
- Refactoring ItemManager's constructor dependency list.
