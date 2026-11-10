using System;
using System.Diagnostics;
using BepInEx.Configuration;
using Comfort.Common;
using EFT.Settings;
using EFT.Weather;
using HarmonyLib;
using UnityEngine;

namespace KaansQoL.SonneGlatt
{
	// Warum die Sonne springt (EFT 0.16.9, TOD_Sky.UpdateCelestials):
	//   ToDController.Update setzt jedes Bild
	//     TOD_Sky.Light.UpdateInterval = {1; 0,5; 0,1; 0}[Grafik "Schatten"-Stufe]
	//   Bei Low/Medium/High rechnet UpdateCelestials die Sonnenrichtung nur alle
	//   UpdateInterval Sekunden neu und setzt LightTransform.rotation dann hart.
	//   Nur bei Ultra (Stufe 3) gleitet das Spiel selbst per Slerp zwischen den
	//   Zielen. Dazu ist die Spielzeit im Raid auf ganze Spielsekunden gerundet
	//   (GameDateTime.CalculateTaxonomyDate) = 1/7 s bei Zeitfaktor 7.
	//   Jeder Sprung dreht das Schattenraster der Kaskaden: alle Schattenkanten
	//   rastern gleichzeitig neu -> sichtbares "Ticken".
	// Dieses Modul laesst das Spiel weiter in seinem Takt rechnen und fuehrt nur das
	// Sonnenlicht zwischen zwei Zielen mit gleichmaessiger Geschwindigkeit nach
	// (genau wie das Spiel es bei Ultra selbst macht, ein Takt Verzoegerung =
	// bei Medium 0,5 s = 0,015 Grad, unsichtbar).
	internal sealed class SonneGlattModule : QolModule
	{
		public override string Id => "SonneGlatt";

		public override string Section => "16 Smooth Sun";

		public override string Beschreibung => "Moves the sunlight and its shadows smoothly instead of in steps. "
			+ "The game only updates the sun every 1 s / 0.5 s / 0.1 s (Shadows quality Low / Medium / High); "
			+ "only on Ultra it glides by itself. This module glides on all other settings too.";

		protected override void BindSettings(ConfigFile cfg)
		{
			string s = Section;
			Modul.Eingeschaltet = Enabled;
			Modul.Gleiten = Cfg.Bind(cfg, s, "Glide between updates", true,
				"Turns the sunlight a little every frame between two sun updates of the game, at constant speed. "
				+ "This is what removes the visible steps.");
			Modul.Takt = Cfg.Bind(cfg, s, "Sun update interval (s)", -1f,
				"How often the game recalculates the sun position. -1 = keep the game's value "
				+ "(Low 1 s, Medium 0.5 s, High 0.1 s, Ultra 0). Smaller values only shorten the glide delay; "
				+ "below about 0.15 s nothing changes because the raid clock only ticks in whole game seconds.",
				new AcceptableValueRange<float>(-1f, 2f));
			Modul.SprungGrad = Cfg.Bind(cfg, s, "Jump instead of glide above (degrees)", 2f,
				"If the sun moves more than this between two updates (time skip, day/night switch to the moon), "
				+ "it jumps directly instead of gliding.",
				new AcceptableValueRange<float>(0.1f, 30f), erweitert: true);
			Modul.Diagnose = Cfg.Bind(cfg, s, "Diagnostics", false,
				"Every 10 s writes one line to the BepInEx log: sun updates, step size, measured cost per frame.");
		}

		public override void Apply(Harmony h)
		{
			h.Patch(AccessTools.Method(typeof(TOD_Sky), nameof(TOD_Sky.UpdateCelestials)),
				postfix: new HarmonyMethod(typeof(Gleiter), nameof(Gleiter.Nachher)));
			h.Patch(AccessTools.Method(typeof(ToDController), nameof(ToDController.Update)),
				postfix: new HarmonyMethod(typeof(Taktgeber), nameof(Taktgeber.Nachher)));
		}

		public override void Entfernt()
		{
			Gleiter.Zuruecksetzen();
		}
	}

	internal static class Modul
	{
		internal static ConfigEntry<bool> Eingeschaltet;
		internal static ConfigEntry<bool> Gleiten;
		internal static ConfigEntry<float> Takt;
		internal static ConfigEntry<float> SprungGrad;
		internal static ConfigEntry<bool> Diagnose;

		internal static int Schattenstufe()
		{
			if (!Singleton<SettingsManager>.Instantiated)
			{
				return -1;
			}
			return Singleton<SettingsManager>.Instance.Graphics.Settings.ShadowsQuality.Value;
		}

		/// <summary>Winkel zwischen zwei Drehungen in Grad, auch fuer winzige Winkel genau
		/// (Quaternion.Angle liefert unter ~0,16 Grad einfach 0).</summary>
		internal static double Grad(Quaternion a, Quaternion b)
		{
			Quaternion d = Quaternion.Inverse(a) * b;
			double v = Math.Sqrt((double)d.x * d.x + (double)d.y * d.y + (double)d.z * d.z);
			return 2.0 * Math.Atan2(v, Math.Abs((double)d.w)) * (180.0 / Math.PI);
		}
	}

