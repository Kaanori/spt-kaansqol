using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using EFT.Weather;
using HarmonyLib;
using UnityEngine;

namespace KaansQoL.Windstille
{
	// Das Spiel klemmt die Ziellautstaerke der Windschleife hart auf
	// mindestens 0,2 fest (WindAmbientBlender.method_7 ->
	// Mathf.Clamp(..., 0.2f, 1f)). Bei Windstaerke 0 rauscht es deshalb
	// weiter. Dieses Modul zieht die Lautstaerke nach dem Takt des
	// Blenders wieder herunter - abhaengig vom echten Windwert.
	internal sealed class WindstilleModule : QolModule
	{
		public override string Id => "Windstille";

		public override string Section => "06 Calm Wind";

		public override string Beschreibung => "Dampens the wind ambience loop.";

		public override string AlteGuid => "com.kaan.windstille";

		public override string AlterName => "Windstille";

		public override MigRow[] Umzug => new[]
		{
			new MigRow("1. Allgemein", "Eingeschaltet", "Enabled"),
			new MigRow("2. Werte", "Schwelle fuer Flaute", "Calm threshold"),
			new MigRow("2. Werte", "Lautstaerke bei Flaute", "Volume when calm"),
			new MigRow("2. Werte", "Lautstaerke bei Wind", "Volume when windy"),
			new MigRow("3. Anzeige", "Ins Protokoll schreiben", "Write log")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			string s = Section;
			Modul.Eingeschaltet = Enabled;
			Modul.Schwelle = Cfg.Bind(cfg, s, "Calm threshold", 0.15f,
				"Up to which wind strength it counts as calm. The game's wind value runs from 0 to 1.",
				new AcceptableValueRange<float>(0f, 1f));
			Modul.LautstaerkeFlaute = Cfg.Bind(cfg, s, "Volume when calm", 0f,
				"0 = completely silent. The game itself never goes below 0.2.",
				new AcceptableValueRange<float>(0f, 1f));
			Modul.LautstaerkeWind = Cfg.Bind(cfg, s, "Volume when windy", 1f,
				"Upper limit when the wind is actually blowing. 1 = unchanged.",
				new AcceptableValueRange<float>(0f, 1f));
			Modul.Protokoll = Cfg.Bind(cfg, s, "Write log", false,
				"Writes the calculated volume to the log. For troubleshooting only.");
		}

		public override void Apply(Harmony h)
		{
			Type blender = AccessTools.TypeByName("Audio.AmbientSubsystem.WindAmbientBlender");
			if (blender == null)
			{
				Plugin.Log.LogError("[Windstille] Klasse WindAmbientBlender nicht gefunden - das Modul tut nichts.");
				return;
			}
			Modul.FeldQuelle = AccessTools.Field(blender, "_windSource");
			Modul.FeldMischQuelle = AccessTools.Field(blender, "_windMixSource");
			MethodInfo takt = AccessTools.Method(blender, "ManualUpdate");
			if (Modul.FeldQuelle == null || takt == null)
			{
				Plugin.Log.LogError("[Windstille] _windSource oder ManualUpdate fehlen - EFT hat die Klasse geaendert.");
				return;
			}
			h.Patch(takt, postfix: new HarmonyMethod(typeof(Daempfer), nameof(Daempfer.Nachher)));
		}
	}

	internal static class Modul
	{
		internal static ConfigEntry<bool> Eingeschaltet;
		internal static ConfigEntry<float> Schwelle;
		internal static ConfigEntry<float> LautstaerkeFlaute;
		internal static ConfigEntry<float> LautstaerkeWind;
		internal static ConfigEntry<bool> Protokoll;

		internal static FieldInfo FeldQuelle;
		internal static FieldInfo FeldMischQuelle;
	}

	internal static class Daempfer
	{
		private static float _zuletzt = -1f;

		public static void Nachher(object __instance)
		{
			if (!Modul.Eingeschaltet.Value || __instance == null)
			{
				return;
			}
			try
			{
				float staerke = 0f;
				WeatherController w = WeatherController.Instance;
				if (w != null)
				{
					staerke = w.WeatherCurve.Wind.magnitude;
				}

				float grenze;
				float schwelle = Modul.Schwelle.Value;
				if (staerke <= schwelle)
				{
					grenze = Modul.LautstaerkeFlaute.Value;
				}
				else
				{
					// ab der Schwelle sanft auf die volle Lautstaerke hochziehen,
					// damit es keinen hoerbaren Sprung gibt
					float anteil = (schwelle >= 1f) ? 1f : ((staerke - schwelle) / (1f - schwelle));
					grenze = Mathf.Lerp(Modul.LautstaerkeFlaute.Value, Modul.LautstaerkeWind.Value,
						Mathf.Clamp01(anteil));
				}

				// Nur deckeln, nie hochsetzen und nie multiplizieren - sonst
				// wuerde sich der Wert Bild fuer Bild gegen null schaukeln.
				Deckeln(Modul.FeldQuelle, __instance, grenze);
				Deckeln(Modul.FeldMischQuelle, __instance, grenze);

				if (Modul.Protokoll.Value && Mathf.Abs(grenze - _zuletzt) > 0.01f)
				{
					_zuletzt = grenze;
					Debug.Log($"[Windstille] Windstaerke {staerke:0.00} -> Obergrenze {grenze:0.00}");
				}
			}
			catch
			{
			}
		}

		private static void Deckeln(FieldInfo feld, object halter, float grenze)
		{
			if (feld == null)
			{
				return;
			}
			AudioSource quelle = feld.GetValue(halter) as AudioSource;
			if (quelle != null && quelle.volume > grenze)
			{
				quelle.volume = grenze;
			}
		}
	}
}
