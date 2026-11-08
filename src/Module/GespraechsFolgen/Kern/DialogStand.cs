// ---------------------------------------------------------------
//  Simulierter Variablen-Stand waehrend eines Gespraechs.
//
//  Bildet BaseTraderDialogController.GetVariableValue/SetVariableValue
//  nach, ohne den echten Stand anzufassen:
//      lesen:  Dialog-Variablen -> Sitzung -> Profil
//      setzen: immer Dialog, ab "Sitzung" auch Sitzung, bei "Profil"
//              zusaetzlich Profil
//  Alles Geschriebene landet in eigenen Ueberlagerungen; die echten
//  Werte werden nur ueber die drei Lese-Funktionen abgefragt.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;

namespace KaansQoL.GespraechsFolgen.Kern
{
	public sealed class DialogStand
	{
		private readonly Func<string, int?> _echtDialog;
		private readonly Func<string, int?> _echtSitzung;
		private readonly Func<string, int> _echtProfil;

		private readonly Dictionary<string, int> _dialog = new Dictionary<string, int>();
		private readonly Dictionary<string, int> _sitzung = new Dictionary<string, int>();
		private readonly Dictionary<string, int> _profil = new Dictionary<string, int>();
		private bool _dialogGeleert;

		private readonly Dictionary<string, int> _zufall = new Dictionary<string, int>();
		private readonly Random _wuerfel = new Random();

		public VariablenGruppen Gruppen;

		// Ist irgendwo ein Zufalls-Ausloeser ausgewertet worden?
		public bool Zufall { get; private set; }

		// Nur die Profil-Aenderungen zaehlen fuer Aufgaben-Bedingungen.
		public IReadOnlyDictionary<string, int> ProfilAenderungen => _profil;

		public DialogStand(Func<string, int?> echtDialog, Func<string, int?> echtSitzung, Func<string, int> echtProfil)
		{
			_echtDialog = echtDialog;
			_echtSitzung = echtSitzung;
			_echtProfil = echtProfil ?? (_ => 0);
		}

		public int Lies(string id)
		{
			if (id == null)
				return 0;
			if (_dialog.TryGetValue(id, out var w))
				return w;
			if (!_dialogGeleert && _echtDialog != null)
			{
				var e = _echtDialog(id);
				if (e.HasValue)
					return e.Value;
			}
			if (_sitzung.TryGetValue(id, out w))
				return w;
			if (_echtSitzung != null)
			{
				var e = _echtSitzung(id);
				if (e.HasValue)
					return e.Value;
			}
			return LiesProfil(id);
		}

		public int LiesProfil(string id)
		{
			return _profil.TryGetValue(id, out var w) ? w : _echtProfil(id);
		}

		public void Setze(string id, int wert, Speicher ort)
		{
			if (id == null)
				return;
			_dialog[id] = wert;
			if (ort == Speicher.Dialog)
				return;
			_sitzung[id] = wert;
			if (ort != Speicher.Profil)
				return;
			_profil[id] = wert;
			// VisitAPI rechnet Gruppen-Variablen (Summe ihrer Mitglieder)
			// bei jeder Profil-Aenderung nach - hier genauso.
			Gruppen?.Nachrechnen(id, LiesProfil, (g, s) => _profil[g] = s);
		}

		// Wechsel in einen ANDEREN Dialog: das Spiel leert dann die
		// Dialog-Variablen (ClearTempData) - auch die echten.
		public void DialogLeeren()
		{
			_dialog.Clear();
			_dialogGeleert = true;
		}

		// Zufalls-Ausloeser: ein Wert pro Variablenname, wie im Spiel.
		public int Zufallswert(string name, uint max)
		{
			Zufall = true;
			var schluessel = name ?? "";
			if (!_zufall.TryGetValue(schluessel, out var w))
			{
				w = _wuerfel.Next(0, (int)Math.Min(int.MaxValue - 1, max) + 1);
				_zufall[schluessel] = w;
			}
			return w;
		}
	}

	// Gruppen-Variablen aus /visitapi/variable/groups: Wert = Summe der Mitglieder.
	public sealed class VariablenGruppen
	{
		private readonly Dictionary<string, List<string>> _mitglieder = new Dictionary<string, List<string>>();
		private readonly Dictionary<string, List<string>> _gruppenVon = new Dictionary<string, List<string>>();

		public int Anzahl => _mitglieder.Count;

		public void Hinzufuegen(string gruppe, List<string> mitglieder)
		{
			if (string.IsNullOrEmpty(gruppe) || mitglieder == null || mitglieder.Count == 0)
				return;
			_mitglieder[gruppe] = mitglieder;
			foreach (var m in mitglieder)
			{
				if (!_gruppenVon.TryGetValue(m, out var l))
					_gruppenVon[m] = l = new List<string>();
				if (!l.Contains(gruppe))
					l.Add(gruppe);
			}
		}

		public void Nachrechnen(string geaendert, Func<string, int> lies, Action<string, int> setze, int tiefe = 0)
		{
			// Schutz gegen Gruppen, die sich gegenseitig enthalten
			if (tiefe > 8 || !_gruppenVon.TryGetValue(geaendert, out var gruppen))
				return;
			foreach (var g in gruppen)
			{
				var summe = 0;
				foreach (var m in _mitglieder[g])
					summe += lies(m);
				if (lies(g) != summe)
				{
					setze(g, summe);
					// Gruppen koennen selbst Mitglied einer Gruppe sein
					Nachrechnen(g, lies, setze, tiefe + 1);
				}
			}
		}
	}
}
