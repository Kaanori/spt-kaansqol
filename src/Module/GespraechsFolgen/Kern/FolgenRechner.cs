// ---------------------------------------------------------------
//  Rechnet aus, was eine Antwort mit den Aufgaben macht.
//
//  Eingabe: die Profil-Variablen, die das Gespraech setzen wuerde, und
//  die Aufgaben-Aktionen (Annehmen, Abschliessen, Abgeben) aus der
//  Simulation. Dann in Runden:
//
//    Runde 1 = direkte Folgen: Bedingungen, die auf genau diese
//              Variablen/Abgaben schauen (Fail, Ziel, Start).
//    Runde 2 = Folge-Folgen: Aufgaben, deren Bedingungen auf den
//              Status einer Aufgabe aus Runde 1 schauen, oder auf
//              Variablen, die VisitAPI beim Statuswechsel setzt
//              ("setVariables"), oder auf Gruppen-Variablen.
//
//  Jede Runde rechnet gegen den Stand am ENDE der vorigen Runde, damit
//  die Tiefe stimmt (sonst wuerde eine Folge-Folge je nach Reihenfolge
//  der Aufgaben schon als direkte Folge zaehlen).
//
//  "Neu" heisst immer: nach der Antwort wahr, VORHER nicht wahr. Was
//  schon vorher erfuellt war, ist keine Folge dieser Antwort.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;

namespace KaansQoL.GespraechsFolgen.Kern
{
	public enum FolgeArt { Fehlschlag, Abschluss, Abgabebereit, Start, Freischaltung, Ziel }

	public sealed class Folge
	{
		public string QuestId;
		public FolgeArt Art;
		public int Tiefe;
		public string BedingungId;   // nur bei Ziel
		public override string ToString() => $"{Art} {QuestId} (Tiefe {Tiefe}{(BedingungId != null ? ", " + BedingungId : "")})";
	}

	public static class FolgenRechner
	{
		// Momentaufnahme: Ueberlagerung ueber die echte Welt.
		private sealed class Stand
		{
			public readonly Dictionary<string, int> Var = new Dictionary<string, int>();
			public readonly Dictionary<string, QStatus> Status = new Dictionary<string, QStatus>();
			public readonly HashSet<string> Abgegeben = new HashSet<string>();   // "quest|bedingung"
			public Stand Kopie()
			{
				var k = new Stand();
				foreach (var kv in Var) k.Var[kv.Key] = kv.Value;
				foreach (var kv in Status) k.Status[kv.Key] = kv.Value;
				foreach (var a in Abgegeben) k.Abgegeben.Add(a);
				return k;
			}
		}

