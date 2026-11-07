// ---------------------------------------------------------------
//  Magazin-Tausch (Magazine Swap)  -  NEU in Kaan's QoL 1.0.0
//
//  Beim normalen Nachladen (R) wandert das ausgeworfene alte Magazin
//  ab Werk in das kleinste freie Raster von Weste oder Taschen -
//  meist also in eine 1x1-Tasche, egal woher das neue kam. Dieses
//  Modul legt es stattdessen genau dorthin, wo das NEUE Magazin
//  herkam: gleiches Raster, gleiche Position, gleiche Drehung.
//
//  WO DER HEBEL SITZT (nachgesehen im Assembly-CSharp von SPT 4.1.6):
//    InputTranslator.ReloadExternalMagazine(weapon, quickReload)
//      sucht das neue Magazin und rechnet einen Zielplatz fuer das
//      alte aus (GetPrioritizedGridsForUnloadedObject: Weste, dann
//      Taschen, kleinstes Raster zuerst). Beides geht an
//      FirearmController.ReloadMag(neu, itemAddress) ->
//      Operation.ReloadMag -> ReloadExternalMagResult.Run(...).
//    Run() fuehrt die Bestandsaenderung aus: altes Magazin aus der
//      Waffe in den Zielplatz (Move), neues Magazin aus seinem Fach in
//      die Waffe (Move). Erst muss das alte raus, sonst ist der
//      Magazinschacht belegt - und der Platz des neuen ist waehrenddessen
//      noch besetzt. Darum geht der Zielplatz "wo das neue herkam" mit
//      Run() allein nicht.
//
//  WAS HIER PASSIERT: ein Prefix auf Run() ersetzt NUR in diesem einen
//  Fall den Ablauf durch das, was das Spiel beim Ziehen eines Magazins
//  auf eine Waffe per Maus ohnehin macht: ItemManipulator.Swap - beide
//  Gegenstaende werden gleichzeitig ausgebucht und an den Platz des
//  anderen gelegt. Das Ergebnis wird in zwei MoveResult verpackt und in
//  ein normales ReloadExternalMagResult gesteckt, so dass Animation,
//  Munition in der Kammer, Ergonomie usw. unveraendert weiterlaufen.
//  Zwei kleine Postfixe sorgen nur dafuer, dass die Ereignisse fuer die
//  Oberflaeche (RaiseEvents) und ein Zurueckrollen (RollBack) in der
//  Reihenfolge laufen, die Swap selbst benutzt: erst beide raus, dann
//  beide rein.
//
//  WANN NICHTS PASSIERT (dann laeuft das Spiel wie gewohnt):
//    - Schnellnachladen (doppeltes R): das alte Magazin faellt absichtlich
//      zu Boden.
//    - Das neue Magazin steckt nicht in einem Raster (z.B. in einem Fach).
//    - Das alte Magazin passt dort nicht hin (andere Form/Drehung).
//    - Eines der beiden ist per Pin-Symbol angeheftet.
//    - Bots und andere Spieler (nur der eigene Spieler wird angefasst).
//    - Jeder Fehler im Modul: es rollt zurueck und laesst das Spiel ran.
//
//  OPTIONEN (beide aus):
//    Leere Magazine in den Rucksack  - ein LEERES altes Magazin geht
//      zuerst in den Rucksack (Vorrang vor dem Tausch).
//    Nie Magazine in die Taschen     - weder der Tausch noch die
//      Spiel-Auswahl legen ein Magazin in die Taschen; gibt es sonst
//      nirgends Platz, bleibt es beim Spiel-Verhalten (Taschen), damit
//      das Magazin nicht auf dem Boden landet.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using EFT.InventoryLogic.Operations;
using HarmonyLib;

namespace KaansQoL.MagazineSwap
{
	internal sealed class MagazineSwapModule : QolModule
	{
		public override string Id => "MagazineSwap";

		public override string Section => "13 Magazine Swap";

		public override string Beschreibung =>
			"On reload, the ejected (old) magazine goes into exactly the grid slot the new magazine was taken from "
			+ "(same container, same position and rotation) if it fits. Otherwise the game behaves as usual. "
			+ "Quick reload (double R) is not changed.";

