// ---------------------------------------------------------------
//  Spielt ein Gespraech ab der gewaehlten Antwort "im Kopf" weiter.
//
//  Nachbau von EFT (Assembly-CSharp, Namensraum EFT.Dialogs):
//    BaseTraderDialogController.SetDialogProgress  - Aktionen der Zeile
//        ausfuehren, SwitchDialog merken, Quit beendet
//    BaseTraderDialogController.method_0           - neuen Dialog bauen,
//        Startpunkt setzt die Hauptvariable
//    BaseTraderDialogController.SetCurrentDialog   - anderer Dialog =>
//        Dialog-Variablen leeren (ClearTempData)
//    DynamicTraderDialog-Konstruktor               - Zeilen filtern: alle
//        mit wahrem Ausloeser, aber nur die ERSTE NPC-Zeile
//    ClientDialogController.ExecuteLine            - ist eine NPC-Zeile
//        dabei, spricht der Haendler sofort die erste Zeile weiter
//
//  Die Simulation laeuft, bis wieder eine echte Wahl kommt (mindestens
//  zwei Antworten) oder das Gespraech endet. Gibt es nur EINE Antwort,
//  folgt sie ihr - die Folgen dieser Pflicht-Antwort gehoeren zur
//  vorherigen Wahl.
// ---------------------------------------------------------------
using System.Collections.Generic;

namespace KaansQoL.GespraechsFolgen.Kern
{
	public sealed class SimErgebnis
	{
		public readonly List<Aktion> QuestAktionen = new List<Aktion>();
		public Dictionary<string, int> ProfilAenderungen = new Dictionary<string, int>();
		public bool Zufall;
		public int Schritte;
		public int Pflichtantworten;
		public bool Abgebrochen;   // Schrittgrenze erreicht (Schleife im Gespraech?)
	}

	public static class DialogSimulator
	{
		public const int MaxSchritte = 60;

		public static SimErgebnis Simuliere(IDialogQuelle quelle, string dialogId, IZeile gewaehlt,
			DialogStand stand, bool pflichtantwortenFolgen = true)
		{
			var erg = new SimErgebnis();
			var aktuell = dialogId;
			var zeile = gewaehlt;

			while (zeile != null)
			{
				if (erg.Schritte >= MaxSchritte)
				{
					erg.Abgebrochen = true;
					break;
				}
				erg.Schritte++;

				var ziel = aktuell;
				string startPunkt = null;
				var ende = false;
				foreach (var a in zeile.Aktionen)
				{
					switch (a)
					{
						case WechsleDialog w:
							ziel = w.DialogId;
							startPunkt = w.StartPunkt;
							break;
						case SetzeVariable s:
							stand.Setze(s.Id, s.Wert, s.Ort);
							break;
						case Beenden _:
							ende = true;
							break;
						default:
							erg.QuestAktionen.Add(a);
							break;
					}
				}

				zeile = null;
				if (ende)
					break;

				var vorlage = quelle.Vorlage(ziel);
				if (vorlage == null)
					break;
				if (startPunkt != null)
					stand.Setze(vorlage.HauptVariable, vorlage.StartWert(startPunkt), Speicher.Dialog);

				var zeilen = Aufbauen(vorlage, stand);
				// Reihenfolge wie im Spiel: erst Zeilen bauen, dann (bei
				// anderem Dialog) die Dialog-Variablen leeren.
				if (ziel != aktuell)
					stand.DialogLeeren();
				aktuell = ziel;

				if (zeilen.Count == 0)
					break;   // Spiel wuerde eine "Zurueck"-Ersatzzeile bauen

				var npc = false;
				foreach (var z in zeilen)
					if (z.Seite == Seite.Npc)
						npc = true;

				if (npc)
				{
					zeile = zeilen[0];   // wie LastNpcLine = Lines.FirstOrDefault()
					continue;
				}
				if (pflichtantwortenFolgen && zeilen.Count == 1)
				{
					erg.Pflichtantworten++;
					zeile = zeilen[0];
				}
			}

			erg.Zufall = stand.Zufall;
			foreach (var kv in stand.ProfilAenderungen)
				erg.ProfilAenderungen[kv.Key] = kv.Value;
			return erg;
		}

		// Nachbau des Zeilenfilters im DynamicTraderDialog-Konstruktor.
		public static List<IZeile> Aufbauen(IDialogVorlage vorlage, DialogStand stand)
		{
			var liste = new List<IZeile>();
			var npc = false;
			foreach (var z in vorlage.Zeilen)
			{
				if (z.Seite == Seite.Npc && npc)
					continue;
				if (!z.Ausloeser(stand))
					continue;
				if (z.Seite == Seite.Npc)
					npc = true;
				liste.Add(z);
			}
			return liste;
		}
	}
}
