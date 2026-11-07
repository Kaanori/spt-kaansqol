// ---------------------------------------------------------------
//  Kartentuer hacken
//
//  Haengt den Eintrag "Hacken" ins Menue jeder verschlossenen
//  Kartentuer (Labs, Reserve, Interchange-Kiba usw.). Nach einer
//  kurzen Hackzeit geht die Tuer auf - ohne Zugangskarte.
//
//  WO DER HEBEL SITZT: Das Menue fuer Kartentueren baut
//  EFT.InteractionContextHelper.GetAvailableActions(
//      GamePlayerOwner, KeycardDoor, bool isProxy).
//  isProxy = true heisst: du schaust auf den Kartenleser. Dann
//  listet das Spiel deine Karten auf ("Karte versuchen ...") oder
//  meldet "Keine Karten". Schaust du auf die Tuer selbst, steht
//  nur "Verschlossen" da. In beiden Faellen haengen wir "Hacken"
//  an und loeschen den Fehlertext.
//
//  WIE GEOEFFNET WIRD: door.Unlock() - dieselbe Methode, die auch
//  eine passende Karte am Ende ausloest. KeycardDoor ueberschreibt
//  UnlockCoroutine: Leser-Griff, Zustand Shut, gruener Piepton,
//  und bei Tueren mit _openOnUnlock schwingt sie gleich auf.
//  Bots bekommen es ueber den DoorState-Setter mit.
//
//  Einstellbar unter F12 -> Kartentuer hacken.
// ---------------------------------------------------------------
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.Communications;
using EFT.Interactive;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace KaansQoL.KartentuerHacken
{
	internal sealed class KartentuerHackenModule : QolModule
	{
		public override string Id => "KartentuerHacken";

		public override string Section => "08 Hack Keycard Doors";

		public override string Beschreibung => "Shows \"Hack\" on every locked keycard door.";

		public override string AlteGuid => "com.kaan.kartentuerhacken";

		public override string AlterName => "KartentuerHacken";

		public override MigRow[] Umzug => new[]
		{
			new MigRow("1. Allgemein", "Eingeschaltet", "Enabled"),
			new MigRow("1. Allgemein", "Passende Karte automatisch waehlen", "Choose matching card automatically"),
			new MigRow("1. Allgemein", "Hackdauer (Sekunden)", "Hack duration (s)"),
			new MigRow("1. Allgemein", "Max. Abstand (Meter)", "Max distance (m)"),
			new MigRow("1. Allgemein", "Erfolgschance (%)", "Success chance (%)"),
			new MigRow("2. Anzeige", "Beschriftung", "Label"),
			new MigRow("3. Diagnose", "Protokoll", "Write log")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			string s = Section;
			Modul.An = Enabled;
			Modul.KarteAutomatisch = Cfg.Bind(cfg, s, "Choose matching card automatically", true,
				"On: at the card reader only ONE entry \"Unlock\" with the matching card is shown instead of "
				+ "\"Try card X\" for every card (same key search as for normal doors). Wrong cards are no longer "
				+ "offered. Off: behaviour as in the unmodified game.");
			Modul.Hackdauer = Cfg.Bind(cfg, s, "Hack duration (s)", 5f,
				"How long the hacking takes. 0 = opens immediately.",
				new AcceptableValueRange<float>(0f, 60f));
			Modul.MaxAbstand = Cfg.Bind(cfg, s, "Max distance (m)", 3f,
				"If you move further than this from the starting point while hacking, it is aborted.",
				new AcceptableValueRange<float>(1f, 20f));
			Modul.Erfolgschance = Cfg.Bind(cfg, s, "Success chance (%)", 100,
				"Below 100 the hack can fail (red beep); then just try again.",
				new AcceptableValueRange<int>(1, 100));
			Modul.Beschriftung = Cfg.Bind(cfg, s, "Label", "Hack", "Text of the menu entry.");
			Modul.Protokoll = Cfg.Bind(cfg, s, "Write log", false, "Writes every hack attempt to the BepInEx console.");
		}

		public override void Apply(Harmony h)
		{
			h.PatchAll(typeof(KartentuerMenuePatch));
		}
	}

	internal static class Modul
	{
		internal static ManualLogSource Log => KaansQoL.Plugin.Log;

		internal static ConfigEntry<bool> An;

		internal static ConfigEntry<bool> KarteAutomatisch;

		internal static ConfigEntry<float> Hackdauer;

		internal static ConfigEntry<float> MaxAbstand;

		internal static ConfigEntry<int> Erfolgschance;

		internal static ConfigEntry<string> Beschriftung;

		internal static ConfigEntry<bool> Protokoll;

		// Tueren, an denen gerade gehackt wird - kein zweiter Versuch
		// parallel, falls man den Eintrag mehrfach anklickt.
		internal static readonly HashSet<KeycardDoor> Laufend = new HashSet<KeycardDoor>();
	}

	[HarmonyPatch]
	internal static class KartentuerMenuePatch
	{
		// Ueberladung vom Compiler aussuchen lassen - aendert ein
		// SPT-Update die Signatur, scheitert schon der Bau.
		[HarmonyTargetMethod]
		internal static MethodBase Ziel()
		{
			Func<GamePlayerOwner, KeycardDoor, bool, AvailableInteractionState> zeiger =
				InteractionContextHelper.GetAvailableActions;
			return zeiger.Method;
		}

		[HarmonyPostfix]
		internal static void Nachher(GamePlayerOwner owner, KeycardDoor door, bool isProxy, AvailableInteractionState __result)
		{
			try
			{
				if (!Modul.An.Value || __result == null || __result.Actions == null)
				{
					return;
				}
				if ((object)door == null || (object)owner == null || (object)owner.Player == null)
				{
					return;
				}
				if (door.DoorState != EDoorState.Locked)
				{
					return;
				}

				// Passende Karte automatisch: Das Spiel listet am Kartenleser
				// (isProxy) jede besessene Karte als "Karte X versuchen" - auch
				// die falschen. Normale Tueren waehlen den passenden Schluessel
				// dagegen selbst (InteractionContextHelper.TryAddUnlock ueber
				// owner.GetKey). Genau diese Spielfunktion nutzen wir hier; im
				// gesperrten Zustand stehen bis hierher nur die Karteneintraege
				// des Spiels in der Liste.
				if (isProxy && Modul.KarteAutomatisch.Value)
				{
					var passend = new List<InteractionAction>();
					var ergebnis = InteractionContextHelper.TryAddUnlock(owner, door, passend);
					__result.Actions.Clear();
					__result.Actions.AddRange(passend);
					if (ergebnis.Failed && Modul.Protokoll.Value)
					{
						Modul.Log.LogInfo("Kartentuer: keine passende Karte fuer " + door.Id
							+ (string.IsNullOrEmpty(ergebnis.Error) ? "" : " (" + ergebnis.Error + ")"));
					}
				}

				string text = Modul.Beschriftung.Value;
				for (int i = 0; i < __result.Actions.Count; i++)
				{
					InteractionAction eintrag = __result.Actions[i];
					if (eintrag != null && eintrag.Name == text)
					{
						return;
					}
				}

				KeycardDoor tuer = door;
				GamePlayerOwner besitzer = owner;

				__result.Actions.Add(new InteractionAction
				{
					Name = text,
					Disabled = !door.Operatable || Modul.Laufend.Contains(door),
					Action = delegate
					{
						Starten(besitzer, tuer);
					}
				});

				// Solange ein Fehlertext ("Keine Karten", "Verschlossen")
				// gesetzt ist, zeigt das Spiel das Menue grau an.
				__result.Error = null;
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Kartentuer hacken: Menueaufbau - " + ex);
			}
		}

		private static void Starten(GamePlayerOwner owner, KeycardDoor door)
		{
			try
			{
				// Sonst bleibt die Spielfigur im Bedien-Zustand haengen.
				owner.Player.MovementContext.ResetCanUsePropState();

				if (Modul.Laufend.Contains(door) || door.DoorState != EDoorState.Locked)
				{
					return;
				}
				Modul.Laufend.Add(door);
				KaansQoL.Plugin.Instance.StartCoroutine(Hacken(owner.Player, door));
			}
			catch (Exception ex)
			{
				Modul.Laufend.Remove(door);
				Modul.Log.LogError("Kartentuer hacken: Start - " + ex);
			}
		}

		private static IEnumerator Hacken(Player player, KeycardDoor door)
		{
			float dauer = Modul.Hackdauer.Value;
			if (dauer > 0f)
			{
				Meldung("Kartenleser wird gehackt ... (" + dauer.ToString("0.#") + " s)");
			}

			Vector3 start = player.Position;
			float ende = Time.time + dauer;
			while (Time.time < ende)
			{
				if (!NochDa(player, door, start))
				{
					Modul.Laufend.Remove(door);
					Meldung("Hacken abgebrochen - zu weit weg.");
					yield break;
				}
				yield return null;
			}

			try
			{
				if (!NochDa(player, door, start) || door.DoorState != EDoorState.Locked)
				{
					yield break;
				}

				bool erfolg = UnityEngine.Random.Range(0, 100) < Modul.Erfolgschance.Value;
				if (erfolg)
				{
					door.Unlock();
					Meldung("Zugang gewaehrt.");
				}
				else
				{
					door.StartCoroutine(door.PlayResultSound(success: false));
					Meldung("Hacken fehlgeschlagen - nochmal versuchen.");
				}

				if (Modul.Protokoll.Value)
				{
					Modul.Log.LogInfo("Hackversuch an " + door.Id + " (Karte " + door.KeyId + "): "
						+ (erfolg ? "offen" : "fehlgeschlagen"));
				}
			}
			finally
			{
				Modul.Laufend.Remove(door);
			}
		}

		private static bool NochDa(Player player, KeycardDoor door, Vector3 start)
		{
			if ((object)player == null || player == null || !player.HealthController.IsAlive)
			{
				return false;
			}
			if (door == null)
			{
				return false;
			}
			return Vector3.Distance(player.Position, start) <= Modul.MaxAbstand.Value;
		}

		private static void Meldung(string text)
		{
			try
			{
				NotificationManager.DisplayMessageNotification(text);
			}
			catch (Exception)
			{
				// Ohne Meldung geht es auch.
			}
		}
	}
}
