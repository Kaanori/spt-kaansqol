// ---------------------------------------------------------------
//  Sicherheitsbehaelter ohne Gewicht
//
//  Das Tragegewicht laeuft im Spiel ueber genau EINE Stelle:
//      Inventory.TotalWeight = () => Equipment.GetEquipmentWeight()
//  Dort wird ueber alle Ausruestungsplaetze summiert. Ein Postfix
//  zieht anschliessend den Inhalt des Sicherheitsbehaelters wieder
//  ab - der Behaelter selbst wiegt weiter.
//
//  Warum "TotalWeight - Weight" und keine eigene Summe ueber den
//  Inhalt: so wird genau der Betrag abgezogen, den das Spiel vorher
//  aufaddiert hat. Rechnet das Spiel den Inhalt gar nicht mit, ist
//  die Differenz null und es passiert nichts - eine doppelte
//  Abrechnung ist damit ausgeschlossen.
// ---------------------------------------------------------------
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.InventoryLogic;
using HarmonyLib;

namespace KaansQoL.BehaelterOhneGewicht
{
	internal sealed class BehaelterOhneGewichtModule : QolModule
	{
		public override string Id => "BehaelterOhneGewicht";

		public override string Section => "05 Weightless Secure Container";

		public override string Beschreibung =>
			"The contents of the secure container no longer count towards your carry weight. The container itself still weighs something.";

		public override string AlteGuid => "com.kaan.behaelterohnegewicht";

		public override string AlterName => "BehaelterOhneGewicht";

		public override MigRow[] Umzug => new[]
		{
			new MigRow("1. Allgemein", "Eingeschaltet", "Enabled"),
			new MigRow("9. Debug", "Abzug ins Protokoll schreiben", "Log the deduction")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			Modul.An = Enabled;
			Modul.Protokoll = Cfg.Bind(cfg, Section, "Log the deduction", false,
				"Writes to the log once per change how much was deducted.");
		}

		public override void Apply(Harmony h)
		{
			h.PatchAll(typeof(GewichtPatch));
			h.PatchAll(typeof(GewichtElitePatch));
		}
	}

	internal static class Modul
	{
		internal static ManualLogSource Log => KaansQoL.Plugin.Log;

		internal static ConfigEntry<bool> An;

		internal static ConfigEntry<bool> Protokoll;
	}

	internal static class Abzug
	{
		private static float _zuletzt = -1f;

		/// <summary>
		/// Was das Spiel fuer den Inhalt des Sicherheitsbehaelters
		/// angesetzt hat.
		/// </summary>
		public static float Inhalt(InventoryEquipment ausruestung)
		{
			try
			{
				if (ausruestung == null)
				{
					return 0f;
				}
				Slot fach = ausruestung.GetSlot(EquipmentSlot.SecuredContainer);
				Item behaelter = fach?.ContainedItem;
				if (behaelter == null)
				{
					return 0f;
				}
				float gesamt = behaelter.TotalWeight;
				float eigen = behaelter.Weight;
				float inhalt = gesamt - eigen;
				if (inhalt <= 0f || float.IsNaN(inhalt) || float.IsInfinity(inhalt))
				{
					return 0f;
				}
				if (Modul.Protokoll.Value && Math.Abs(inhalt - _zuletzt) > 0.01f)
				{
					_zuletzt = inhalt;
					Modul.Log.LogInfo($"Abzug: {inhalt:0.00} kg Inhalt "
						+ $"(Behaelter gesamt {gesamt:0.00}, eigen {eigen:0.00})");
				}
				return inhalt;
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Abzug: " + ex.Message);
				return 0f;
			}
		}
	}

	[HarmonyPatch(typeof(InventoryEquipment), "GetEquipmentWeight")]
	internal static class GewichtPatch
	{
		[HarmonyPostfix]
		private static void Danach(InventoryEquipment __instance, ref float __result)
		{
			if (!Modul.An.Value)
			{
				return;
			}
			__result = Math.Max(0f, __result - Abzug.Inhalt(__instance));
		}
	}

	// Der zweite Wert wird fuer den Ausdauer-Bonus der Kraft-Fertigkeit
	// benutzt. Bliebe er unveraendert, waere das Gewicht an einer Stelle
	// abgezogen und an der anderen nicht.
	[HarmonyPatch(typeof(InventoryEquipment), "GetEquipmentWeightEliteSkill")]
	internal static class GewichtElitePatch
	{
		[HarmonyPostfix]
		private static void Danach(InventoryEquipment __instance, ref float __result)
		{
			if (!Modul.An.Value)
			{
				return;
			}
			__result = Math.Max(0f, __result - Abzug.Inhalt(__instance));
		}
	}
}
