// ---------------------------------------------------------------
//  Haken in die Antwortzeilen des Gespraechsfensters.
//
//  Das Spiel baut jede Antwort ueber TraderDialogWindowOptionRow.Show
//  (aufgerufen aus TraderDialogWindow.AssembleRows). Show setzt den Text
//  jedes Mal neu - wir haengen im Postfix unsere Zeilen an (Inline) oder
//  nur ein Kennzeichen [!]/[+] mit Details beim Draufzeigen (Tooltip).
//
//  Berechnet wird einmal pro angezeigter Auswahl fuer ALLE Antworten,
//  damit "Keine Aufgabenfolgen" (grau) nur erscheint, wenn eine andere
//  Antwort derselben Auswahl wirklich Folgen hat. Sonst stuende das
//  unter jeder Handels- oder Plauder-Antwort.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using EFT.Dialogs;
using EFT.UI;
using KaansQoL.GespraechsFolgen.Kern;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KaansQoL.GespraechsFolgen
{
	internal static class Anzeige
	{
		[HarmonyPatch(typeof(TraderDialogWindowOptionRow), nameof(TraderDialogWindowOptionRow.Show))]
		internal static class ZeilenPatch
		{
			[HarmonyPostfix]
			private static void Postfix(TraderDialogWindowOptionRow __instance, BaseTraderDialogLine line)
			{
				try
				{
					Zeigen(__instance, line);
				}
				catch (Exception e)
				{
					if (!_fehlerGemeldet)
					{
						_fehlerGemeldet = true;
						Plugin.Log.LogWarning("Preview failed (shown once): " + e);
					}
				}
			}
		}

		private static bool _fehlerGemeldet;

		// Zwischenspeicher fuer die gerade angezeigte Auswahl
		private static BaseTraderDialog _dialog;
		private static int _stand = -1;
		private static readonly Dictionary<BaseTraderDialogLine, Vorschau> _vorschau = new Dictionary<BaseTraderDialogLine, Vorschau>();
		private static bool _irgendwas;

		private static void Zeigen(TraderDialogWindowOptionRow zeile, BaseTraderDialogLine line)
		{
			var hinweis = zeile.GetComponent<FolgenHinweis>();
			hinweis?.Setzen(null);

			if (!Modul.Eingeschaltet.Value || line == null || line.DialogSide != EDialogSide.Player || zeile._text == null)
				return;
			if (!(line.Context is BaseTraderDialogController dc) || dc.CurrentDialog == null)
				return;

			var opt = Modul.Optionen();
			var vorschau = Holen(dc, line, opt);
			if (vorschau == null)
				return;

			var zeilen = Darstellung.Zeilen(vorschau, opt, Namen.Quest, Namen.Bedingung, grauWennLeer: _irgendwas);
			if (zeilen.Count == 0)
				return;

			if (Modul.Art.Value == Anzeigeart.Inline)
			{
				zeile._text.text += "\n<size=78%>" + string.Join("\n", zeilen) + "</size>";
				return;
			}

			// Tooltip: nur ein Kennzeichen hinter der Antwort
			var kennz = vorschau.HatFehlschlag ? Darstellung.Farbe(Darstellung.Rot, " [!]")
				: vorschau.HatPositives ? Darstellung.Farbe(Darstellung.Gruen, " [+]")
				: Darstellung.Farbe(Darstellung.Grau, " [-]");
			zeile._text.text += kennz;
			if (hinweis == null)
				hinweis = zeile.gameObject.AddComponent<FolgenHinweis>();
			hinweis.Setzen(string.Join("\n", zeilen));
		}

		private static Vorschau Holen(BaseTraderDialogController dc, BaseTraderDialogLine line, Anzeigeoptionen opt)
		{
			var dialog = dc.CurrentDialog;
			var stand = Daten.Stand * 1000 + Modul.EinstellungsStand;
			if (!ReferenceEquals(dialog, _dialog) || stand != _stand)
			{
				_dialog = dialog;
				_stand = stand;
				_vorschau.Clear();
				_irgendwas = false;
				var antworten = dialog.Lines?.Where(l => l != null && l.DialogSide == EDialogSide.Player).ToList()
				                ?? new List<BaseTraderDialogLine>();
				foreach (var a in antworten)
				{
					var v = Rechnen(dc, a, opt);
					_vorschau[a] = v;
					if (v != null && !v.Leer)
						_irgendwas = true;
				}
			}
			if (_vorschau.TryGetValue(line, out var gemerkt))
				return gemerkt;
			// Zeile gehoert nicht zur aktuellen Auswahl (sollte nicht vorkommen)
			return Rechnen(dc, line, opt);
		}

		private static Vorschau Rechnen(BaseTraderDialogController dc, BaseTraderDialogLine line, Anzeigeoptionen opt)
		{
			var kontext = (IDialogContext)dc;
			var profil = kontext.Profile;
			if (profil == null || line.Template == null)
				return null;

			// Schatten-Stand: liest den echten Stand, schreibt nur in sich selbst
			var stand = new DialogStand(
				id => Wandler.Mongo(id) is MongoID m && dc.dictionary_1.TryGetValue(m, out var v) ? v : (int?)null,
				id => Wandler.Mongo(id) is MongoID m && dc.dictionary_0.TryGetValue(m, out var v) ? v : (int?)null,
				id => Wandler.Mongo(id) is MongoID m ? profil.ProfileVariables.GetVariableValue(m) : 0)
			{
				Gruppen = Daten.Gruppen,
			};
			var sim = new SimKontext(kontext, stand);
			var quelle = new SpielQuelle(sim);
			// Die gewaehlte Zeile mit IHREN Aktionen (das Spiel haengt bei
			// eingebetteten Aufgaben-Gespraechen noch einen Dialogwechsel an).
			var gewaehlt = new SpielZeile(line.Template, Wandler.Aktionen(line.Actions), sim);

			var erg = DialogSimulator.Simuliere(quelle, dc.CurrentDialog.Id.ToString(), gewaehlt, stand);

			var qc = kontext.QuestController;
			var welt = new SpielWelt(profil, qc, Daten.Quests(qc), Daten.Gruppen);
			var folgen = FolgenRechner.Berechne(welt, erg.ProfilAenderungen, erg.QuestAktionen, opt.MaxTiefe);
			return Darstellung.Filtern(folgen, erg.Zufall, opt, welt.Versteckt);
		}
	}

	// Tooltip beim Draufzeigen. Eigene kleine Komponente statt
	// HoverTooltipArea: die holt sich den Tooltip schon in Awake und
	// wirft, wenn ItemUiContext gerade fehlt.
	internal sealed class FolgenHinweis : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
	{
		private string _text;
		private bool _offen;

		public void Setzen(string text)
		{
			_text = text;
			if (_offen)
				Schliessen();
		}

		public void OnPointerEnter(PointerEventData eventData)
		{
			if (string.IsNullOrEmpty(_text) || !Modul.Eingeschaltet.Value)
				return;
			try
			{
				var tooltip = ItemUiContext.Instance?.Tooltip;
				if (tooltip == null)
					return;
				tooltip.Show(_text, null, 0f, null);
				_offen = true;
			}
			catch
			{
				_offen = false;
			}
		}

		public void OnPointerExit(PointerEventData eventData) => Schliessen();

		public void OnPointerClick(PointerEventData eventData) => Schliessen();

		private void OnDisable() => Schliessen();

		private void Schliessen()
		{
			if (!_offen)
				return;
			_offen = false;
			try
			{
				ItemUiContext.Instance?.Tooltip?.Close();
			}
			catch
			{
				// egal
			}
		}
	}
}
