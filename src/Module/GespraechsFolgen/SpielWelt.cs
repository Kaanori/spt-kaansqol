// ---------------------------------------------------------------
//  Die Welt, gegen die der FolgenRechner rechnet: Aufgabenstatus,
//  erledigte Bedingungen, Profilvariablen, Level, Haendlerwerte -
//  alles aus dem laufenden Profil, nur lesend.
// ---------------------------------------------------------------
using System.Collections.Generic;
using EFT;
using EFT.Quests;
using KaansQoL.GespraechsFolgen.Kern;

namespace KaansQoL.GespraechsFolgen
{
	internal sealed class SpielWelt : IWelt
	{
		private readonly Profile _profil;
		private readonly QuestController _qc;
		private readonly Dictionary<string, QuestInfo> _quests;
		private readonly Dictionary<string, QuestDataClass> _daten = new Dictionary<string, QuestDataClass>();

		public SpielWelt(Profile profil, QuestController qc, Dictionary<string, QuestInfo> quests, VariablenGruppen gruppen)
		{
			_profil = profil;
			_qc = qc;
			_quests = quests;
			Gruppen = gruppen;
			if (profil?.QuestsData != null)
				foreach (var d in profil.QuestsData)
					if (d?.Id != null)
						_daten[d.Id] = d;
		}

		public IEnumerable<QuestInfo> Quests => _quests.Values;
		public VariablenGruppen Gruppen { get; }
		public int Level => _profil?.Info?.Level ?? 1;

		public QStatus Status(string questId)
		{
			if (questId == null)
				return QStatus.Gesperrt;
			try
			{
				// Live-Status des Clients zuerst, er ist am aktuellsten
				var q = _qc?.Quests?.GetConditional(questId);
				if (q != null)
					return (QStatus)(int)q.QuestStatus;
			}
			catch
			{
				// weiter mit den Profildaten
			}
			return _daten.TryGetValue(questId, out var d) ? (QStatus)(int)d.Status : QStatus.Gesperrt;
		}

		public bool BedingungErledigt(string questId, string bedingungId)
		{
			if (questId == null || !_daten.TryGetValue(questId, out var d) || d.CompletedConditions == null)
				return false;
			var id = Wandler.Mongo(bedingungId);
			return id.HasValue && d.CompletedConditions.Contains(id.Value);
		}

		public int Variable(string id)
		{
			var m = Wandler.Mongo(id);
			if (!m.HasValue || _profil?.ProfileVariables == null)
				return 0;
			return _profil.ProfileVariables.GetVariableValue(m.Value);
		}

		public double? HaendlerWert(string typ, string haendlerId)
		{
			var m = Wandler.Mongo(haendlerId);
			if (!m.HasValue || _profil?.TradersInfo == null || !_profil.TradersInfo.TryGetValue(m.Value, out var t) || t == null)
				return null;
			return typ == "TraderLoyalty" ? t.LoyaltyLevel : t.Standing;
		}

		public bool Versteckt(string questId) =>
			questId != null && _quests.TryGetValue(questId, out var q) && q.Versteckt;
	}
}
