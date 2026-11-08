# Changelog

## 1.0.1 - 2026-11-07

**Fixed**
- **03 Sort Button**, sorting during a raid: the raid inventory controller had no operation for the sort result
  (`operationResult is of unexpected type ApplySortItemsPositionResult` in `errors.log`). Without other mods nothing
  was sorted and the button stayed busy; with UIFixes "Stack before sort" the items were moved but no events were
  raised, so the open grid kept its old picture and the moved items showed up hatched ("not searched") after
  reopening. In raid the sort now runs as the same `ApplyInventoryChangesOperation` the stash uses, so the open view
  refreshes at once and the items stay known. Only for the player's own containers; the stash path is unchanged.
- **03 Sort Button**: no more "SetUnlockStatus skipped" warning when the button is created (the stash template has
  no canvas group; nothing was broken).

## 1.0.0 - 2026-11-07

First combined release. Client plugin `com.kaan.qol` + server mod `KaansQoL`.

**Merged (former stand-alone mods, each now a module with its own F12 section and an Enabled switch):**
AutoLaufen 1.2.0, Blickrichtung 1.0.0, Sortierknopf 1.0.1, AllesAnnehmen 1.2.0, BehaelterOhneGewicht 1.0.0,
Windstille 1.0.0, KeinSchmerzmittelFilter 1.0.0, KartentuerHacken 1.0.0, TuerAbschliessen 1.1.1,
HeliOhneFackel 1.0.0, GuertelFix 1.0.0, PfeilSpitze 1.0.0; server: BalsamDauer 1.0.0, AlleAusgaenge 1.0.0.

**New**
- Module **13 Magazine Swap**: on reload the ejected magazine goes to the place the new magazine came from
  (same container, position, rotation) if it fits; options "Empty magazines to backpack" and
  "Never put magazines in pockets" (both off by default).
- One shared F12 layout: sections `01` ... `13`, "Enabled" first, English labels and tooltips.
- Settings migration: the values of the old `BepInEx/config/<old GUID>.cfg` files are copied on the first start
  (`Migrated N settings from <mod>` in the log); old files are never changed or deleted.
- Server settings in `user/mods/KaansQoL/config.json`; migrated from the old `balsam.json` / `ausgaenge.json`.
- Duplicate guard: a module stays off (with a log warning) while the old stand-alone DLL is still installed.
- Each module patches with its own Harmony instance, so a module can be switched on/off at runtime
  (Heli Without Flare applies at the next raid; locking doors off mid-raid leaves locked doors locked).
- Optional partners (DynamicMaps, Trenchfoot-BeltSlot, Manimal's Icebreaker) are soft dependencies: a module whose
  partner is missing stays off instead of stopping the whole plugin.

**Changed**
- Setting names are English (old German names are migrated automatically); `Start key mode` values are now
  `Toggle` / `Hold`. Default texts: button "ACCEPT ALL", menu entries "Hack", "Lock", "Unlock"; the auto-run hint reads "Auto run".
- Build copies into the game only with `-p:Installieren=true`.