	/// <summary>Optional: eigener Takt fuer die Sonnen-Neuberechnung statt des Schattenstufen-Werts.</summary>
	internal static class Taktgeber
	{
		public static void Nachher()
		{
			if (!Modul.Eingeschaltet.Value)
			{
				return;
			}
			float takt = Modul.Takt.Value;
			if (takt < 0f || WeatherEventController.ChangeTimeInProgress)
			{
				return;
			}
			TOD_Sky sky = MonoBehaviourSingleton<TOD_Sky>.Instance;
			if (sky != null)
			{
				sky.Light.UpdateInterval = takt;
			}
		}
	}

	internal static class Gleiter
	{
		private static TOD_Sky _sky;
		private static Transform _licht;
		private static Quaternion _von, _nach, _gezeigt;
		private static float _zielZeit;
		private static float _dauer = 0.5f;
		private static bool _bereit;

		// Diagnose
		private static readonly Stopwatch _uhr = new Stopwatch();
		private static float _diagStart = -1f;
		private static int _bilder, _updates, _spruenge;
		private static double _summeGrad, _maxGrad, _summeTakt;

		internal static void Zuruecksetzen()
		{
			_sky = null;
			_licht = null;
			_bereit = false;
		}

		// float_5 ist der Takt-Zaehler von TOD_Sky: UpdateCelestials zaehlt ihn erst hoch und
		// setzt ihn (bei Schattenstufe < 3) genau dann auf 0, wenn es die Sonne neu gesetzt hat.
		public static void Nachher(TOD_Sky __instance, float ___float_5)
		{
			if (!Modul.Eingeschaltet.Value || !Modul.Gleiten.Value || __instance == null)
			{
				_bereit = false;
				return;
			}
			bool diag = Modul.Diagnose.Value;
			if (diag)
			{
				_uhr.Start();
			}
			try
			{
				Schritt(__instance, ___float_5 == 0f);
			}
			finally
			{
				if (diag)
				{
					_uhr.Stop();
					Bericht();
				}
			}
		}

		private static void Schritt(TOD_Sky sky, bool neuGesetzt)
		{
			// Ultra: das Spiel gleitet selbst (Slerp in UpdateCelestials) - nichts tun.
			if (Modul.Schattenstufe() >= 3 || !Application.isPlaying)
			{
				_bereit = false;
				return;
			}
			TOD_Components teile = sky.Components;
			Transform licht = teile != null ? teile.LightTransform : null;
			if (licht == null)
			{
				return;
			}
			float jetzt = Time.time;
			if (!_bereit || sky != _sky || licht != _licht)
			{
				_sky = sky;
				_licht = licht;
				_von = _nach = _gezeigt = licht.rotation;
				_zielZeit = jetzt;
				_dauer = Mathf.Max(sky.Light.UpdateInterval, 0.02f);
				_bereit = true;
				return;
			}
			_bilder++;
			if (neuGesetzt)
			{
				Quaternion ziel = licht.rotation;
				double schritt = Modul.Grad(_nach, ziel);
				double abstand = Modul.Grad(_gezeigt, ziel);
				_updates++;
				_summeGrad += schritt;
				_maxGrad = Math.Max(_maxGrad, schritt);
				_summeTakt += jetzt - _zielZeit;
				_dauer = Mathf.Clamp(jetzt - _zielZeit, 0.02f, 2f);
				_zielZeit = jetzt;
				_nach = ziel;
				if (abstand > Modul.SprungGrad.Value)
				{
					// Zeitsprung oder Wechsel Sonne <-> Mond: direkt hin.
					_spruenge++;
					_von = _gezeigt = ziel;
					return;
				}
				_von = _gezeigt;
			}
			float k = Mathf.Clamp01((jetzt - _zielZeit) / _dauer);
			_gezeigt = Quaternion.Slerp(_von, _nach, k);
			licht.rotation = _gezeigt;
		}

		private static void Bericht()
		{
			float jetzt = Time.realtimeSinceStartup;
			if (_diagStart < 0f)
			{
				_diagStart = jetzt;
				return;
			}
			float spanne = jetzt - _diagStart;
			if (spanne < 10f)
			{
				return;
			}
			double usProBild = _bilder > 0 ? _uhr.Elapsed.TotalMilliseconds * 1000.0 / _bilder : 0.0;
			string stufe;
			switch (Modul.Schattenstufe())
			{
				case 0: stufe = "Low"; break;
				case 1: stufe = "Medium"; break;
				case 2: stufe = "High"; break;
				case 3: stufe = "Ultra (game glides itself, module idle)"; break;
				default: stufe = "?"; break;
			}
			float takt = _sky != null ? _sky.Light.UpdateInterval : -1f;
			Plugin.Log.LogInfo(string.Format(System.Globalization.CultureInfo.InvariantCulture,
				"[Smooth Sun] {0:0.0} s: {1} frames, {2} sun updates (every {3:0.00} s, interval setting {4:0.00} s), "
				+ "step avg {5:0.0000} deg / max {6:0.0000} deg, {7} jumps, cost {8:0.0} us/frame, shadows {9}",
				spanne, _bilder, _updates, _updates > 0 ? _summeTakt / _updates : 0.0, takt,
				_updates > 0 ? _summeGrad / _updates : 0.0, _maxGrad, _spruenge, usProBild, stufe));
			_diagStart = jetzt;
			_bilder = _updates = _spruenge = 0;
			_summeGrad = _maxGrad = _summeTakt = 0.0;
			_uhr.Reset();
		}
	}
}