		// Neues Modul: keine alte Mod, keine alte cfg.

		protected override void BindSettings(ConfigFile cfg)
		{
			string s = Section;
			Modul.An = Enabled;
			Modul.LeereInRucksack = Cfg.Bind(cfg, s, "Empty magazines to backpack", false,
				"On: if the ejected magazine is empty it goes into the backpack first (if there is room), "
				+ "before the swap rule is tried.");
			Modul.NieInTaschen = Cfg.Bind(cfg, s, "Never put magazines in pockets", false,
				"On: the ejected magazine is never put into the pockets (neither by the swap nor by the game's own choice). "
				+ "The game's own choice then uses the vest first, then the backpack. "
				+ "If there is no other room at all, it still ends up in the pockets rather than on the ground.");
			Modul.Protokoll = Cfg.Bind(cfg, s, "Write log", false,
				"Writes where each ejected magazine went (swap, backpack, vanilla) to the BepInEx console.");
		}

		public override void Apply(Harmony h)
		{
			h.PatchAll(typeof(LadenPatch));
			h.PatchAll(typeof(EreignissePatch));
			h.PatchAll(typeof(ZurueckrollenPatch));
		}
	}

	internal static class Modul
	{
		internal static ManualLogSource Log => KaansQoL.Plugin.Log;

		internal static ConfigEntry<bool> An;

		internal static ConfigEntry<bool> LeereInRucksack;

		internal static ConfigEntry<bool> NieInTaschen;

		internal static ConfigEntry<bool> Protokoll;

		// Ergebnisse, die WIR gebaut haben (Tausch). Nur fuer sie gelten die
		// angepassten Ereignis-/Rollback-Reihenfolgen.
		internal static readonly ConditionalWeakTable<Player.FirearmController.ReloadExternalMagResult, object> Getauscht =
			new ConditionalWeakTable<Player.FirearmController.ReloadExternalMagResult, object>();

		internal static void Melde(string text)
		{
			if (Protokoll != null && Protokoll.Value)
			{
				Log.LogInfo("Magazine Swap: " + text);
			}
		}
	}

	/// <summary>Hilfen zum Suchen von Zielplaetzen.</summary>
	internal static class Plaetze
	{
		internal static bool IstEigenerSpieler(InventoryController steuerung)
		{
			if (!Singleton<GameWorld>.Instantiated)
			{
				return false;
			}
			Player spieler = Singleton<GameWorld>.Instance?.MainPlayer;
			return spieler != null && ReferenceEquals(spieler.InventoryController, steuerung);
		}

		private static Grid[] Raster(Item behaelter)
		{
			CompoundItem c = behaelter as CompoundItem;
			return c == null ? new Grid[0] : c.Grids;
		}

		/// <summary>Erster Platz im Rucksack, der das Magazin aufnimmt.</summary>
		internal static GridItemAddress InRucksack(InventoryEquipment ausruestung, Item magazin)
		{
			Item rucksack = ausruestung.GetSlot(EquipmentSlot.Backpack).ContainedItem;
			foreach (Grid g in Raster(rucksack))
			{
				GridItemAddress a = g.FindLocationForItem(magazin);
				if (a != null)
				{
					return a;
				}
			}
			return null;
		}

		/// <summary>
		/// Wie die Spiel-Auswahl (kleinstes Raster mit Platz zuerst), aber nur Weste, dann
		/// Rucksack - nie die Taschen.
		/// </summary>
		internal static GridItemAddress OhneTaschen(InventoryEquipment ausruestung, Item magazin)
		{
			Item weste = ausruestung.GetSlot(EquipmentSlot.TacticalVest).ContainedItem;
			GridItemAddress ziel = Raster(weste)
				.Select(g => g.FindLocationForItem(magazin))
				.Where(a => a != null)
				.OrderBy(a => a.Grid.GridWidth * a.Grid.GridHeight)
				.FirstOrDefault();
			return ziel ?? InRucksack(ausruestung, magazin);
		}

