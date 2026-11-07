using System;
using System.Linq;
using System.Reflection;
using BeltSlot.Helpers;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.InventoryLogic;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;

namespace KaansQoL.GuertelFix
{
	internal sealed class GuertelFixModule : QolModule
	{
		public override string Id => "GuertelFix";

		public override string Section => "11 Belt Slot Fix";

		public override string Beschreibung =>
			"Fixes the corpse view of the Pack 'n' Strap belt slot mod (Trenchfoot-BeltSlot): "
			+ "stale loot windows from the previous body no longer shine through. Needs Trenchfoot-BeltSlot.";

		public override string AlteGuid => "com.kaan.guertelfix";

		public override string AlterName => "GuertelFix";

		public override string[] BenoetigtPlugins => new[] { "com.trenchfoot.beltslot" };

		// Die alte Mod hatte keine Einstellungen - nichts zu uebernehmen.

		protected override void BindSettings(ConfigFile cfg)
		{
		}

		public override void Apply(Harmony h)
		{
			Modul.Anwenden(h);
		}
	}

	// Repariert die Leichenansicht der Guertel-Mod aus Pack 'n' Strap
	// (Trenchfoot-BeltSlot 2.1.1).
	//
	// Fehlerbild: Beim naechsten Koerper erscheint das alte Beutefenster mit
	// falschem Inhalt, Reste scheinen durch.
	//
	// Ursache in BeltSlot:
	//  1. UI_Mappings.getComplexLootUI_Mappings sucht mit transform.Find nach
	//     "Gear Panel Template(Clone)" und "ArmBand Slot". Find liefert das
	//     ERSTE passende Kind - liegt die Ansicht der vorigen Leiche noch im
	//     Baum, greift die Mod deren Objekte.
	//  2. Beim Schliessen wird nur ComplexStashPanelLoaded zurueckgesetzt, die
	//     Verweise LootArmbandSlot/lootArmBand/lootBeltSlot bleiben stehen.
	//  3. ItemView.Update ruft in jedem Bild UpdateLootArmBandSlot auf und
	//     schaltet per SetActive Teile dieser (alten) Objekte an und aus.
	//
	// Dieser Patch nimmt immer die neueste sichtbare Ansicht und loescht die
	// Verweise beim Schliessen. BeltSlot selbst bleibt unveraendert.
	// Das Modul selbst (GuertelFixModule) kennt keine BeltSlot-Typen; erst diese
	// Klasse greift darauf zu - und wird nur angefasst, wenn BeltSlot geladen ist.
	internal static class Modul
	{
		internal static ManualLogSource Log => KaansQoL.Plugin.Log;

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		internal static void Anwenden(Harmony h)
		{
			h.Patch(AccessTools.Method(typeof(UI_Mappings), nameof(UI_Mappings.getComplexLootUI_Mappings)),
				prefix: new HarmonyMethod(typeof(Modul), nameof(ZuordnungNeu)));
			h.Patch(AccessTools.Method(typeof(ComplexStashPanel), "Close"),
				postfix: new HarmonyMethod(typeof(Modul), nameof(NachSchliessen)));
			Log.LogInfo("[GuertelFix] aktiv.");
		}

		// Letztes sichtbares direktes Kind mit diesem Namen.
		private static Transform NeuestesKind(Transform eltern, string name, out int anzahl)
		{
			Transform treffer = null;
			anzahl = 0;
			for (int i = 0; i < eltern.childCount; i++)
			{
				Transform k = eltern.GetChild(i);
				if (k.name != name) continue;
				anzahl++;
				if (k.gameObject.activeInHierarchy) treffer = k;
			}
			return treffer;
		}

		// Ersetzt getComplexLootUI_Mappings vollstaendig (return false).
		private static bool ZuordnungNeu(UI_Mappings __instance, ref Slot __result)
		{
			__result = null;
			try
			{
				if (__instance.inventoryScreen == null)
				{
					__instance.inventoryScreen = Comfort.Common.Singleton<EFT.UI.CommonUI>.Instance.InventoryScreen;
				}
				Transform inhalt = __instance.inventoryScreen.transform
					.Find("Items Panel/Stash Panel/Complex Loot Panel/Containers Scrollview/Content");
				if (inhalt == null || __instance.countTransformChildren(inhalt.gameObject) < 5)
				{
					BeltSlot.Plugin.Instance.ComplexStashPanelLoaded = false;
					return false;
				}
				Transform ausruestung = NeuestesKind(inhalt, "Gear Panel Template(Clone)", out int nAusr);
				Transform guertel = NeuestesKind(inhalt, "ArmBand Slot", out int nGuert);
				if (nAusr > 1 || nGuert > 1)
				{
					Log.LogWarning($"[GuertelFix] Alte Leichenansicht noch im Baum: {nAusr}x Ausruestung, {nGuert}x Guertel - nehme die neueste.");
				}
				Transform armband = ausruestung != null ? ausruestung.Find("ArmBand Slot") : null;
				if (ausruestung == null || guertel == null || armband == null)
				{
					BeltSlot.Plugin.Instance.ComplexStashPanelLoaded = false;
					return false;
				}
				__instance.lootContainerGameObject = inhalt.gameObject;
				__instance.lootEquipmentGameObject = ausruestung.gameObject;
				__instance.lootArmBand = armband.gameObject;
				__instance.lootBeltSlot = guertel.gameObject;
				__instance.setBeltSlot_Settings(__instance.lootBeltSlot);
				__result = __instance.lootBeltSlot.GetComponent<SlotView>()?.Slot;
			}
			catch (Exception ex)
			{
				Log.LogError("[GuertelFix] Zuordnung fehlgeschlagen: " + ex);
				BeltSlot.Plugin.Instance.ComplexStashPanelLoaded = false;
				__result = null;
			}
			return false;
		}

		// Beim Schliessen der Leichenansicht alle Verweise auf sie loesen,
		// damit ItemView.Update nichts mehr an ihr umschaltet.
		private static void NachSchliessen()
		{
			try
			{
				BeltSlot.Plugin p = BeltSlot.Plugin.Instance;
				if (p == null) return;
				p.ComplexStashPanelLoaded = false;
				p.LootArmbandSlot = null;
				p._lootArmbandItemId = null;
				UI_Mappings m = BeltSlot.Plugin.UiMappings;
				if (m != null)
				{
					m.lootArmBand = null;
					m.lootBeltSlot = null;
					m.lootEquipmentGameObject = null;
				}
			}
			catch (Exception ex)
			{
				Log.LogError("[GuertelFix] Aufraeumen fehlgeschlagen: " + ex);
			}
		}
	}
}