		public static List<Folge> Berechne(IWelt welt, IReadOnlyDictionary<string, int> profilAenderungen,
			IEnumerable<Aktion> questAktionen, int maxTiefe)
		{
			var folgen = new List<Folge>();
			var gemeldet = new HashSet<string>();      // "quest|art|bedingung"
			var beendet = new HashSet<string>();       // Aufgaben mit Abschluss/Fehlschlag in der Vorhersage
			var vorher = new Stand();                  // leer = echte Welt
			var jetzt = new Stand();

			foreach (var kv in profilAenderungen)
				jetzt.Var[kv.Key] = kv.Value;
			NachrechnenGruppen(welt, jetzt, profilAenderungen.Keys.ToList());

			// Runde 1 bekommt zusaetzlich die direkten Aufgaben-Aktionen.
			var naechste = jetzt.Kopie();
			var statuswechsel = new List<(string quest, QStatus status)>();
			foreach (var a in questAktionen)
			{
				switch (a)
				{
					case GibAb g when !g.Teilweise && g.QuestId != null && g.BedingungId != null:
						jetzt.Abgegeben.Add(g.QuestId + "|" + g.BedingungId);
						naechste.Abgegeben.Add(g.QuestId + "|" + g.BedingungId);
						break;
					case NimmQuestAn n when n.QuestId != null:
						var st = StatusIn(welt, jetzt, n.QuestId);
						if (st == QStatus.Gesperrt || st == QStatus.Verfuegbar || st == QStatus.SpaeterVerfuegbar)
							if (Melden(folgen, gemeldet, n.QuestId, FolgeArt.Start, 1, null))
								statuswechsel.Add((n.QuestId, QStatus.Gestartet));
						break;
					case SchliesseQuestAb s when s.QuestId != null:
						if (!IstEnde(StatusIn(welt, jetzt, s.QuestId))
						    && Melden(folgen, gemeldet, s.QuestId, FolgeArt.Abschluss, 1, null))
						{
							beendet.Add(s.QuestId);
							statuswechsel.Add((s.QuestId, QStatus.Erfolg));
						}
						break;
				}
			}

			for (var tiefe = 1; tiefe <= maxTiefe; tiefe++)
			{
				foreach (var q in welt.Quests)
				{
					if (q == null || q.Kapitel || beendet.Contains(q.Id))
						continue;
					var st = StatusIn(welt, jetzt, q.Id);
					if (IstEnde(st))
						continue;

					// --- Fehlschlag ---
					if (q.Fehlschlag.Any(b => Wahr(welt, jetzt, b, false) && !Wahr(welt, vorher, b, false)))
					{
						if (Melden(folgen, gemeldet, q.Id, FolgeArt.Fehlschlag, tiefe, null))
						{
							beendet.Add(q.Id);
							statuswechsel.Add((q.Id, QStatus.Fehlschlag));
						}
						continue;
					}

					// --- Ziele / Abschluss (nur laufende Aufgaben) ---
					if (st == QStatus.Gestartet || st == QStatus.Abgabebereit)
					{
						var notwendig = q.Ziel.Where(b => b.Notwendig).ToList();
						var neu = q.Ziel.Where(b => ZielErledigt(welt, jetzt, q, b) && !ZielErledigt(welt, vorher, q, b)).ToList();
						if (neu.Count > 0 && notwendig.Count > 0)
						{
							if (notwendig.All(b => ZielErledigt(welt, jetzt, q, b)))
							{
								if (q.AutoAbschluss)
								{
									if (Melden(folgen, gemeldet, q.Id, FolgeArt.Abschluss, tiefe, null))
									{
										beendet.Add(q.Id);
										statuswechsel.Add((q.Id, QStatus.Erfolg));
									}
									continue;
								}
								if (Melden(folgen, gemeldet, q.Id, FolgeArt.Abgabebereit, tiefe, null))
									statuswechsel.Add((q.Id, QStatus.Abgabebereit));
							}
							else
							{
								foreach (var b in neu)
									Melden(folgen, gemeldet, q.Id, FolgeArt.Ziel, tiefe, b.Id);
							}
						}
					}

					// --- Start / Freischaltung ---
					if (st == QStatus.Gesperrt || st == QStatus.Verfuegbar || st == QStatus.SpaeterVerfuegbar)
					{
						var jetztOk = StartErfuellt(welt, jetzt, q);
						var vorherOk = StartErfuellt(welt, vorher, q);
						if (jetztOk && !vorherOk)
						{
							if (q.AutoStart)
							{
								if (Melden(folgen, gemeldet, q.Id, FolgeArt.Start, tiefe, null))
									statuswechsel.Add((q.Id, QStatus.Gestartet));
							}
							else if (st == QStatus.Gesperrt)
							{
								if (Melden(folgen, gemeldet, q.Id, FolgeArt.Freischaltung, tiefe, null))
									statuswechsel.Add((q.Id, QStatus.Verfuegbar));
							}
						}
					}
				}

				if (statuswechsel.Count == 0)
					break;

				// Statuswechsel dieser Runde fuer die naechste Runde uebernehmen,
				// samt den Variablen, die VisitAPI dabei setzt.
				foreach (var (quest, status) in statuswechsel)
				{
					naechste.Status[quest] = status;
					var schluessel = status == QStatus.Erfolg ? "Success"
						: status == QStatus.Fehlschlag ? "Fail"
						: status == QStatus.Gestartet ? "Started" : null;
					var qi = schluessel == null ? null : welt.Quests.FirstOrDefault(x => x != null && x.Id == quest);
					if (qi != null && qi.SetztVariablen.TryGetValue(schluessel, out var vars))
					{
						foreach (var kv in vars)
							naechste.Var[kv.Key] = kv.Value;
						NachrechnenGruppen(welt, naechste, vars.Keys.ToList());
					}
				}
				statuswechsel.Clear();
				jetzt = naechste;
				naechste = jetzt.Kopie();
			}

			return folgen;
		}

