// Pfeil-Spitze
//
// DynamicMaps (UI/Components/MapMarker.cs) baut jeden Marker so:
//   Wurzel "MapMarker <Name>"  (30x30, Pivot 0.5/0.5, anchoredPosition = Weltposition)
//     Kind "image"  (30x30, Pivot 0.5/0.5, Anker Mitte)  <- Sprite, wird per
//                    SetRotation() um SEINE Mitte gedreht (localRotation)
//     Kind "label"  (Anker unten-mitte der Wurzel, Pivot oben-mitte)
// Der Pfeil (Markers/arrow.png, 60x60, Spitze oben, erste deckende Zeile = 3)
// liegt also mit seiner MITTE auf der Position und schwenkt beim Drehen um die
// Mitte. Die Spitze ist ~14 px (bei 30 px Marker) daneben.
//
// Fix an der Ursache: Pivot des Bild-Kindes auf die Spitze (0.5 / 0.95).
// Das Kind sitzt weiter auf der Wurzelmitte (= Position); weil Unity um den
// Pivot dreht UND positioniert, liegt die Spitze exakt auf der Position und
// der Pfeil dreht sich um die Spitze. Betroffen: PlayerMapMarker mit
// Bildpfad "Markers/arrow.png" (eigener Spieler, Freunde, Feinde, Scavs;
// Bosse = star.png bleiben unangetastet).
// Das Label haengt an der Wurzel und wuerde jetzt im Pfeilkoerper liegen
// (Pfeil reicht 30 px unter die Position) -> es wird um eine Markergroesse nach
// unten geschoben, so dass es bei jeder Drehung unter dem Pfeil steht.

using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace KaansQoL.PfeilSpitze
{
	internal sealed class PfeilSpitzeModule : QolModule
	{
		public override string Id => "PfeilSpitze";

		public override string Section => "12 Map Arrow Tip";

		public override string Beschreibung =>
			"DynamicMaps arrows point with their tip at the position (and rotate around the tip) instead of around the arrow's centre. "
			+ "Needs DynamicMaps.";

		public override string AlteGuid => "com.kaan.pfeilspitze";

		public override string AlterName => "PfeilSpitze";

		public override string[] BenoetigtPlugins => new[] { Modul.DynamicMapsGuid };

		public override MigRow[] Umzug => new[]
		{
			new MigRow("1. Allgemein", "Aktiv", "Enabled"),
			new MigRow("1. Allgemein", "Spitzen-Versatz", "Tip offset")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			Modul.Aktiv = Enabled;
			Modul.SpitzeY = Cfg.Bind(cfg, Section, "Tip offset", 0.95f,
				"Where the tip lies in the arrow image (0 = bottom edge, 1 = top edge). Default 0.95: the tip starts "
				+ "3 of 60 pixels below the edge. For fine-tuning.",
				new AcceptableValueRange<float>(0.5f, 1f));
		}

		public override void Apply(Harmony h)
		{
			Verbinden(h);
		}

		public override void Entfernt()
		{
			// Pfeile zurueck auf die Mitte (Aktiv ist jetzt aus).
			Zuruecksetzen();
		}

		// Eigene Methoden: Typen aus DynamicMaps.dll werden erst hier geladen,
		// also nie, wenn DynamicMaps fehlt.
		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void Verbinden(Harmony h)
		{
			h.PatchAll(typeof(Patches));
			Patches.AllesNeuAnwenden();
			if (!Modul.Verbunden)
			{
				Modul.Verbunden = true;
				Modul.SpitzeY.SettingChanged += (s, e) => Patches.AllesNeuAnwenden();
			}
		}

		[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
		private static void Zuruecksetzen()
		{
			Patches.AllesNeuAnwenden();
		}
	}

	internal static class Modul
	{
		internal const string DynamicMapsGuid = "com.mpstark.dynamicmaps";

		internal static ManualLogSource Log => KaansQoL.Plugin.Log;

		internal static ConfigEntry<bool> Aktiv;

		internal static ConfigEntry<float> SpitzeY;

		internal static bool Verbunden;
	}

	// Eigene Klasse: Typen aus DynamicMaps.dll werden erst beim PatchAll geladen,
	// also nie, wenn DynamicMaps fehlt.
	internal static class Patches
	{
		private static readonly List<DynamicMaps.UI.Components.PlayerMapMarker> Pfeile =
			new List<DynamicMaps.UI.Components.PlayerMapMarker>();

		[HarmonyPatch(typeof(DynamicMaps.UI.Components.PlayerMapMarker),
			nameof(DynamicMaps.UI.Components.PlayerMapMarker.Create))]
		[HarmonyPostfix]
		private static void NachCreate(DynamicMaps.UI.Components.PlayerMapMarker __result, string imagePath)
		{
			try
			{
				if (__result == null || imagePath == null || !imagePath.EndsWith("arrow.png", StringComparison.OrdinalIgnoreCase))
					return;
				Pfeile.Add(__result);
				Anwenden(__result);
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Pfeil-Spitze: " + ex);
			}
		}

		internal static void AllesNeuAnwenden()
		{
			Pfeile.RemoveAll(m => m == null);
			foreach (var m in Pfeile) Anwenden(m);
		}

		private static void Anwenden(DynamicMaps.UI.Components.PlayerMapMarker m)
		{
			var bild = m.Image != null ? m.Image.rectTransform : null;
			if (bild == null) return;
			bild.pivot = new Vector2(0.5f, Modul.Aktiv.Value ? Modul.SpitzeY.Value : 0.5f);

			// Label unter den Pfeil (Wurzel-Unterkante liegt 0.5 Groesse unter der Position)
			if (m.Label != null)
			{
				var l = m.Label.rectTransform;
				l.anchoredPosition = Modul.Aktiv.Value ? new Vector2(0f, -m.RectTransform.sizeDelta.y * 0.5f) : Vector2.zero;
			}
		}
	}
}