		internal static bool InTaschen(ItemAddress adresse)
		{
			GridItemAddress g = adresse as GridItemAddress;
			return g != null && g.Grid.ParentItem is Pockets;
		}
	}

	[HarmonyPatch(typeof(Player.FirearmController.ReloadExternalMagResult),
		nameof(Player.FirearmController.ReloadExternalMagResult.Run))]
	internal static class LadenPatch
	{
		[HarmonyPrefix]
		private static bool Vorher(ItemController itemController, Weapon weapon, Magazine nextMagazine,
			bool quickReload, bool isKnownMalfunction, ref ItemAddress vestTargetAddress,
			ref Option<Player.FirearmController.ReloadExternalMagResult> __result)
		{
			try
			{
				// Schnellnachladen: das alte Magazin faellt absichtlich zu Boden.
				if (!Modul.An.Value || quickReload || weapon == null || nextMagazine == null)
				{
					return true;
				}
				InventoryController steuerung = itemController as InventoryController;
				if (steuerung == null || !Plaetze.IstEigenerSpieler(steuerung))
				{
					return true;
				}
				Magazine alt = weapon.GetCurrentMagazine();
				if (alt == null)
				{
					return true;
				}
				InventoryEquipment ausruestung = steuerung.Inventory.Equipment;

				// 1) Leeres Magazin zuerst in den Rucksack (das Spiel legt es dann dorthin).
				if (Modul.LeereInRucksack.Value && alt.Count == 0)
				{
					GridItemAddress rucksack = Plaetze.InRucksack(ausruestung, alt);
					if (rucksack != null)
					{
						vestTargetAddress = rucksack;
						Modul.Melde("empty magazine -> backpack " + rucksack);
						return true;
					}
				}

				// 2) Tausch: altes Magazin an den Platz des neuen.
				if (Tausch.Versuchen(itemController, weapon, alt, nextMagazine, isKnownMalfunction, ref __result))
				{
					return false;
				}

				// 3) Spiel-Auswahl, aber ohne Taschen.
				if (Modul.NieInTaschen.Value && Plaetze.InTaschen(vestTargetAddress))
				{
					GridItemAddress anders = Plaetze.OhneTaschen(ausruestung, alt);
					if (anders != null)
					{
						vestTargetAddress = anders;
						Modul.Melde("not into pockets -> " + anders);
					}
				}
				return true;
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Magazine Swap: " + ex);
				return true;
			}
		}
	}

