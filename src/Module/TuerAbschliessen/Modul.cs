// ---------------------------------------------------------------
//  Tuer abschliessen
//
//  Haengt den Eintrag "Abschliessen" ins Tuermenue. Damit laesst
//  sich eine Tuer, die man mit dem Schluessel aufgesperrt und
//  danach wieder zugemacht hat, erneut verriegeln.
//
//  WARUM DAS UEBERHAUPT GEHT: Das Spiel kann es bereits. In
//  EFT.Interactive.WorldInteractiveObject steht eine fertige,
//  oeffentliche Methode Lock():
//
//      _interaction.ResultState = EDoorState.Locked;
//      DoorState = EDoorState.Locked;
//      CurrentAngle = GetAngle(DoorState);
//
//  Auch EInteractionType.Lock gibt es. BSG hat den Menueeintrag
//  nur nie verdrahtet: EFT.InteractionContextHelper
//  .GetAvailableActions(GamePlayerOwner, Door) kennt genau
//  OpenDoor, CloseDoor, Breach und die drei grauen Taktik-
//  Eintraege. Hier wird nichts nachgebaut - wir haengen uns mit
//  einem Postfix hinter diese Methode und legen einen Eintrag
//  obendrauf.
//
//  Door.Interact(..., EInteractionType.Lock) ALLEIN LAEUFT INS LEERE.
//  Die Basisklasse verarbeitet Lock, aber Door ueberschreibt
//  Interact und ihr Schalter kennt nur Open, Close, Unlock und
//  Breach. Bis 1.1.0 darum direkt door.Lock() - stumm, ohne Hand.
//  SEIT 1.1.1 laufen Ab- und Aufschliessen ueber den Bedienweg des
//  Spiels (Player.StartInteraction), mit Schluessel-Animation und
//  demselben Schloss-Klang wie das Aufsperren; den fehlenden
//  Lock-Fall in Door.Interact ergaenzt SchlossBedienung.cs.
//
//  BOTS BEKOMMEN ES MIT: Lock() schreibt ueber den DoorState-
//  Setter, und der feuert OnDoorStateChanged sowie
//  DoorStateChanged(). Daran haengt die KI-Navigation.
//
//  KARTENTUEREN (KeycardDoor) sind absichtlich aussen vor - die
//  haben ihre eigene Menuemethode und ein anderes Schloss.
//
//  SEIT 1.1.0 - TUEREN OHNE SCHLOSS (Einstellung, Standard aus):
//  Auch Tueren ohne KeyId lassen sich abschliessen. Welche genau,
//  entscheidet Abgeschlossen.OhneSchlossAbschliessbar. Das Spiel
//  selbst bietet an so einer Tuer danach nur noch "Eintreten" an:
//  InteractionContextHelper.TryAddUnlock bricht bei leerer KeyId
//  sofort ab, ein "Unlock" erscheint nie. Darum haengen wir an
//  Tueren, die WIR verriegelt haben, "Aufschliessen" an - das ruft
//  die Unlock()-Methode des Spiels (Zustand zurueck auf Shut,
//  ebenfalls ueber den DoorState-Setter).
//
//  SEIT 1.1.0 - BOTS: Jede Tuer, die du abgeschlossen hast, merkt
//  sich das Plugin bis Raidende; kein Bot bekommt sie auf (siehe
//  Abgeschlossen.cs).
//
//  Einstellbar unter F12 -> Tuer abschliessen.
// ---------------------------------------------------------------
using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.Interactive;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace KaansQoL.TuerAbschliessen
{
	internal sealed class TuerAbschliessenModule : QolModule
	{
		public override string Id => "TuerAbschliessen";

		public override string Section => "09 Lock Doors";

		public override string Beschreibung =>
			"Adds \"Lock\" to the door menu, makes every door kickable, and keeps bots out of doors you locked. "
			+ "Switching this off during a raid leaves doors you already locked locked.";

		public override string AlteGuid => "com.kaan.tuerabschliessen";

		public override string AlterName => "TuerAbschliessen";

		public override MigRow[] Umzug => new[]
		{
			new MigRow("1. Allgemein", "Eingeschaltet", "Enabled"),
			new MigRow("1. Allgemein", "Schluessel noetig", "Key required"),
			new MigRow("1. Allgemein", "Jede Tuer eintretbar", "Every door can be kicked in"),
			new MigRow("1. Allgemein", "Eintreten gelingt immer", "Kicking in always succeeds"),
			new MigRow("1. Allgemein", "Bots duerfen eintreten", "Bots may kick in doors"),
			new MigRow("1. Allgemein", "Auch Tueren ohne Schloss abschliessen", "Lock doors without a lock too"),
			new MigRow("1. Allgemein", "Bots respektieren abgeschlossene Tueren", "Bots respect locked doors"),
			new MigRow("2. Anzeige", "Beschriftung", "Label lock"),
			new MigRow("2. Anzeige", "Beschriftung Aufschliessen", "Label unlock"),
			new MigRow("3. Diagnose", "Protokoll", "Write log")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			string s = Section;
			Modul.An = Enabled;

			Modul.SchluesselNoetig = Cfg.Bind(cfg, s, "Key required", true,
				"On: only with the matching key in your inventory. "
				+ "Off: every door with a lock can be locked.");

			Modul.JedeTuerEintretbar = Cfg.Bind(cfg, s, "Every door can be kicked in", true,
				"On: \"Kick in\" can be chosen on every closed door - even those the game never meant it for,\n"
				+ "and no matter from which angle you stand in front of it.");

			Modul.EintretenGelingtImmer = Cfg.Bind(cfg, s, "Kicking in always succeeds", true,
				"On: no more missteps. By default the kick always fails when the door opens towards you -\n"
				+ "then you kick the lock instead of the door open.");

			Modul.BotsTretenEin = Cfg.Bind(cfg, s, "Bots may kick in doors", false,
				"Off: bots only open doors normally and no longer kick any in.\n"
				+ "On: bots roll the dice as in the unmodified game (BREACH_CHANCE_100 of their difficulty).");

			Modul.AuchOhneSchloss = Cfg.Bind(cfg, s, "Lock doors without a lock too", false,
				"On: ordinary doors without a keyhole can be locked as well.\n"
				+ "Keycard doors, power/switch doors, quest and trigger doors and doors the game locked itself stay excluded.\n"
				+ "Such a door opens again via \"Unlock\" or by kicking it in.");

			Modul.BotsAussperren = Cfg.Bind(cfg, s, "Bots respect locked doors", true,
				"On: a door YOU locked cannot be opened by any bot - neither unlock nor open nor kick in, "
				+ "not even via ORBIT or SAIN.\n"
				+ "Applies until the end of the raid or until you open it again yourself.");

			Modul.Beschriftung = Cfg.Bind(cfg, s, "Label lock", "Lock",
				"Text of the menu entry. The game sends menu texts through its translation table; "
				+ "if the text is not in there it is shown unchanged - which is exactly what is wanted here.");

			Modul.BeschriftungAuf = Cfg.Bind(cfg, s, "Label unlock", "Unlock",
				"Text of the menu entry on a door without a lock that you locked.");

			Modul.Protokoll = Cfg.Bind(cfg, s, "Write log", false,
				"Writes every locking action to the BepInEx console - and every rejected attempt "
				+ "of a bot including the caller.");
		}

		public override void Apply(Harmony h)
		{
			h.PatchAll(typeof(TuermenuePatch));
			h.PatchAll(typeof(FreigabeBedienen));
			h.PatchAll(typeof(FreigabeEintreten));
			h.PatchAll(typeof(FreigabeNachFehltritt));
			h.PatchAll(typeof(SperreZustand));
			h.PatchAll(typeof(SperreSetDoorState));
			h.PatchAll(typeof(SperreUnlock));
			h.PatchAll(typeof(SperreEintreten));
			h.PatchAll(typeof(SperreWinkel));
			h.PatchAll(typeof(SperreWinkelSchiebetuer));
			h.PatchAll(typeof(EintretenFreischalten));
			h.PatchAll(typeof(EintretenGelingt));
			h.PatchAll(typeof(BotsTretenNichtEin));
			h.PatchAll(typeof(BedienungAusfuehren));
			h.PatchAll(typeof(SchlossAnimation));
			h.PatchAll(typeof(SchluesselInDerHand));
			if (BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey(OrbitSperre.OrbitGuid))
			{
				OrbitSperre.Anwenden(h);
			}
		}
	}

	internal static class Modul
	{
		internal static ManualLogSource Log => KaansQoL.Plugin.Log;

		internal static ConfigEntry<bool> An;

		internal static ConfigEntry<bool> SchluesselNoetig;

		internal static ConfigEntry<string> Beschriftung;

		internal static ConfigEntry<bool> Protokoll;

		internal static ConfigEntry<bool> JedeTuerEintretbar;

		internal static ConfigEntry<bool> EintretenGelingtImmer;

		internal static ConfigEntry<bool> BotsTretenEin;

		internal static ConfigEntry<bool> AuchOhneSchloss;

		internal static ConfigEntry<bool> BotsAussperren;

		internal static ConfigEntry<string> BeschriftungAuf;
	}

	// Haengt sich hinter den Aufbau des Tuermenues.
	[HarmonyPatch]
	internal static class TuermenuePatch
	{
		// GetAvailableActions gibt es mehrfach - fuer Kisten, Kofferraeume,
		// Kartentueren und so weiter. Statt den Namen als Zeichenkette
		// hinzuschreiben und auf die richtige Ueberladung zu hoffen, lassen
		// wir den Compiler sie aussuchen: Wenn das Spiel diese Methode mit
		// genau dieser Signatur nicht mehr hat, scheitert schon der Bau -
		// und nicht erst das Plugin im laufenden Raid.
		[HarmonyTargetMethod]
		internal static MethodBase Ziel()
		{
			Func<GamePlayerOwner, Door, AvailableInteractionState> zeiger =
				InteractionContextHelper.GetAvailableActions;
			return zeiger.Method;
		}

		[HarmonyPostfix]
		internal static void Nachher(GamePlayerOwner owner, Door door, AvailableInteractionState __result)
		{
			try
			{
				if (!Modul.An.Value)
				{
					return;
				}
				if (__result == null || __result.Actions == null)
				{
					return;
				}
				if ((object)door == null || (object)owner == null || (object)owner.Player == null)
				{
					return;
				}

				bool hatSchloss = !string.IsNullOrEmpty(door.KeyId);

				// Nur eine zugefallene Tuer laesst sich abschliessen. Offen
				// heisst: erst zumachen.
				if (door.DoorState == EDoorState.Shut)
				{
					if (hatSchloss)
					{
						// Steckt der passende Schluessel in der Tasche? GetKey
						// sitzt in PlayerOwner und macht genau die Pruefung, die
						// das Spiel auch fuers Aufsperren benutzt.
						if (Modul.SchluesselNoetig.Value && owner.GetKey(door) == null)
						{
							return;
						}
					}
					else
					{
						// Ohne KeyId hat die Tuer gar kein Schloss - nur mit
						// der Zusatz-Einstellung, und nur geeignete Tueren.
						if (!Modul.AuchOhneSchloss.Value)
						{
							return;
						}
						if (!Abgeschlossen.OhneSchlossAbschliessbar(door, out string grund))
						{
							if (Modul.Protokoll.Value && door.Id != _letzteAbgelehnte)
							{
								_letzteAbgelehnte = door.Id;
								Modul.Log.LogInfo("Ohne Schloss nicht abschliessbar: " + door.Id + " (" + grund + ")");
							}
							return;
						}
					}

					Door tuer = door;
					GamePlayerOwner besitzer = owner;
					EintragAnhaengen(__result, Modul.Beschriftung.Value, !door.Operatable, delegate
					{
						Abschliessen(besitzer, tuer);
					});
					return;
				}

				// Verriegelt - und zwar von UNS? Dann muss es auch wieder
				// aufgehen. Das Spiel bietet "Unlock" nur mit passendem
				// Schluessel an (TryAddUnlock); ohne KeyId nie.
				if (door.DoorState == EDoorState.Locked && Abgeschlossen.IstUnsere(door))
				{
					if (hatSchloss)
					{
						// Mit Schloss gilt wie bisher der Schluessel. Nur wer
						// "Schluessel noetig" aus hat, konnte ohne Schluessel
						// abschliessen - dem fehlt sonst der Weg zurueck.
						if (Modul.SchluesselNoetig.Value || HatEintrag(__result, "Unlock"))
						{
							return;
						}
					}

					Door tuer = door;
					GamePlayerOwner besitzer = owner;
					EintragAnhaengen(__result, Modul.BeschriftungAuf.Value, !door.Operatable, delegate
					{
						Aufschliessen(besitzer, tuer);
					});
				}
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Tuer abschliessen: Menueaufbau - " + ex);
			}
		}

		private static void Abschliessen(GamePlayerOwner owner, Door door)
		{
			try
			{
				// Wie das Spiel vor dem Aufsperren (TryAddUnlock).
				owner.Player.MovementContext.ResetCanUsePropState();

				// Mit Schloss nimmt die Hand den Schluessel mit, falls er
				// in der Tasche steckt ("Schluessel noetig" aus: evtl. nicht).
				KeyComponent schluessel = string.IsNullOrEmpty(door.KeyId) ? null : owner.GetKey(door);

				// Ueber den Bedienweg des Spiels: Hand zum Schloss,
				// Zylinder dreht und klingt, dann Lock() ueber den
				// DoorState-Setter (KI-Navigation bekommt es mit). Gemerkt
				// wird die Tuer erst, wenn sie wirklich zu ist
				// (SchlossBedienung.Abschliessvorgang).
				SchlossBedienung.Abschliessen(owner.Player, door, schluessel);

				if (Modul.Protokoll.Value)
				{
					Modul.Log.LogInfo("Abschliessen gestartet: " + door.Id + " ("
						+ (string.IsNullOrEmpty(door.KeyId) ? "ohne Schloss" : "Schluessel " + door.KeyId
							+ (schluessel == null ? ", nicht dabei" : ""))
						+ ")");
				}
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Tuer abschliessen: Abschliessen - " + ex);
			}
		}

		private static void Aufschliessen(GamePlayerOwner owner, Door door)
		{
			try
			{
				owner.Player.MovementContext.ResetCanUsePropState();

				// Ueber den Bedienweg des Spiels wie das normale Aufsperren:
				// Schluessel-Animation, Door.Interact(Unlock) -> Unlock()
				// -> UnlockCoroutine (Zylinder klingt, Zustand Shut ueber
				// den DoorState-Setter, Trigger). Die Freigabe fuer unsere
				// Sperre gibt der Prefix auf MovementContext.StartInteraction.
				SchlossBedienung.Aufschliessen(owner.Player, door);

				if (Modul.Protokoll.Value)
				{
					Modul.Log.LogInfo("Aufschliessen gestartet: " + door.Id
						+ " (Klang " + (SchlossBedienung.HatSchlossKlang(door) ? "Zylinder" : "Ersatz") + ")");
				}
			}
			catch (Exception ex)
			{
				Modul.Log.LogError("Tuer abschliessen: Aufschliessen - " + ex);
			}
		}

		private static string _letzteAbgelehnte;

		private static bool HatEintrag(AvailableInteractionState zustand, string name)
		{
			// Bewusst mit Index statt foreach - die Liste wird waehrend des
			// Menueaufbaus an anderer Stelle noch angefasst.
			for (int i = 0; i < zustand.Actions.Count; i++)
			{
				InteractionAction eintrag = zustand.Actions[i];
				if (eintrag != null && eintrag.Name == name)
				{
					return true;
				}
			}
			return false;
		}

		private static void EintragAnhaengen(AvailableInteractionState zustand, string text, bool grau, Action aktion)
		{
			// Kein zweiter Eintrag, falls die Methode mehrfach durchlaeuft.
			if (HatEintrag(zustand, text))
			{
				return;
			}
			zustand.Actions.Add(new InteractionAction
			{
				Name = text,
				Disabled = grau,
				Action = aktion
			});
			// Das Menue faerbt sich grau, solange ein Fehlertext gesetzt
			// ist. Wir haben gerade einen brauchbaren Eintrag angehaengt.
			zustand.Error = null;
		}
	}
}
