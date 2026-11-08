// ---------------------------------------------------------------
//  Bruecke vom Kern zu den echten Spiel-Typen (EFT.Dialogs).
//
//  - SpielQuelle/SpielVorlage/SpielZeile: Dialogvorlagen aus DialogStorage
//  - SimKontext: ein IDialogContext, der fuer Variablen und Zufall auf
//    den Schatten-Stand umleitet und alles andere (Aufgaben, Inventar,
//    Haendler) beim echten Gespraech nachfragt. Damit koennen die
//    Ausloeser-Bedingungen des Spiels (VariableValue, QuestStatus,
//    HasItemForHandover ...) unveraendert ausgewertet werden.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using EFT;
using EFT.Dialogs;
using EFT.InventoryLogic;
using EFT.Quests;
using KaansQoL.GespraechsFolgen.Kern;

namespace KaansQoL.GespraechsFolgen
{
	internal sealed class SimKontext : IDialogContext
	{
		private readonly IDialogContext _echt;
		public DialogStand Stand;

		public SimKontext(IDialogContext echt, DialogStand stand)
		{
			_echt = echt;
			Stand = stand;
		}

		public Profile Profile => _echt.Profile;
		public Dictionary<MongoID, Profile.TraderInfo> TradersInfo => _echt.TradersInfo;
		public IReadOnlyDictionary<MongoID, TaskConditionCounter> TaskConditionCounters => _echt.TaskConditionCounters;
		public List<QuestDataClass> QuestsData => _echt.QuestsData;
		public Dictionary<ETraderServiceType, GlobalConfiguration.ServiceData> ServicesData => _echt.ServicesData;
		public QuestController QuestController => _echt.QuestController;
		public InventoryController InventoryController => _echt.InventoryController;
		public Profile.TraderInfo Trader => _echt.Trader;
		public string SelectedQuestId => _echt.SelectedQuestId;
		public ETraderServiceType SelectedServiceType => _echt.SelectedServiceType;
		public string SelectedSubServiceId => _echt.SelectedSubServiceId;

		public int GetVariableValue(MongoID variableId) => Stand.Lies(variableId.ToString());

		public void SetVariableValue(DialogSetVariableAction.SaveStateData saveData)
		{
			if (saveData != null)
				Stand.Setze(saveData.VariableId.ToString(), saveData.Value, Wandler.Ort(saveData.Storage));
		}

		public int GetRandomValue(RandomLineCondition condition) =>
			Stand.Zufallswert(condition?.VariableName.ToString(), condition?.MaxValue ?? 0);

		public void StartDialog(MongoID traderId, MongoID? entryDialogId = null, ITraderAnimationController animationController = null)
		{
			// Simulation startet nie ein echtes Gespraech
		}
	}

	internal sealed class SpielZeile : IZeile
	{
		private readonly DialogLineTemplate _vorlage;
		private readonly SimKontext _kontext;

		public SpielZeile(DialogLineTemplate vorlage, IReadOnlyList<Aktion> aktionen, SimKontext kontext)
		{
			_vorlage = vorlage;
			_kontext = kontext;
			Aktionen = aktionen;
			Id = vorlage.Id.ToString();
			Seite = vorlage.DialogSide == EDialogSide.Npc ? Kern.Seite.Npc : Kern.Seite.Spieler;
		}

		public string Id { get; }
		public Seite Seite { get; }
		public IReadOnlyList<Aktion> Aktionen { get; }

		public bool Ausloeser(DialogStand stand)
		{
			var t = _vorlage.Trigger;
			if (t == null)
				return true;
			try
			{
				_kontext.Stand = stand;
				return t.Test(_kontext);
			}
			catch
			{
				return false;   // lieber eine Zeile zu wenig als ein Absturz
			}
		}
	}

	internal sealed class SpielVorlage : IDialogVorlage
	{
		private readonly TraderDialogTemplate _t;

		public SpielVorlage(TraderDialogTemplate t, SimKontext kontext)
		{
			_t = t;
			Id = t.Id.ToString();
			HauptVariable = t.MainVariable.ToString();
			var zeilen = new List<IZeile>();
			if (t.Lines != null)
				foreach (var l in t.Lines)
					if (l != null)
						zeilen.Add(new SpielZeile(l, Wandler.AktionenGemerkt(l), kontext));
			Zeilen = zeilen;
		}

