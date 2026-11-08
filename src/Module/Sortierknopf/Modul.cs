// ---------------------------------------------------------------
//  Sortierknopf fuer Sicherheitsbehaelter und Rucksack
//
//  Das Lager hat im Spiel bereits einen Sortierknopf: das Bauteil
//  EFT.UI.DragAndDrop.GridSortPanel. Es traegt die ganze Logik in
//  sich - Nachfrage, Fortschrittsanzeige, InteractionsHandlerClass
//  .Sort und die Netzwerk-Uebertragung.
//
//  Darum wird hier NICHTS nachgebaut: ein vorhandenes GridSortPanel
//  wird geklont, in die Ansicht des Behaelters gehaengt und mit
//  Show(steuerung, behaelter) auf ihn gerichtet. Der Knopf sieht
//  dadurch aus wie der im Lager und verhaelt sich auch so.
//
//  Angehaengt wird an ContainedGridsView.Show - die Ansicht, die im
//  Ausruestungsbildschirm den Inhalt von Sicherheitsbehaelter und
//  Rucksack darstellt.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KaansQoL.Sortierknopf
{
	internal sealed class SortierknopfModule : QolModule
	{
		public override string Id => "Sortierknopf";

		public override string Section => "03 Sort Button";

		public override string Beschreibung =>
			"Adds the stash's sort button to the secure container and backpack view, and allows sorting during a raid.";

		public override string AlteGuid => "com.kaan.sortierknopf";

		public override string AlterName => "Sortierknopf";

		public override MigRow[] Umzug => new[]
		{
			new MigRow("1. Allgemein", "Eingeschaltet", "Enabled"),
			new MigRow("1. Allgemein", "Im Sicherheitsbehaelter", "In secure container"),
			new MigRow("1. Allgemein", "Im Rucksack", "In backpack"),
			new MigRow("1. Allgemein", "In jedem Behaelter", "In every container"),
			new MigRow("1. Allgemein", "Auch im Raid sortieren", "Sort in raid too"),
			new MigRow("2. Lage", "Versatz waagerecht", "Offset horizontal"),
			new MigRow("2. Lage", "Versatz senkrecht", "Offset vertical"),
			new MigRow("2. Lage", "Oben rechts verankern", "Anchor top right"),
			new MigRow("2. Lage", "Ebene", "Level"),
			new MigRow("2. Lage", "Groesse", "Size")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			string s = Section;
			Modul.An = Enabled;
			Modul.Sicherheitsbehaelter = Cfg.Bind(cfg, s, "In secure container", true, "Show the sort button in the secure container.");
			Modul.Rucksack = Cfg.Bind(cfg, s, "In backpack", true, "Show the sort button in the backpack.");
			Modul.Alles = Cfg.Bind(cfg, s, "In every container", false,
				"Pockets, vests, cases and crates get the button too.");
			Modul.ImRaid = Cfg.Bind(cfg, s, "Sort in raid too", true,
				"The game blocks sorting completely during a raid - even its own buttons. Without this\n"
				+ "setting it only says \"is not appropriate for sorting\" when you press the button.\n"
				+ "The block is lifted only for containers that belong to you.");
			Modul.VersatzX = Cfg.Bind(cfg, s, "Offset horizontal", -6f,
				"From the chosen corner. With the anchor on the right, negative values go left.",
				new AcceptableValueRange<float>(-400f, 400f));
			Modul.VersatzY = Cfg.Bind(cfg, s, "Offset vertical", 24f,
				"Positive = up.", new AcceptableValueRange<float>(-400f, 400f));
			Modul.Rechts = Cfg.Bind(cfg, s, "Anchor top right", true, "Off = top left.");
			Modul.Ebene = Cfg.Bind(cfg, s, "Level", 1,
				"Which element the button is attached to. 0 = the grid view itself, 1 = one level up "
				+ "(where the BACKPACK heading sits), 2 = two levels up. The names are written to the log when it is set.",
				new AcceptableValueRange<int>(0, 3));
			Modul.Groesse = Cfg.Bind(cfg, s, "Size", 1f, "1 = like in the stash.",
				new AcceptableValueRange<float>(0.3f, 2f));
		}

		public override void Apply(Harmony h)
		{
			h.PatchAll(typeof(AnsichtPatch));
			h.PatchAll(typeof(ImRaidPatch));
			h.PatchAll(typeof(RaidOperationPatch));
			h.PatchAll(typeof(SuchstandPatch));
		}
	}

	internal static class Modul
	{
		internal static ManualLogSource Log => KaansQoL.Plugin.Log;

		internal static ConfigEntry<bool> An;

		internal static ConfigEntry<bool> Sicherheitsbehaelter;

		internal static ConfigEntry<bool> Rucksack;

		internal static ConfigEntry<bool> Alles;

		internal static ConfigEntry<bool> ImRaid;

		internal static ConfigEntry<float> VersatzX;

		internal static ConfigEntry<float> VersatzY;

		internal static ConfigEntry<bool> Rechts;

		internal static ConfigEntry<int> Ebene;

		internal static ConfigEntry<float> Groesse;
	}

	/// <summary>Markiert unsere eigenen Knoepfe, damit keine zweiten entstehen.</summary>
	internal class UnserKnopf : MonoBehaviour
	{
	}

	[HarmonyPatch]
	internal static class AnsichtPatch
	{
		// Es gibt zwei Show-Methoden: die abstrakte mit sechs Parametern und
		// die ausgefuehrte mit acht. Gesucht ist die zweite - ueber die
		// Parameterzahl statt ueber die Typen, weil einer davon
		// (FilterPanel) in keiner der eingebundenen Assemblies liegt.
		[HarmonyTargetMethod]
		private static MethodBase Ziel()
		{
			foreach (MethodInfo m in typeof(ContainedGridsView).GetMethods(
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				if (m.Name == "Show" && !m.IsAbstract && m.GetParameters().Length == 8)
				{
					Modul.Log.LogInfo("Sortierknopf haengt an " + m.DeclaringType.Name + ".Show");
					return m;
				}
			}
			Modul.Log.LogError("ContainedGridsView.Show mit acht Parametern nicht gefunden.");
			return null;
		}

		private static FieldInfo _steuerungFeld;

		private static bool _gesucht;

		[HarmonyPostfix]
		private static void Danach(ContainedGridsView __instance, CompoundItem compoundItem,
								   EFT.InventoryLogic.ItemController itemController)
		{
			try
			{
				if (!Modul.An.Value || __instance == null || compoundItem == null)
				{
					return;
				}
				if (!Zustaendig(compoundItem))
				{
					return;
				}

				GridSortPanel vorlage = Vorlage(__instance);
				if (vorlage == null)
				{
					Modul.Log.LogWarning("Kein GridSortPanel zum Klonen gefunden - "
						+ "einmal das Lager oeffnen, dann ist es geladen.");
					return;
				}
				Transform eltern = Elternteil(__instance.transform);
				if (eltern == null || eltern.GetComponentInChildren<UnserKnopf>() != null)
				{
					return;
				}

				// Die Steuerung ZUERST holen. Ohne sie waere der Knopf nur
				// Zierde - dann gar nicht erst einen anlegen.
				InventoryController steuerung = itemController as InventoryController;
				if (steuerung == null)
				{
					steuerung = AusFeld(__instance);
				}
				if (steuerung == null)
				{
					Modul.Log.LogWarning("Keine Inventarsteuerung - kein Sortierknopf fuer "
						+ compoundItem.TemplateId);
					return;
				}

				GridSortPanel klon = UnityEngine.Object.Instantiate(vorlage, eltern, worldPositionStays: false);
				try
				{
					klon.gameObject.name = "SortierknopfKaan";
					klon.gameObject.AddComponent<UnserKnopf>();
					Entschaerfen(klon);
					Platzieren(klon.transform as RectTransform, vorlage.transform as RectTransform);
					klon.gameObject.SetActive(value: true);
					Anrichten(klon, steuerung, compoundItem);
					// _canvasGroup ist schon in der Vorlage des Lagers leer -
					// das Spiel ruft SetUnlockStatus dort selbst nie auf. Ohne
					// CanvasGroup ist der Knopf ohnehin voll sichtbar und
					// bedienbar; nur wenn eine da ist, wird sie freigeschaltet.
					if (klon._canvasGroup != null)
					{
						klon.SetUnlockStatus(value: true);
					}
					Modul.Log.LogInfo("Sortierknopf gesetzt an " + compoundItem.TemplateId
						+ " | Kette: " + Kette(__instance.transform));
				}
				catch (Exception ex)
				{
					// Nichts Halbfertiges stehen lassen - sonst haengen
					// verirrte Balken im Bild.
					UnityEngine.Object.Destroy(klon.gameObject);
					Modul.Log.LogError("Sortierknopf-Aufbau gescheitert, Klon entfernt: " + ex);
				}
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Sortierknopf: " + ex);
			}
		}

		/// <summary>Namen der Elternelemente - zum Einstellen der Ebene.</summary>
		private static string Kette(Transform t)
		{
			string s = "";
			int i = 0;
			while (t != null && i < 4)
			{
				s += ((i > 0) ? " < " : "") + i + ":" + t.name;
				t = t.parent;
				i++;
			}
			return s;
		}

		private static FieldInfo _feldSteuerung;

		private static FieldInfo _feldGegenstand;

		private static bool _felderGesucht;

		/// <summary>
		/// Richtet den Klon auf Behaelter und Steuerung aus - OHNE Show().
		///
		/// An GridSortPanel.Show haengt der Mod ChouUn.Iof einen Postfix.
		/// Der erwartet ein Panel, das in der UI-Struktur des Spiels steckt,
		/// und greift bei unserem frisch erzeugten Klon ins Leere. Ergebnis
		/// war die Meldung "ChouUn.Iof threw an exception" im Spiel.
		///
		/// Show() selbst tut nichts weiter, als zwei Felder zu setzen - das
		/// koennen wir direkt, dann laeuft dort auch kein fremder Postfix.
		/// </summary>
		private static void Anrichten(GridSortPanel klon, InventoryController steuerung, CompoundItem item)
		{
			if (!_felderGesucht)
			{
				_felderGesucht = true;
				foreach (FieldInfo f in typeof(GridSortPanel).GetFields(
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (_feldSteuerung == null && typeof(InventoryController).IsAssignableFrom(f.FieldType))
					{
						_feldSteuerung = f;
					}
					else if (_feldGegenstand == null && typeof(CompoundItem).IsAssignableFrom(f.FieldType))
					{
						_feldGegenstand = f;
					}
				}
				Modul.Log.LogInfo("Sortierknopf: Felder "
					+ ((_feldSteuerung != null) ? _feldSteuerung.Name : "?") + " / "
					+ ((_feldGegenstand != null) ? _feldGegenstand.Name : "?"));
			}
			if (_feldSteuerung != null && _feldGegenstand != null)
			{
				_feldSteuerung.SetValue(klon, steuerung);
				_feldGegenstand.SetValue(klon, item);
				return;
			}
			// Rueckfallebene. Der Rumpf von Show laeuft dabei durch, nur der
			// fremde Postfix danach kann stolpern - das ist zu verschmerzen.
			try
			{
				klon.Show(steuerung, item);
			}
			catch (Exception ex)
			{
				Modul.Log.LogWarning("Show() hat einen fremden Postfix gestoert: " + ex.Message);
			}
		}

		private static bool Zustaendig(CompoundItem item)
		{
			if (Modul.Alles.Value)
			{
				return true;
			}
			try
			{
				string slot = item.CurrentAddress?.Container?.ID;
				if (Modul.Sicherheitsbehaelter.Value && slot == "SecuredContainer")
				{
					return true;
				}
				if (Modul.Rucksack.Value && slot == "Backpack")
				{
					return true;
				}
			}
			catch
			{
			}
			return false;
		}

		/// <summary>
		/// Irgendein bereits geladenes GridSortPanel als Vorlage - das des
		/// Lagers. FindObjectsOfTypeAll findet auch ausgeblendete.
		/// </summary>
		private static GridSortPanel Vorlage(ContainedGridsView ansicht)
		{
			GridSortPanel[] alle = Resources.FindObjectsOfTypeAll<GridSortPanel>();
			for (int i = 0; i < alle.Length; i++)
			{
				GridSortPanel p = alle[i];
				if (p == null || p.GetComponent<UnserKnopf>() != null)
				{
					continue;
				}
				if (ansicht != null && p.transform.IsChildOf(ansicht.transform))
				{
					continue;
				}
				return p;
			}
			return null;
		}

		/// <summary>
		/// Nimmt dem Klon alles, womit er Eingaben abfangen koennte.
		///
		/// Das GridSortPanel bringt ein Lade-Overlay mit (_loaderPanel), das
		/// waehrend des Sortierens ueber die Flaeche gelegt wird. Im Klon
		/// wird es nie wieder abgeschaltet - es liegt also unsichtbar im Weg
		/// und schluckt Mausrad und Klicks. Genau das hat das Scrollen in
		/// den Kisten blockiert.
		///
		/// Zusaetzlich wird JEDE Grafik ausser der des Knopfes selbst von der
		/// Eingabe abgemeldet. Danach kann der Klon nur noch dort etwas
		/// abfangen, wo wirklich der Knopf sitzt.
		/// </summary>
		private static void Entschaerfen(GridSortPanel klon)
		{
			try
			{
				foreach (FieldInfo f in typeof(GridSortPanel).GetFields(
					BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
				{
					if (typeof(GameObject).IsAssignableFrom(f.FieldType))
					{
						GameObject go = f.GetValue(klon) as GameObject;
						if (go != null && go != klon.gameObject)
						{
							go.SetActive(value: false);
							Modul.Log.LogInfo("Sortierknopf: " + f.Name + " abgeschaltet.");
						}
					}
				}
			}
			catch (Exception ex)
			{
				Modul.Log.LogWarning("Overlay nicht abschaltbar: " + ex.Message);
			}
			try
			{
				Graphic behalten = null;
				Button knopf = klon.GetComponentInChildren<Button>(includeInactive: true);
				if (knopf != null)
				{
					behalten = knopf.targetGraphic;
				}
				Graphic[] alle = klon.GetComponentsInChildren<Graphic>(includeInactive: true);
				int aus = 0;
				for (int i = 0; i < alle.Length; i++)
				{
					if (alle[i] != null && alle[i] != behalten && alle[i].raycastTarget)
					{
						alle[i].raycastTarget = false;
						aus++;
					}
				}
				if (aus > 0)
				{
					Modul.Log.LogInfo("Sortierknopf: " + aus + " Grafiken von der Eingabe abgemeldet.");
				}
			}
			catch (Exception ex)
			{
				Modul.Log.LogWarning("Raycast nicht abschaltbar: " + ex.Message);
			}
		}

		private static void Platzieren(RectTransform rt, RectTransform vorlage)
		{
			if (rt == null)
			{
				return;
			}
			// In eine ECKE haengen und die Groesse der Vorlage uebernehmen.
			// Ohne das behaelt der Klon die Streckung seines urspruenglichen
			// Platzes im Lager - daher die langen Balken quer ueber den
			// Bildschirm.
			float x = (Modul.Rechts.Value ? 1f : 0f);
			rt.anchorMin = new Vector2(x, 1f);
			rt.anchorMax = new Vector2(x, 1f);
			rt.pivot = new Vector2(x, 1f);
			if (vorlage != null && vorlage.rect.width > 1f && vorlage.rect.height > 1f)
			{
				rt.sizeDelta = new Vector2(vorlage.rect.width, vorlage.rect.height);
			}
			else if (rt.sizeDelta.x < 1f || rt.sizeDelta.y < 1f)
			{
				rt.sizeDelta = new Vector2(28f, 28f);
			}
			rt.offsetMin = new Vector2(rt.offsetMin.x, rt.offsetMin.y);
			rt.anchoredPosition = new Vector2(Modul.VersatzX.Value, Modul.VersatzY.Value);
			rt.localScale = Vector3.one * Modul.Groesse.Value;
			rt.localRotation = Quaternion.identity;

			// Sitzt der Knopf in einem Element mit automatischem Layout,
			// wuerde es ihn sonst sofort wieder woanders hinschieben.
			LayoutElement le = rt.GetComponent<LayoutElement>();
			if (le == null)
			{
				le = rt.gameObject.AddComponent<LayoutElement>();
			}
			le.ignoreLayout = true;
		}

		/// <summary>
		/// Das Element, an das gehaengt wird. Eine Ebene ueber der
		/// Rasteransicht sitzt der Bereich mit der Ueberschrift.
		/// </summary>
		private static Transform Elternteil(Transform ansicht)
		{
			Transform t = ansicht;
			for (int i = 0; i < Modul.Ebene.Value; i++)
			{
				if (t.parent == null)
				{
					break;
				}
				t = t.parent;
			}
			return t;
		}

		private static InventoryController AusFeld(ContainedGridsView ansicht)
		{
			try
			{
				if (!_gesucht)
				{
					_gesucht = true;
					for (Type t = ansicht.GetType(); t != null && _steuerungFeld == null; t = t.BaseType)
					{
						foreach (FieldInfo f in t.GetFields(BindingFlags.Instance
							| BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
						{
							if (typeof(InventoryController).IsAssignableFrom(f.FieldType))
							{
								_steuerungFeld = f;
								break;
							}
						}
					}
				}
				return _steuerungFeld?.GetValue(ansicht) as InventoryController;
			}
			catch
			{
				return null;
			}
		}
	}

	// ---------------------------------------------------------------
	//  Sortieren im Raid freigeben
	//
	//  Im Raid laesst das Spiel ueberhaupt nicht sortieren - auch
	//  seine eigenen Knoepfe nicht. Der Grund steht in
	//  EFT.InventoryLogic.InventoryController:
	//
	//      public virtual bool IsAllowedToSort(CompoundItem item)
	//      {
	//          return false;
	//      }
	//
	//  Nur BackEndInventoryController - die Steuerung von Menue und
	//  Lager - ueberschreibt das und erlaubt es, wenn der Behaelter
	//  ihr selbst gehoert:
	//
	//      return item.CurrentAddress?.GetOwner() == this;
	//
	//  Im Raid haengt die Spielfigur aber an
	//  SinglePlayerInventoryController. Der erbt ueber
	//  PlayerInventoryController die Fassung, die immer nein sagt -
	//  daher die Meldung "... is not appropriate for sorting".
	//
	//  Hier wird dieselbe Regel angewandt, die BSG im Menue benutzt.
	//  Die Ueberschreibung im BackEndInventoryController bleibt
	//  unberuehrt: Harmony patcht genau eine Methode, und die
	//  ueberschriebene Fassung ist eine andere.
	//
	//  Eingegrenzt auf PlayerInventoryController, damit fremde
	//  Spielerkoerper (ObservedPlayerInventoryController, ebenfalls
	//  direkt von InventoryController abgeleitet) aussen vor
	//  bleiben.
	// ---------------------------------------------------------------
	[HarmonyPatch]
	internal static class ImRaidPatch
	{
		[HarmonyTargetMethod]
		private static MethodBase Ziel()
		{
			return AccessTools.Method(typeof(InventoryController),
				nameof(InventoryController.IsAllowedToSort));
		}

		[HarmonyPostfix]
		private static void Danach(InventoryController __instance, CompoundItem item, ref bool __result)
		{
			try
			{
				// Ein Ja des Spiels nie in ein Nein verwandeln.
				if (__result || Modul.ImRaid == null || !Modul.ImRaid.Value)
				{
					return;
				}
				if (item == null || !(__instance is Player.PlayerInventoryController))
				{
					return;
				}

				// Dieselbe Bedingung wie im Menue: der Behaelter muss
				// dieser Steuerung selbst gehoeren. GetOwnerOrNull statt
				// GetOwner - die werfende Fassung wuerde bei einem gerade
				// abgelegten Gegenstand eine Ausnahme ausloesen.
				__result = item.CurrentAddress.GetOwnerOrNull() == __instance;
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Sortierknopf: Raid-Freigabe - " + ex);
			}
		}
	}

	// ---------------------------------------------------------------
	//  Sortieren im Raid: die fehlende Operation
	//
	//  Die Freigabe oben reicht allein nicht. Der Sortierknopf baut ein
	//  ApplySortItemsPositionResult und gibt es an
	//  TryRunNetworkTransaction. Dort wird daraus eine Operation:
	//
	//    - Im Menue macht BackEndInventoryController
	//      .ConvertOperationResultToOperation daraus eine
	//      ApplyInventoryChangesOperation. Die legt die Gegenstaende um
	//      und meldet beim Abschluss (Dispose) alle Ereignisse - erst
	//      dadurch baut sich die offene Rasteransicht neu auf.
	//    - Im Raid laeuft die Grundfassung in ItemController. Die kennt
	//      das Sortieren nicht und wirft
	//        "operationResult is of unexpected type
	//         ApplySortItemsPositionResult"
	//      (steht in Logs/.../errors.log).
	//
	//  Folgen der Ausnahme im Raid:
	//    - Ohne fremde Mods: es wird gar nicht sortiert, und der Knopf
	//      bleibt bis zum Schliessen "beschaeftigt" (ChangeProgress(false)
	//      wird nie erreicht).
	//    - Mit UIFixes ("Stack before sort"): UIFixes sortiert mit
	//      simulate:false, die Gegenstaende liegen also schon um - aber
	//      kein einziges Ereignis wird gemeldet. Die Ansicht bleibt alt,
	//      der Suchstand behaelt die alten Plaetze, und nach dem
	//      Wiederoeffnen erscheinen die verschobenen Gegenstaende als
	//      schraffierte "undurchsuchte" Felder.
	//
	//  Abhilfe: im Raid dieselbe Operation verwenden wie im Menue.
	//  ApplyInventoryChangesOperation arbeitet rein lokal (Entfernen +
	//  Ablegen); nur ToDescriptor() waere fuer echtes Online-Spiel
	//  noetig, und das ruft der Einzelspieler-Raid nie auf.
	//  Eingegrenzt wie die Freigabe: nur die Steuerung der eigenen
	//  Spielfigur und nur, wenn sie diesen Behaelter sortieren darf
	//  (also nie Kisten oder Koerper). Das Menue erreicht diesen Patch
	//  nie - BackEndInventoryController erledigt das Sortieren vorher
	//  selbst.
	// ---------------------------------------------------------------
	[HarmonyPatch]
	internal static class RaidOperationPatch
	{
		[HarmonyTargetMethod]
		private static MethodBase Ziel()
		{
			return AccessTools.Method(typeof(ItemController),
				nameof(ItemController.ConvertOperationResultToOperation));
		}

		[HarmonyPrefix]
		private static bool Vorher(ItemController __instance, IOperationResult operationResult,
								   ref EFT.InventoryLogic.Operations.AbstractOperation __result)
		{
			try
			{
				if (!(operationResult is ApplySortItemsPositionResult sortieren)
					|| Modul.ImRaid == null || !Modul.ImRaid.Value)
				{
					return true;
				}
				if (!(__instance is Player.PlayerInventoryController steuerung)
					|| sortieren._sortedItem == null
					|| !steuerung.IsAllowedToSort(sortieren._sortedItem))
				{
					return true;
				}
				__result = new EFT.InventoryLogic.Operations.ApplyInventoryChangesOperation(
					steuerung.GetAndIncrementNextOperationId(), steuerung, sortieren);
				return false;
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Sortierknopf: Raid-Operation - " + ex);
				return true;
			}
		}
	}

	// ---------------------------------------------------------------
	//  Sortieren im Raid: Gegenstaende bleiben "bekannt"
	//
	//  Im Raid ist jeder Behaelter des Spielers durchsuchbar
	//  (ISearchableContainer). Der Suchstand haengt an Gegenstand UND
	//  genauer Stelle: ActiveSearchController.IsItemKnown vergleicht den
	//  gemerkten Platz (_knownItems[item]) mit der aktuellen Adresse.
	//  Ein gewoehnliches Verschieben (AddResult.RaiseEvents) traegt den
	//  neuen Platz nach - das Sortieren nicht: ItemManipulator.Sort legt
	//  die Gegenstaende ueber Grid.AddAnywhere (GridAddResult) neu ab,
	//  und ApplySortItemsPositionResult.RaiseEvents meldet nur
	//  Entfernen/Hinzufuegen. Alles, was beim Sortieren den Platz
	//  wechselt, gilt danach als undurchsucht und wird schraffiert
	//  gezeigt, bis man "Durchsuchen" drueckt.
	//
	//  Das Spiel selbst kennt das Problem nicht, weil es im Raid gar
	//  nicht sortieren laesst - erst ImRaidPatch und RaidOperationPatch
	//  oben machen es moeglich (RaiseEvents laeuft im Raid ueber
	//  ApplyInventoryChangesOperation.Dispose).
	//  Darum hier: Was vor dem Sortieren bekannt war, ist es am neuen
	//  Platz auch. Laeuft VOR dem Melden der Ereignisse, damit die
	//  Ansichten gleich als bekannte Gegenstaende entstehen. Ausserhalb
	//  des Raids ist kein Fach durchsuchbar, dann passiert nichts.
	// ---------------------------------------------------------------
	[HarmonyPatch(typeof(ApplySortItemsPositionResult), nameof(ApplySortItemsPositionResult.RaiseEvents))]
	internal static class SuchstandPatch
	{
		[HarmonyPrefix]
		private static void Vorher(ApplySortItemsPositionResult __instance, CommandStatus status)
		{
			try
			{
				if (status != CommandStatus.Succeed)
				{
					return;
				}
				IPlayerSearchController suche = __instance._inventoryController?.SearchController as IPlayerSearchController;
				if (suche == null || __instance._removeResults == null || __instance._sortResults == null)
				{
					return;
				}
				Dictionary<Item, ItemAddress> vorher = new Dictionary<Item, ItemAddress>();
				foreach (ContainerRemoveResult r in __instance._removeResults)
				{
					if (r?.Item != null && r.Address != null)
					{
						vorher[r.Item] = r.Address;
					}
				}
				int nachgetragen = 0;
				foreach (GridAddResult a in __instance._sortResults)
				{
					Item item = a?.Item;
					if (item == null || item.Parent == null || !vorher.TryGetValue(item, out ItemAddress alt))
					{
						continue;
					}
					// Was vorher unbekannt war, bleibt es - Sortieren ist
					// kein Durchsuchen.
					if (!suche.IsItemKnown(item, alt) || suche.IsItemKnown(item, item.Parent))
					{
						continue;
					}
					suche.SetItemAsKnown(item, raiseEvents: false);
					nachgetragen++;
				}
				if (nachgetragen > 0)
				{
					Modul.Log.LogInfo("Sortierknopf: " + nachgetragen + " Gegenstaende nach dem Sortieren als bekannt nachgetragen.");
				}
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Sortierknopf: Suchstand nach dem Sortieren - " + ex);
			}
		}
	}
}
