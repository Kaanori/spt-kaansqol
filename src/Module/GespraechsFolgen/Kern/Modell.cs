// ---------------------------------------------------------------
//  Kern-Modell der Gespraechsfolgen.
//
//  Absichtlich OHNE Bezug auf Spiel-Typen (EFT, Unity): derselbe Kern
//  laeuft im Spiel und im Test-Programm unter Test/, das die Story-
//  Dateien direkt liest. So laesst sich die Vorhersage offline pruefen.
// ---------------------------------------------------------------
using System.Collections.Generic;

namespace KaansQoL.GespraechsFolgen.Kern
{
	public enum Seite { Spieler, Npc }

	// Wo eine gesetzte Variable landet - wie ESaveStateType im Spiel:
	// Dialog = nur dieses Gespraech, Sitzung = bis zum Spielende,
	// Profil = dauerhaft (nur DIESE sehen die Aufgaben-Bedingungen).
	public enum Speicher { Dialog, Sitzung, Profil }

	// Gleiche Zahlen wie EQuestStatus im Spiel und QuestStatusEnum im Server.
	public enum QStatus
	{
		Gesperrt = 0, Verfuegbar = 1, Gestartet = 2, Abgabebereit = 3, Erfolg = 4,
		Fehlschlag = 5, FehlschlagNeustart = 6, AlsFehlschlag = 7, Abgelaufen = 8, SpaeterVerfuegbar = 9
	}

	// ---- Aktionen einer Gespraechszeile (nur die, die fuer Folgen zaehlen) ----
	public abstract class Aktion { }

	public sealed class SetzeVariable : Aktion
	{
		public string Id;
		public int Wert;
		public Speicher Ort;
	}

	public sealed class WechsleDialog : Aktion
	{
		public string DialogId;
		public string StartPunkt;   // kann null sein
	}

	// Gespraech endet (Beenden, Handel, Aufgabenliste)
	public sealed class Beenden : Aktion { }

	public sealed class NimmQuestAn : Aktion { public string QuestId; }

	public sealed class SchliesseQuestAb : Aktion { public string QuestId; }

	public sealed class GibAb : Aktion
	{
		public string QuestId;
		public string BedingungId;
		public bool Teilweise;
	}

	// ---- Gespraechsdaten ----
	public interface IZeile
	{
		string Id { get; }
		Seite Seite { get; }
		IReadOnlyList<Aktion> Aktionen { get; }
		// Ausloeser (Trigger) der Zeile gegen den simulierten Stand pruefen.
		bool Ausloeser(DialogStand stand);
	}

	public interface IDialogVorlage
	{
		string Id { get; }
		string HauptVariable { get; }
		IReadOnlyList<IZeile> Zeilen { get; }
		int StartWert(string startPunkt);
	}

	public interface IDialogQuelle
	{
		// null, wenn der Dialog unbekannt ist
		IDialogVorlage Vorlage(string dialogId);
	}

	// ---- Aufgaben ----
	public sealed class Bedingung
	{
		public string Id;
		public string Typ;                       // conditionType
		public List<string> Ziele = new List<string>();
		public double Wert;
		public string Vergleich = ">=";
		public List<int> Status = new List<int>();
		public bool Notwendig = true;
	}

	public sealed class QuestInfo
	{
		public string Id;
		public string Haendler;
		public List<Bedingung> Start = new List<Bedingung>();
		public List<Bedingung> Ziel = new List<Bedingung>();
		public List<Bedingung> Fehlschlag = new List<Bedingung>();

		// Aus den VisitAPI-Kennzeichen (/visitapi/quest/flags)
		public bool AutoStart;
		public bool AutoAbschluss;
		public bool Kapitel;
		public bool Versteckt;
		public List<string> StartNach = new List<string>();   // "startAfter": startet, sobald EINE davon erledigt ist
		// "setVariables": Status ("Started"/"Success"/"Fail") -> Variable -> Wert
		public Dictionary<string, Dictionary<string, int>> SetztVariablen =
			new Dictionary<string, Dictionary<string, int>>();
	}

	public interface IWelt
	{
		IEnumerable<QuestInfo> Quests { get; }
		QStatus Status(string questId);
		bool BedingungErledigt(string questId, string bedingungId);
		int Variable(string id);                       // Profilvariable, Stand JETZT
		int Level { get; }
		double? HaendlerWert(string typ, string haendlerId);   // TraderLoyalty / TraderStanding
		VariablenGruppen Gruppen { get; }
	}
}