		public string Id { get; }
		public string HauptVariable { get; }
		public IReadOnlyList<IZeile> Zeilen { get; }

		public int StartWert(string startPunkt)
		{
			try
			{
				var id = Wandler.Mongo(startPunkt);
				return id.HasValue ? _t.GetStartVariableValue(id.Value) : 0;
			}
			catch
			{
				return 0;
			}
		}
	}

	internal sealed class SpielQuelle : IDialogQuelle
	{
		private readonly SimKontext _kontext;
		private readonly Dictionary<string, SpielVorlage> _gebaut = new Dictionary<string, SpielVorlage>();

		public SpielQuelle(SimKontext kontext)
		{
			_kontext = kontext;
		}

		public IDialogVorlage Vorlage(string dialogId)
		{
			if (dialogId == null)
				return null;
			if (_gebaut.TryGetValue(dialogId, out var v))
				return v;
			var id = Wandler.Mongo(dialogId);
			if (!id.HasValue || DialogStorage.Instance == null || !DialogStorage.Instance.TryGetTemplate(id.Value, out var t) || t == null)
				return null;
			v = new SpielVorlage(t, _kontext);
			_gebaut[dialogId] = v;
			return v;
		}
	}

	internal static class Wandler
	{
		private static readonly Dictionary<string, MongoID?> _ids = new Dictionary<string, MongoID?>(StringComparer.Ordinal);

		// Vorlagenzeilen aus DialogStorage leben das ganze Spiel lang ->
		// ihre umgewandelten Aktionen merken.
		private static readonly Dictionary<DialogLineTemplate, IReadOnlyList<Aktion>> _aktionen =
			new Dictionary<DialogLineTemplate, IReadOnlyList<Aktion>>();

		public static MongoID? Mongo(string s)
		{
			if (string.IsNullOrEmpty(s))
				return null;
			lock (_ids)
			{
				if (_ids.TryGetValue(s, out var m))
					return m;
				try
				{
					m = new MongoID(s);
				}
				catch
				{
					m = null;
				}
				_ids[s] = m;
				return m;
			}
		}

		public static Speicher Ort(DialogLineTemplate.ESaveStateType t)
		{
			switch (t)
			{
				case DialogLineTemplate.ESaveStateType.Profile: return Speicher.Profil;
				case DialogLineTemplate.ESaveStateType.Session: return Speicher.Sitzung;
				default: return Speicher.Dialog;
			}
		}

		public static IReadOnlyList<Aktion> AktionenGemerkt(DialogLineTemplate l)
		{
			if (_aktionen.TryGetValue(l, out var a))
				return a;
			a = Aktionen(l.Actions);
			_aktionen[l] = a;
			return a;
		}

		public static List<Aktion> Aktionen(IEnumerable<DialogAction> liste)
		{
			var erg = new List<Aktion>();
			if (liste == null)
				return erg;
			foreach (var a in liste)
			{
				switch (a)
				{
					case DialogSetVariableAction s when s.Variable != null:
						erg.Add(new SetzeVariable { Id = s.Variable.VariableId.ToString(), Wert = s.Variable.Value, Ort = Ort(s.Variable.Storage) });
						break;
					case DialogSwitchDialogAction w:
						erg.Add(new WechsleDialog { DialogId = w.DialogId.ToString(), StartPunkt = w.SplitterId?.ToString() });
						break;
					case DialogQuitAction _:
					case DialogTradingScreenAction _:
					case DialogQuestsScreenAction _:
						erg.Add(new Beenden());
						break;
					case DialogAcceptQuestAction q:
						erg.Add(new NimmQuestAn { QuestId = q.QuestId });
						break;
					case DialogFinishQuestAction f:
						erg.Add(new SchliesseQuestAb { QuestId = f.QuestId });
						break;
					case DialogHandoverItemAction h:
						erg.Add(new GibAb { QuestId = h.QuestId, BedingungId = h.ConditionId?.ToString(), Teilweise = h.PartialHandover });
						break;
				}
			}
			return erg;
		}
	}
}
