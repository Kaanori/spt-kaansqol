# Changelog

## 1.2.0 - 2026-11-09

**New module**
- **16 Smooth Sun**: the sunlight (and with it all sun shadows) glides every frame between the game's sun updates instead of
  jumping every 1 s / 0.5 s / 0.1 s (Shadows quality Low / Medium / High). Same method the game uses itself on Ultra; idle on Ultra.
  Optional own update interval, jump threshold for time skips, diagnostics line in the log.
- Offline test: 16 modules.

**Changed (server part)**
- **All extracts**: new `exceptions` list (extract names left untouched). Default `E6` = Streets "Scav Checkpoint": BSG set it to 0 % and it sits 4 m next to the Labs transit, pulling you out before you reach the transit door.

## 1.1.0 - 2026-11-08

Three more of the author's mods moved in. Client plugin `com.kaan.qol` + server mod `KaansQoL`, both 1.1.0.

**Merged (former stand-alone mods):**
ZonenKills 1.0.0, GespraechsFolgen 1.0.0 (client + server route), KeinAufgabenFehlschlag 1.0.0 (server).

**New modules / parts**
- Module **14 Zone Kills**: a kill counts for zone quests while *you* stand in the zone, wherever the enemy is
  (vanilla only checks the zones of the victim). Kills of enemies inside the zone still count.
- Module **15 Dialogue Consequences**: under each answer in VisitAPI conversations it shows, before you pick it, which
  quests the answer completes, fails or unlocks (inline lines or a tooltip marker). Needs VisitAPI (soft dependency).
  Reads quest data through the server route `/gespraechsfolgen/quests`, which is now part of `KaansQoL.Server.dll`
  (the URL is unchanged).
- Server part **No quest failures** (`noQuestFailures` in `config.json`): quests no longer fail through play (kills,
  death, reputation, wrong place, time limits); accepted daily/weekly quests survive the reset with their progress.
  Story branches (quest-vs-quest "either/or" and dialogue-choice variables) are kept on purpose - Dialogue
  Consequences shows what they do. Optional: lift the one-raid rule, remove wait times, per-quest exceptions.

**Migration / safety**
- `Zone Kills` and `Dialogue Consequences` take over the settings of the old `com.kaan.zonenkills.cfg` /
  `com.kaan.gespraechsfolgen.cfg` on the first start; they stay off while the old DLL is still installed.
- The server config section `noQuestFailures` is filled from the old `user/mods/KeinAufgabenFehlschlag/config.json`
  (or `kein-aufgaben-fehlschlag.json` copied into the KaansQoL folder). An existing 1.0.x `config.json` gets the new
  section added once; its other values stay.
- If the old server mod `KeinAufgabenFehlschlag` is still installed, the quest rebuild stays off (log warning); if the
  old `GespraechsFolgen` server mod is still installed, the new route passes through and the old one answers.
- Tests: new `tests/GespraechsFolgenTest` (offline check of the dialogue/quest calculation on the real "Blue Fire" case);
  `ServerConfigTest` and `OfflineTest` extended (migration, duplicate guard, new config section).

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
