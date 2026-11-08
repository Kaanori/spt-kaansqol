// ---------------------------------------------------------------
//  Macht aus den Folgen den kurzen, farbigen Text unter der Antwort.
//  Farben: gruen = Abschluss/Start, rot = Fehlschlag, grau = nichts.
//  Rich-Text-Tags sind die von TextMeshPro (<color>, <size>).
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KaansQoL.GespraechsFolgen.Kern
{
	public enum Detailstufe { Direct = 1, Chain = 2, ChainAndObjectives = 3 }

	public sealed class Anzeigeoptionen
	{
		public bool NurFehlschlaege;
		public bool Freischaltungen = true;
		public Detailstufe Detail = Detailstufe.Chain;
		public bool Deutsch = true;
		public int MaxEintraege = 4;

		public int MaxTiefe => Detail == Detailstufe.Direct ? 1 : 2;
	}

	public sealed class Vorschau
	{
		public List<Folge> Sichtbar = new List<Folge>();
		public bool Zufall;
		public bool HatFehlschlag => Sichtbar.Any(f => f.Art == FolgeArt.Fehlschlag);
		public bool HatPositives => Sichtbar.Any(f => f.Art != FolgeArt.Fehlschlag);
		public bool Leer => Sichtbar.Count == 0;
	}

	public static class Darstellung
	{
		public const string Rot = "#E86A5F";
		public const string Gruen = "#7FD06B";
		public const string Hellgruen = "#B4DCA8";
		public const string Grau = "#9A9A9A";

		public static Vorschau Filtern(List<Folge> folgen, bool zufall, Anzeigeoptionen opt, Func<string, bool> versteckt)
		{
			var v = new Vorschau { Zufall = zufall };
			foreach (var f in folgen)
			{
				if (f.Tiefe > opt.MaxTiefe)
					continue;
				if (opt.NurFehlschlaege && f.Art != FolgeArt.Fehlschlag)
					continue;
				if (!opt.Freischaltungen && (f.Art == FolgeArt.Start || f.Art == FolgeArt.Freischaltung))
					continue;
				if (f.Art == FolgeArt.Ziel && opt.Detail != Detailstufe.ChainAndObjectives)
					continue;
				// Interne Hilfsaufgaben nur in der hoechsten Stufe
				if (opt.Detail != Detailstufe.ChainAndObjectives && versteckt != null && versteckt(f.QuestId))
					continue;
				v.Sichtbar.Add(f);
			}
			// Wichtigstes zuerst: Fehlschlaege, dann Abschluesse, Starts, Ziele;
			// innerhalb davon direkte Folgen vor Folge-Folgen.
			v.Sichtbar = v.Sichtbar
				.OrderBy(f => f.Tiefe)
				.ThenBy(f => Rang(f.Art))
				.ToList();
			return v;
		}

		private static int Rang(FolgeArt a)
		{
			switch (a)
			{
				case FolgeArt.Fehlschlag: return 0;
				case FolgeArt.Abschluss: return 1;
				case FolgeArt.Abgabebereit: return 2;
				case FolgeArt.Start: return 3;
				case FolgeArt.Freischaltung: return 4;
				default: return 5;
			}
		}

		// Zeilen als TMP-Rich-Text (ohne umschliessendes <size>).
		public static List<string> Zeilen(Vorschau v, Anzeigeoptionen opt, Func<string, string> questName,
			Func<string, string> bedingungsText, bool grauWennLeer)
		{
			var zeilen = new List<string>();
			if (v.Leer)
			{
				if (grauWennLeer)
					zeilen.Add(Farbe(Grau, opt.NurFehlschlaege
						? (opt.Deutsch ? "Kein Fehlschlag" : "No quest fails")
						: (opt.Deutsch ? "Keine Aufgabenfolgen" : "No quest effects")));
				return zeilen;
			}

			var anzahl = 0;
			foreach (var f in v.Sichtbar)
			{
				if (anzahl >= opt.MaxEintraege)
					break;
				var name = f.Art == FolgeArt.Ziel
					? (bedingungsText?.Invoke(f.BedingungId) ?? f.BedingungId)
					: (questName?.Invoke(f.QuestId) ?? f.QuestId);
				zeilen.Add(Farbe(FarbeFuer(f.Art), Text(f, opt.Deutsch) + Sauber(name)));
				anzahl++;
			}
			var rest = v.Sichtbar.Count - anzahl;
			if (rest > 0)
				zeilen.Add(Farbe(Grau, opt.Deutsch ? $"+{rest} weitere" : $"+{rest} more"));
			if (v.Zufall)
				zeilen.Add(Farbe(Grau, opt.Deutsch ? "(Zufall im Gespräch - kann abweichen)" : "(random branch - may differ)"));
			return zeilen;
		}

		private static string FarbeFuer(FolgeArt a)
		{
			switch (a)
			{
				case FolgeArt.Fehlschlag: return Rot;
				case FolgeArt.Ziel: return Hellgruen;
				default: return Gruen;
			}
		}

		private static string Text(Folge f, bool de)
		{
			// "» " (U+00BB) markiert Folge-Folgen - steckt sicher in jeder Schrift.
			var folge = f.Tiefe > 1;
			switch (f.Art)
			{
				case FolgeArt.Fehlschlag:
					return folge ? (de ? "» dadurch scheitert: " : "» then fails: ") : (de ? "Scheitert: " : "Fails: ");
				case FolgeArt.Abschluss:
					return folge ? (de ? "» dadurch erledigt: " : "» then completes: ") : (de ? "Wird erledigt: " : "Completes: ");
				case FolgeArt.Abgabebereit:
					return folge ? (de ? "» dadurch abgabebereit: " : "» then ready to turn in: ") : (de ? "Abgabebereit: " : "Ready to turn in: ");
				case FolgeArt.Start:
					return folge ? (de ? "» dadurch startet: " : "» then starts: ") : (de ? "Startet: " : "Starts: ");
				case FolgeArt.Freischaltung:
					return folge ? (de ? "» dadurch freigeschaltet: " : "» then unlocks: ") : (de ? "Schaltet frei: " : "Unlocks: ");
				default:
					return folge ? (de ? "» Ziel erfüllt: " : "» objective done: ") : (de ? "Ziel erfüllt: " : "Objective done: ");
			}
		}

		public static string Farbe(string farbe, string text) => "<color=" + farbe + ">" + text + "</color>";

		// Namen aus den Sprachdateien koennen Platzhalter, Zeilenumbrueche
		// oder eigene Tags enthalten - fuer eine Kurzzeile alles raus.
		public static string Sauber(string s)
		{
			if (string.IsNullOrEmpty(s))
				return "?";
			var sb = new StringBuilder(s.Length);
			var inTag = false;
			foreach (var c in s)
			{
				if (c == '<') { inTag = true; continue; }
				if (c == '>' && inTag) { inTag = false; continue; }
				if (inTag) continue;
				sb.Append(c == '\n' || c == '\r' || c == '\t' ? ' ' : c);
			}
			var t = sb.ToString().Replace("{0}", "").Replace("{1}", "").Trim();
			while (t.Contains("  "))
				t = t.Replace("  ", " ");
			if (t.Length > 70)
				t = t.Substring(0, 67).TrimEnd() + "...";
			return t.Length == 0 ? "?" : t;
		}
	}
}
