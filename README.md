# Kaan's QoL (SPT 4.1.6)

One BepInEx client plugin plus one small server mod that bundle **twelve former stand-alone quality-of-life mods** and add a new one, **Magazine Swap**. Every module has its own section in the F12 menu with an **Enabled** switch at the top, so you only run what you want.

## How this mod was made (AI disclosure)

This mod was **programmed by Claude**, an AI assistant made by Anthropic.
**Kaan was the tester**: Kaan has no programming background, played with the mod
on SPT 4.1.6, reported what went wrong, and Claude fixed it. No human programmer
has reviewed the code. It is open source so anyone can check it — if you find a
bug, please open an [issue](https://github.com/Kaanori/spt-kaansqol/issues).

## Requirements

- SPT 4.1.6 (EFT 0.16.9). This mod has a client part and a server part; the ZIP contains both.
- Replaces 12 client mods and 2 server mods (see the table). Remove the old stand-alone DLLs/folders when upgrading.

## Installation

1. Download `KaansQoL-1.0.0.zip` from the [Releases](https://github.com/Kaanori/spt-kaansqol/releases) page.
2. Extract it **into your SPT game folder** (the folder that contains `EscapeFromTarkov.exe`).
   The ZIP already has the right layout; the mod ends up in `BepInEx/plugins/` (client part) and `SPT_Runtime/user/mods/KaansQoL/` (server part).
   On Windows, make sure "Extract All" does not add an extra subfolder.
3. Restart the SPT server and the game.

## Uninstall

Delete the mod's files from `BepInEx/plugins/` (client part) and `SPT_Runtime/user/mods/KaansQoL/` (server part) (settings: `BepInEx/config/` for client mods) and restart the SPT server and the game.

## What's inside – at a glance

| Module | Former mod | What it's for |
|---|---|---|
| Auto Run | AutoLaufen | Double-tap W and keep walking – no holding the key on long routes. |
| Turn To Look Direction | Blickrichtung | After free-look, your body turns to where you looked. |
| Sort Button | Sortierknopf | The stash's sort button for secure container, backpack and other containers – also in raid. |
| Accept All | AllesAnnehmen | One click takes all trader messages/items into the stash. |
| Weightless Secure Container | BehaelterOhneGewicht | What's inside the secure container doesn't count as weight. |
| Calm Wind | Windstille | Quieter wind ambience. |
| No Painkiller Filter | KeinSchmerzmittelFilter | No grey screen on painkillers – they still work. |
| Hack Keycard Doors | KartentuerHacken | Hack keycard doors instead of needing the card. |
| Lock Doors | TuerAbschliessen | Lock doors behind you, kick in any door, bots stay out. |
| Icebreaker Heli Without Flare | HeliOhneFackel | Icebreaker's heli extract without a flare. |
| Belt Slot Fix | GuertelFix | Fixes ghost windows of the Pack 'n' Strap belt slot on bodies. |
| Map Arrow Tip | PfeilSpitze | DynamicMaps arrows point with their tip at your position. |
| **Magazine Swap** | *new* | On reload the old magazine goes exactly where the new one came from. |
| Golden Star Balm duration (server) | BalsamDauer | The balm's pain/radiation relief lasts longer (default 15 min). |
| All extracts (server) | AlleAusgaenge | Every extract of every map is always available. |

**Works together with** (optional, the matching module switches itself off if the partner is missing): DynamicMaps, Trenchfoot-BeltSlot, Manimal's Icebreaker, ORBIT.

| | |
|---|---|
| **Tested with** | SPT 4.1.6 (EFT `0.16.9.40743`), BepInEx 5, developed and played on Linux (Proton). The server part also runs on Windows. |
| **Client** | `KaansQoL.dll` -> `BepInEx/plugins/KaansQoL/` (GUID `com.kaan.qol`, version 1.0.0) |
| **Server** | `KaansQoL.Server.dll` -> `SPT_Runtime/user/mods/KaansQoL/` (net10, SPT 4.1) |
| **Optional partners** | DynamicMaps (module 12), Trenchfoot-BeltSlot (module 11), Manimal's Icebreaker (module 10), ORBIT (module 09, only for the bot lock-out) - a module whose partner is missing simply stays off |
| **License** | MIT |

> **Quick context:** SPT is a purely *offline* version of Escape from Tarkov. Nothing here is meant for BSG's official servers. The hacking/lock/door modules and the sort-in-raid feature change single-player rules only.

## Modules

Every module is on by default (except where noted). F12 -> sections `01` ... `13`. All settings have English labels and tooltips.

| F12 section | Module (former mod) | What it does |
|---|---|---|
| **01 Auto Run** | AutoLaufen | Double-tap `W` (or press `Home`) and the character keeps walking forward. Stop keys (default `W A S D Space`), start keys, double-tap keys and window are editable with an in-menu key recorder (`+ Add key`). Optional: keep running with the inventory open, continuous sprint, on-screen hint. |
| **02 Turn To Look Direction** | Blickrichtung | Releasing the look-around key (middle mouse) turns the *body* to where you looked instead of snapping the head back. Option: adopt pitch too. |
| **03 Sort Button** | Sortierknopf | Adds the stash's sort button to the secure container and backpack (or every container) and lifts the game's "no sorting in raid" block for your own containers. Position, size and level are adjustable. |
| **04 Accept All** | AllesAnnehmen | One button (and hotkey `F9`) in the messenger window that takes **all** trader attachments from **all** traders straight into the stash, redeems profile bonuses and marks everything as read. Can also take over the paperclip button of each trader. |
| **05 Weightless Secure Container** | BehaelterOhneGewicht | The *contents* of the secure container no longer count towards carry weight (the container itself still weighs). |
| **06 Calm Wind** | Windstille | Dampens the wind ambience loop (the game never goes below 0.2 volume). Threshold and volumes adjustable. |
| **07 No Painkiller Filter** | KeinSchmerzmittelFilter | Removes the grey desaturation while a painkiller is active; the painkiller itself still works. |
| **08 Hack Keycard Doors** | KartentuerHacken | Adds **Hack** to locked keycard doors (hack time, range, success chance, label adjustable) and optionally shows only the matching card at the reader. |
| **09 Lock Doors** | TuerAbschliessen | Adds **Lock** to the door menu (with the key animation and sound), lets you kick in any door, stops bots from kicking in doors, and keeps bots out of doors *you* locked. Optionally also locks doors without a keyhole. |
| **10 Icebreaker Heli Without Flare** | HeliOhneFackel | The Icebreaker's helicopter exit is called at raid start - no green flare needed. (Cost: set `HeliExfilCost` in the Icebreaker's own config.) Needs Manimal's Icebreaker. Takes effect at the next raid start. |
| **11 Belt Slot Fix** | GuertelFix | Fixes the corpse view of the Pack 'n' Strap belt slot mod (Trenchfoot-BeltSlot): stale windows of the previous body no longer shine through. Needs Trenchfoot-BeltSlot. |
| **12 Map Arrow Tip** | PfeilSpitze | DynamicMaps arrows point with their *tip* at your position and rotate around the tip. Needs DynamicMaps. |
| **13 Magazine Swap** | *new* | See below. |

### 13 Magazine Swap (new)

Vanilla: when you reload (`R`) the ejected magazine goes into the **smallest free grid of vest or pockets** - usually a pocket, whichever place the new magazine came from.

With this module the ejected magazine goes to **exactly the place the new magazine was taken from**: same container (vest, pocket, backpack, a case inside the backpack...), same position, same rotation - like dragging a magazine onto the weapon does it. If it does not fit there, the game behaves as usual.

| Setting | Default | |
|---|---|---|
| Enabled | on | |
| Empty magazines to backpack | off | An *empty* ejected magazine goes into the backpack first (if there is room), before the swap rule. |
| Never put magazines in pockets | off | The ejected magazine is never put into the pockets - neither by the swap nor by the game's own choice (which then uses the vest first, then the backpack). If there is absolutely no other room it still lands in the pockets rather than on the ground. |
| Write log | off | Logs where each ejected magazine went. |

What it does **not** touch: quick reload (double `R` - the old magazine still drops on purpose), bots and other players, reloads with ammo/internal magazines, and magazines that are pinned. Limits: the old magazine must fit the new one's cell (different sizes -> vanilla), and the module is built on the game's own swap operation, so a game update that changes `ReloadExternalMagResult` may need a rebuild.

How it works (for the curious): a prefix on `Player.FirearmController.ReloadExternalMagResult.Run` replaces the move sequence by the game's own `ItemManipulator.Swap` (both items leave and enter at the same moment) and packs the result into a normal `ReloadExternalMagResult`, so animation, chamber round and ergonomics run unchanged. Two tiny prefixes make the UI events and a possible rollback run in swap order.

## Server part (`KaansQoL.Server.dll`)

Replaces the two server mods **BalsamDauer** and **AlleAusgaenge**. Settings live in `user/mods/KaansQoL/config.json` (created on first start; edit and restart the server, no rebuild):

```json
{
  "balsamDuration": { "enabled": true, "minutes": 15, "effects": ["Pain", "RadExposure"] },
  "allExtracts":    { "enabled": true, "chance": true, "entryPoints": true, "timeWindows": true }
}
```

* **balsamDuration** - Golden Star Balm keeps its Pain/RadExposure effects for `minutes` (vanilla 5:50 / 6:40 min). The tiny per-second energy/hydration buffs are deliberately not stretched.
* **allExtracts** - every extract of every map is always reachable: chance 100 %, all entry points merged, time windows opened (trains stay excluded).

On the first start without a `config.json` the values of the old `user/mods/BalsamDauer/balsam.json` and `user/mods/AlleAusgaenge/ausgaenge.json` are copied over (log line `Migrated N settings from ...`); the old files are never changed or deleted. If you already moved the old server mod folders away, copy `balsam.json` / `ausgaenge.json` into `user/mods/KaansQoL/` before the first start - they are picked up from there too (`installieren.sh` does this). If an old server mod is still installed next to it, a warning is logged (both would apply the same change - harmless but redundant).

## Installation

1. Close the game and the server.
2. Client: copy `KaansQoL.dll` to `BepInEx/plugins/KaansQoL/`.
3. Server: copy `KaansQoL.Server.dll` to `SPT_Runtime/user/mods/KaansQoL/`.
4. **If you used the former stand-alone mods, remove them** (see next section). Start game and server.

`installieren.sh` in this folder does steps 1-3 plus the clean-up for the author's own setup; read it before using it elsewhere.

## Upgrading from the separate mods

* **Settings are migrated automatically.** On the first start (when `com.kaan.qol.cfg` does not exist yet, or for every setting that is new in it) the plugin reads the old files `BepInEx/config/<old GUID>.cfg` and copies every matching value into its own section. The log shows `Migrated N settings from <mod>`. The old `.cfg` files are only read - they are never changed or deleted.
* **No double patching.** If the old DLL of a module is still installed, that module stays **off** and the log says so (`Module '01 Auto Run' stays OFF: the old stand-alone mod AutoLaufen (com.kaan.autolaufen) is still installed ...`). The detection uses the old plugin GUID, so a half-finished update cannot patch the same game method twice. Remove the old DLL and restart to switch the module on.
* Removing the old DLLs is a manual step on purpose (the build never touches them): rename `X.dll` to `X.dll.vor-kaansqol-<date>` (BepInEx ignores that), and for the server mods move the folders out of `user/mods`.

| Old mod | Old DLL (`BepInEx/plugins/`) | Old GUID |
|---|---|---|
| AutoLaufen | `AutoLaufen.dll` | `com.kaan.autolaufen` |
| Blickrichtung | `Blickrichtung.dll` | `kaan.blickrichtung` |
| Sortierknopf | `Sortierknopf/Sortierknopf.dll` | `com.kaan.sortierknopf` |
| AllesAnnehmen | `AllesAnnehmen.dll` | `com.kaan.allesannehmen` |
| BehaelterOhneGewicht | `BehaelterOhneGewicht/BehaelterOhneGewicht.dll` | `com.kaan.behaelterohnegewicht` |
| Windstille | `Windstille.dll` | `com.kaan.windstille` |
| KeinSchmerzmittelFilter | `KeinSchmerzmittelFilter.dll` | `com.kaan.keinschmerzmittelfilter` |
| KartentuerHacken | `KartentuerHacken.dll` | `com.kaan.kartentuerhacken` |
| TuerAbschliessen | `TuerAbschliessen.dll` | `com.kaan.tuerabschliessen` |
| HeliOhneFackel | `HeliOhneFackel.dll` | `kaan.heliohnefackel` |
| GuertelFix | `GuertelFix.dll` | `com.kaan.guertelfix` |
| PfeilSpitze | `PfeilSpitze.dll` | `com.kaan.pfeilspitze` |
| BalsamDauer (server) | `user/mods/BalsamDauer/` | `com.kaan.balsamdauer` |
| AlleAusgaenge (server) | `user/mods/AlleAusgaenge/` | `com.kaan.alleausgaenge` |

## Formerly separate mods

These were published (or kept) as individual mods. Their Forge pages stay as they are; this combined mod supersedes them.

| Former mod | Forge page |
|---|---|
| AutoLaufen (Auto Run) | https://sp-mod.com/mod/3275/autolaufen-auto-run |
| Blickrichtung (Turn To Look Direction) | https://sp-mod.com/mod/3277/blickrichtung-turn-to-look-direction |
| AllesAnnehmen (Accept All) | https://sp-mod.com/mod/3271/allesannehmen-accept-all |
| BehaelterOhneGewicht (Weightless Secure Container) | https://sp-mod.com/mod/3272/behaelterohnegewicht-weightless-secure-container |
| Windstille (Calm Wind) | https://sp-mod.com/mod/3270/windstille-calm-wind |
| KartentuerHacken (Hack Keycard Doors) | https://sp-mod.com/mod/3278/kartentuerhacken-hack-keycard-doors |
| TuerAbschliessen (Lock Doors) | https://sp-mod.com/mod/3280/tuerabschliessen-lock-doors |
| AlleAusgaenge (All Extracts, server) | https://sp-mod.com/mod/3281/alleausgaenge-all-extracts |
| BalsamDauer (Golden Star Balm Duration, server) | https://sp-mod.com/mod/3282/balsamdauer-golden-star-balm-duration |
| Sortierknopf, KeinSchmerzmittelFilter, HeliOhneFackel, GuertelFix, PfeilSpitze | never published separately |

## Tests (offline, no game needed)

* `tests/OfflineTest/run.sh` - migration against *copies* of real cfg files, the duplicate guard, Magazine Swap with the game's real inventory classes and item templates (swap position, rollback, size mismatch, pocket rule, backpack rule), and all Harmony patches against the real `Assembly-CSharp`. Changes nothing in the game folder.
* `tests/ServerConfigTest/run.sh` - the server `config.json` and its migration.

## Notes

* The in-game *notification texts* of some modules (e.g. "Accept All" results, hack progress) are still the original German strings; all F12 labels and tooltips are English.
* Source comments are German, as in the original mods.

## Building from source

Needs the .NET 10 SDK and an SPT 4.1.6 installation to build against.

```bash
./build.sh             # Linux: build only
./build.sh --install   # build and copy into the game folder
```
```powershell
.\build.ps1            # Windows
.\build.ps1 -Install
```

The game folder is set in `Directory.Build.props` (Windows default `C:/SPT`)
or via the environment variable `SPT_DIR`.

## Support

Bugs and questions: [GitHub issues](https://github.com/Kaanori/spt-kaansqol/issues).

## License

MIT — see [LICENSE](LICENSE).