		private static bool Melden(List<Folge> folgen, HashSet<string> gemeldet, string quest, FolgeArt art, int tiefe, string bedingung)
		{
			if (!gemeldet.Add(quest + "|" + art + "|" + bedingung))
				return false;
			folgen.Add(new Folge { QuestId = quest, Art = art, Tiefe = tiefe, BedingungId = bedingung });
			return true;
		}

		public static bool IstEnde(QStatus s)
		{
			return s == QStatus.Erfolg || s == QStatus.Fehlschlag || s == QStatus.FehlschlagNeustart
			       || s == QStatus.AlsFehlschlag || s == QStatus.Abgelaufen;
		}

		private static QStatus StatusIn(IWelt welt, Stand stand, string quest)
		{
			return stand.Status.TryGetValue(quest, out var s) ? s : welt.Status(quest);
		}

		private static int VarIn(IWelt welt, Stand stand, string id)
		{
			return stand.Var.TryGetValue(id, out var v) ? v : welt.Variable(id);
		}

		private static void NachrechnenGruppen(IWelt welt, Stand stand, List<string> geaendert)
		{
			if (welt.Gruppen == null)
				return;
			foreach (var id in geaendert)
				welt.Gruppen.Nachrechnen(id, v => VarIn(welt, stand, v), (g, s) => stand.Var[g] = s);
		}

		// Auswertung einer Bedingung. null = Typ nicht auswertbar.
		private static bool? Auswerten(IWelt welt, Stand stand, Bedingung b)
		{
			var ziel = b.Ziele.Count > 0 ? b.Ziele[0] : null;
			switch (b.Typ)
			{
				case "GlobalVariableValue":
					return ziel != null && Vergleiche(VarIn(welt, stand, ziel), b.Wert, b.Vergleich);
				case "Quest":
					return b.Ziele.Any(z => b.Status.Contains((int)StatusIn(welt, stand, z)));
				case "Level":
					return Vergleiche(welt.Level, b.Wert, b.Vergleich);
				case "TraderLoyalty":
				case "TraderStanding":
					var w = ziel == null ? null : welt.HaendlerWert(b.Typ, ziel);
					return w.HasValue ? Vergleiche(w.Value, b.Wert, b.Vergleich) : (bool?)null;
				default:
					return null;
			}
		}

		// Fail-Bedingungen: unbekannte Typen gelten als "nicht ausgeloest".
		private static bool Wahr(IWelt welt, Stand stand, Bedingung b, bool unbekannt)
		{
			return Auswerten(welt, stand, b) ?? unbekannt;
		}

		private static bool ZielErledigt(IWelt welt, Stand stand, QuestInfo q, Bedingung b)
		{
			if (welt.BedingungErledigt(q.Id, b.Id) || stand.Abgegeben.Contains(q.Id + "|" + b.Id))
				return true;
			// Nur Bedingungen, die rein von Variablen/Status abhaengen, rechnen
			// wir selbst aus - Level oder Ansehen erfuellen kein Aufgabenziel.
			if (b.Typ == "GlobalVariableValue" || b.Typ == "Quest")
				return Auswerten(welt, stand, b) == true;
			return false;
		}

		private static bool StartErfuellt(IWelt welt, Stand stand, QuestInfo q)
		{
			// Unbekannte Start-Bedingungen blockieren nicht (sie aendern sich
			// durch ein Gespraech ohnehin nicht).
			if (!q.Start.All(b => Wahr(welt, stand, b, true)))
				return false;
			if (q.StartNach.Count > 0)
				return q.StartNach.Any(id => StatusIn(welt, stand, id) == QStatus.Erfolg);
			return true;
		}

		public static bool Vergleiche(double ist, double soll, string vergleich)
		{
			switch (vergleich)
			{
				case "==": return Math.Abs(ist - soll) < 0.0001;
				case "!=": return Math.Abs(ist - soll) >= 0.0001;
				case ">": return ist > soll;
				case "<": return ist < soll;
				case "<=": return ist <= soll;
				default: return ist >= soll;   // ">=" ist Vorgabe im Spiel
			}
		}
	}
}
