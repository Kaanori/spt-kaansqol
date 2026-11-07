// ---------------------------------------------------------------
//  Kein Schmerzmittel-Filter (No Painkiller Desaturation)
//
//  Nimmt Schmerzmitteln die grauen, entsaettigten Farben. Das
//  Schmerzmittel selbst wirkt unveraendert.
//
//  WO DER EFFEKT ENTSTEHT: EffectsController haengt an der Kamera
//  und hoert nur zu, welche Gesundheits-Effekte starten und enden.
//  Fuer IPainKiller legt Init() einen eigenen Sammler an:
//
//      new DesaturateMaskAccumulator(this, cc_Sharpen_0, typeof(IPainKiller))
//
//  Solange ein Schmerzmittel laeuft, schiebt dessen UpdateAmount()
//  jedes Bild MaskDesaturate Richtung 1 (0,5 pro Sekunde) und den
//  Radius der Maske von 1 auf 0,325 (0,2 pro Sekunde). CC_Sharpen
//  bzw. DesaturateEffect reichen beides als _desatMask/_radius an
//  den Shader weiter. Niemand sonst schreibt MaskDesaturate, und
//  kein anderer Gesundheits-Effekt laeuft ueber diesen Sammler.
//
//  WAS HIER PASSIERT: Ein Prefix vor UpdateAmount() biegt nur die
//  beiden Zielwerte auf den Ruhezustand um, also auf das, was der
//  Sammler auch ohne Schmerzmittel anstrebt. Die Bewegung dorthin
//  macht das Spiel selbst, darum blendet das Einschalten mitten im
//  Raid weich aus und nichts bleibt haengen.
//
//  WARUM NICHT WEITER OBEN:
//  - Effekt nicht starten (ActiveHealthController): dann gaebe es
//    keine Schmerzfreiheit.
//  - EffectsController.OnEffectStarted abfangen: dann fehlt der
//    Effekt auch im CC_RadialBlurAccumulator, und der ist es, der
//    unter Schmerzmittel den Schmerz-Weichzeichner abschaltet.
//  - DesaturateMaskAccumulator.AddEffect abfangen: wirkt nicht live,
//    ein laufender Effekt bliebe drin.
//  - CC_Sharpen/DesaturateEffect anfassen: dort sitzen auch die
//    Wetter- und die Erschoepfungs-Entsaettigung.
//
//  NICHT BETROFFEN: HealthDesaturate (DesaturateAccumulator, leichte
//  Entsaettigung bei Erschoepfung) ist ein eigener Wert im selben
//  Shader und hat nichts mit Schmerzmitteln zu tun.
// ---------------------------------------------------------------
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace KaansQoL.KeinSchmerzmittelFilter
{
	internal sealed class KeinSchmerzmittelFilterModule : QolModule
	{
		public override string Id => "KeinSchmerzmittelFilter";

		public override string Section => "07 No Painkiller Filter";

		public override string Beschreibung =>
			"Removes the grey, desaturated colours of painkillers. The painkiller itself keeps working. "
			+ "Takes effect immediately, also during a raid.";

		public override string AlteGuid => "com.kaan.keinschmerzmittelfilter";

		// Beim Ausschalten rechnet der Patch den Zustand selbst zurueck (siehe unten).
		public override bool PatchBleibtBeiAus => true;

		public override string AlterName => "KeinSchmerzmittelFilter";

		// Die alte cfg hatte nur "1. General / Enabled".
		public override MigRow[] Umzug => new[]
		{
			new MigRow("1. General", "Enabled", "Enabled")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			Modul.Eingeschaltet = Enabled;
		}

		public override void Apply(Harmony h)
		{
			h.PatchAll(typeof(MaskenZielPatch));
		}
	}

	internal static class Modul
	{
		internal static ManualLogSource Log => KaansQoL.Plugin.Log;

		internal static ConfigEntry<bool> Eingeschaltet;
	}

	[HarmonyPatch(typeof(EffectsController.DesaturateMaskAccumulator),
		nameof(EffectsController.DesaturateMaskAccumulator.UpdateAmount))]
	internal static class MaskenZielPatch
	{
		private static bool _fehlerGemeldet;

		[HarmonyPrefix]
		private static void Vorher(EffectsController.DesaturateMaskAccumulator __instance)
		{
			try
			{
				if (Modul.Eingeschaltet.Value)
				{
					__instance.MaxEffectValue = 0f;
					__instance._desaturateRadius = __instance._maxDesaturateRadius;
					// Das Ende EINES von zwei Schmerzmitteln setzt diese Marke.
					// Bei Maske 0 wuerde UpdateAmount den Sammler dann abschalten,
					// obwohl noch eins laeuft, und das Ausschalten des Mods bliebe
					// bis zum naechsten Schmerzmittel ohne Wirkung.
					if (__instance.ActiveEffects.Count > 0)
					{
						__instance._restoreDesaturateMask = false;
					}
				}
				else if (__instance.MaxEffectValue == 0f && __instance.ActiveEffects.Count > 0)
				{
					// Gerade ausgeschaltet: Das Spiel rechnet seine Ziele neu aus.
					// Ohne diesen Mod gibt es diesen Zustand nicht, sonst laeuft
					// hier also nichts.
					__instance.UpdateMaxEffectValue();
				}
			}
			catch (Exception fehler)
			{
				if (!_fehlerGemeldet)
				{
					_fehlerGemeldet = true;
					Modul.Log.LogError("Painkiller desaturation patch failed: " + fehler);
				}
			}
		}
	}
}