	internal static class Tausch
	{
		/// <summary>
		/// Ersetzt ReloadExternalMagResult.Run fuer den Tausch-Fall. Liefert true, wenn das Ergebnis
		/// (Erfolg ODER Fehler) gesetzt ist; false = das Spiel soll ganz normal laufen.
		/// Bis zum Setzen von true bleibt der Bestand unveraendert (sonst wird zurueckgerollt).
		/// </summary>
		internal static bool Versuchen(ItemController steuerung, Weapon weapon, Magazine alt, Magazine neu,
			bool isKnownMalfunction, ref Option<Player.FirearmController.ReloadExternalMagResult> ergebnis)
		{
			// Platz des neuen Magazins: muss ein Rasterplatz sein.
			GridItemAddress platz = neu.Parent as GridItemAddress;
			if (platz == null)
			{
				return false;
			}
			if (Modul.NieInTaschen.Value && Plaetze.InTaschen(platz))
			{
				return false;
			}
			// Angeheftete Gegenstaende fasst das Spiel beim normalen Verschieben gesondert an.
			if (neu.PinLockState != EItemPinLockState.Free || alt.PinLockState != EItemPinLockState.Free)
			{
				return false;
			}
			Slot fach = weapon.GetMagazineSlot();
			if (fach == null || alt.Parent == null)
			{
				return false;
			}
			ItemAddress fachAdresse = fach.CreateItemAddress();

			// Probe ohne Folgen: passt das alte Magazin auf den Platz des neuen?
			OperationResult<SwapResult> probe = ItemManipulator.Swap(alt, platz, neu, fachAdresse, steuerung, simulate: true);
			if (probe.Failed)
			{
				Modul.Melde("no swap (" + probe.Error + ") - vanilla");
				return false;
			}

			// Wie im Spiel: Kammer, Zustand merken.
			Slot kammer = weapon.HasChambers ? weapon.Chambers[0] : null;
			Ammo patrone = kammer?.ContainedItem as Ammo;

			OperationResult<SwapResult> tausch = ItemManipulator.Swap(alt, platz, neu, fachAdresse, steuerung);
			if (tausch.Failed)
			{
				return false;
			}
			SwapResult sw = tausch.Value;
			List<UnbindResult> loesenAlt = new List<UnbindResult>();
			List<UnbindResult> loesenNeu = new List<UnbindResult>();
			OperationResult<RemoveResult> ausKammer = default(OperationResult<RemoveResult>);
			OperationResult<IItemOperationResult> nachladen = default(OperationResult<IItemOperationResult>);

			void ZurueckRollen()
			{
				nachladen.Value?.RollBack();
				foreach (UnbindResult u in loesenNeu) u.RollBack();
				foreach (UnbindResult u in loesenAlt) u.RollBack();
				sw.RollBack();
				ausKammer.Value?.RollBack();
			}

			try
			{
				// Patrone aus der Kammer (wie im Spiel, wenn der Verschluss geoeffnet werden muss).
				Weapon.EMalfunctionState zustand = weapon.MalfState.State;
				if (zustand == Weapon.EMalfunctionState.Misfire)
				{
					weapon.MalfState.ChangeStateSilent(Weapon.EMalfunctionState.None);
				}
				ausKammer = (patrone == null || !weapon.MustBoltBeOpennedForExternalReload)
					? default(OperationResult<RemoveResult>)
					: ItemManipulator.Remove(patrone, steuerung);
				weapon.MalfState.ChangeStateSilent(zustand);
				if (ausKammer.Failed)
				{
					sw.RollBack();
					ergebnis = ausKammer.Error;
					return true;
				}

				// Schnellleisten-Bindungen loesen, wie es ItemManipulator.Move fuer beide taete.
				ItemManipulator.TryUnbindItems(steuerung, alt, in loesenAlt, simulate: false);
				ItemManipulator.TryUnbindItems(steuerung, neu, in loesenNeu, simulate: false);

				MoveResult altBewegt = new MoveResult(sw.Item, sw._from, sw.To, steuerung,
					sw._destroyResult1, sw._removeResult1, sw._addResult1, null, loesenAlt, null);
				MoveResult neuBewegt = new MoveResult(sw.Item2, sw._from2, sw.To2, steuerung,
					sw._destroyResult2, sw._removeResult2, sw._addResult2, null, loesenNeu, null);

				// Ab hier wie ReloadExternalMagResult.Run.
				bool passt = neu.IsAmmoCompatible(weapon.Chambers);
				if ((kammer != null && kammer.ContainedItem == null && neu.Count > 0) & passt)
				{
					bool darf = weapon.MalfState.State == Weapon.EMalfunctionState.None
						|| ((weapon.MalfState.State == Weapon.EMalfunctionState.Misfire) & isKnownMalfunction);
					Weapon.EMalfunctionState zustand2 = weapon.MalfState.State;
					if (darf && weapon.MalfState.State != Weapon.EMalfunctionState.None)
					{
						weapon.MalfState.ChangeStateSilent(Weapon.EMalfunctionState.None);
					}
					if (darf)
					{
						nachladen = neu.Cartridges.PopTo(steuerung, kammer.CreateItemAddress());
						if (zustand2 != Weapon.EMalfunctionState.None)
						{
							weapon.MalfState.ChangeStateSilent(zustand2);
						}
						if (nachladen.Failed)
						{
							Error fehler = nachladen.Error;
							foreach (UnbindResult u in loesenNeu) u.RollBack();
							foreach (UnbindResult u in loesenAlt) u.RollBack();
							sw.RollBack();
							ausKammer.Value?.RollBack();
							ergebnis = fehler;
							return true;
						}
					}
				}

				Player.FirearmController.ReloadExternalMagResult r = new Player.FirearmController.ReloadExternalMagResult(
					steuerung, ausKammer.Value, null, altBewegt, neuBewegt, nachladen.Value, weapon, passt, false, isKnownMalfunction);
				Modul.Getauscht.Add(r, new object());
				ergebnis = r;
				Modul.Melde("swapped: old magazine -> " + sw.To);
				return true;
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Magazine Swap: Tausch gescheitert, Bestand wird zurueckgerollt - " + ex);
				try
				{
					ZurueckRollen();
				}
				catch (Exception ex2)
				{
					Modul.Log.LogError("Magazine Swap: Rollback gescheitert - " + ex2);
				}
				return false;
			}
		}
	}

	/// <summary>
	/// Ereignisse fuer die Oberflaeche in Swap-Reihenfolge: erst beide Entnahmen, dann beide Ablagen.
	/// (Im Original kaeme die Ablage des alten Magazins VOR der Entnahme des neuen - am selben Platz.)
	/// </summary>
	[HarmonyPatch(typeof(Player.FirearmController.ReloadExternalMagResult),
		nameof(Player.FirearmController.ReloadExternalMagResult.RaiseEvents))]
	internal static class EreignissePatch
	{
		[HarmonyPrefix]
		private static bool Vorher(Player.FirearmController.ReloadExternalMagResult __instance,
			ItemController controller, CommandStatus status)
		{
			if (!Modul.Getauscht.TryGetValue(__instance, out _))
			{
				return true;
			}
			__instance.Weapon.Parent.RaiseRemoveEvent(__instance.Weapon,
				(status != CommandStatus.Begin) ? CommandStatus.Failed : CommandStatus.Begin, controller);
			__instance.RemoveFromChamberResult?.RaiseEvents(controller, status);
			MoveResult alt = __instance.MoveOldMagResult;
			MoveResult neu = __instance.InsertNextMagResult;
			Entnahme(alt, controller, status);
			Entnahme(neu, controller, status);
			alt._addResult.RaiseEvents(controller, status);
			neu._addResult.RaiseEvents(controller, status);
			__instance.PopNewAmmoResult?.RaiseEvents(controller, status);
			return false;
		}

		// MoveResult.RaiseEvents ohne den letzten Schritt (Ablage).
		private static void Entnahme(MoveResult m, IItemOwner controller, CommandStatus status)
		{
			if (m._setPinLockResults != null)
			{
				foreach (SetPinLockResult r in m._setPinLockResults) r.RaiseEvents(controller, status);
			}
			if (m._setTagResults != null)
			{
				foreach (SetTagResult r in m._setTagResults) r.RaiseEvents(controller, status);
			}
			if (m._unbindResults != null)
			{
				foreach (UnbindResult r in m._unbindResults) r.RaiseEvents(controller, status);
			}
			m._destroyResult.RaiseEvents(controller, status);
			m._removeResult.RaiseEvents(controller, status);
		}
	}

	/// <summary>Zurueckrollen in Swap-Reihenfolge: erst beide Ablagen weg, dann beide Entnahmen zurueck.</summary>
	[HarmonyPatch(typeof(Player.FirearmController.ReloadExternalMagResult),
		nameof(Player.FirearmController.ReloadExternalMagResult.RollBack))]
	internal static class ZurueckrollenPatch
	{
		[HarmonyPrefix]
		private static bool Vorher(Player.FirearmController.ReloadExternalMagResult __instance)
		{
			if (!Modul.Getauscht.TryGetValue(__instance, out _))
			{
				return true;
			}
			MoveResult alt = __instance.MoveOldMagResult;
			MoveResult neu = __instance.InsertNextMagResult;
			__instance.PopNewAmmoResult?.RollBack();
			neu._addResult.RollBack();
			alt._addResult.RollBack();
			neu._removeResult.RollBack();
			alt._removeResult.RollBack();
			if (neu._unbindResults != null) foreach (UnbindResult u in neu._unbindResults) u.RollBack();
			if (alt._unbindResults != null) foreach (UnbindResult u in alt._unbindResults) u.RollBack();
			neu._destroyResult.RollBack();
			alt._destroyResult.RollBack();
			__instance.RemoveFromChamberResult?.RollBack();
			return false;
		}
	}
}
