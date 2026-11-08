// ---------------------------------------------------------------
//  Gespraechsfolgen - Server-Teil  (frueher eigener Server-Mod GespraechsFolgen)
//
//  Route und Antwort 1:1 uebernommen. Ist der alte Einzel-Mod noch installiert,
//  antwortet er selbst - dieser Router reicht dann nur durch, was schon da ist.
//
//  Ein einziger, rein LESENDER Endpunkt:
//
//      /gespraechsfolgen/quests
//
//  Antwort (im ueblichen "data"-Umschlag):
//      { "<questId>": { "t": "<haendlerId>",
//                       "s": [Bedingung...],   // AvailableForStart
//                       "f": [Bedingung...],   // AvailableForFinish
//                       "x": [Bedingung...] }, // Fail
//        ... }
//  Bedingung: { "i": id, "c": conditionType, "n": notwendig (bool),
//               und nur bei auswertbaren Typen zusaetzlich
//               "z": [Ziele], "v": Wert, "m": Vergleich, "st": [Status] }
//
//  Warum ueberhaupt ein Server-Teil: Der Client bekommt von SPT nur die
//  Aufgaben, deren Vorgaenger schon erledigt sind. Gesperrte Aufgaben
//  kennt er gar nicht - also koennte er nie sagen "diese Antwort schaltet
//  Aufgabe X frei". Hier liegen alle Vorlagen im Speicher.
//
//  Es wird nichts veraendert, nichts gespeichert. Die Antwort wird beim
//  ersten Abruf gebaut (dann sind sicher alle anderen Mods geladen,
//  auch VisitAPI mit seinen Story-Aufgaben) und danach wiederverwendet.
// ---------------------------------------------------------------
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace KaansQoL.Server;

[Injectable]
public class AufgabenRouter(JsonUtil jsonUtil, HttpResponseUtil antwort, TemplateTable vorlagen)
	: StaticRouter(jsonUtil, new List<RouteAction>
	{
		new RouteAction("/gespraechsfolgen/quests",
			(url, info, sessionId, output, abbruch) =>
				AlteMods.Da("GespraechsFolgen", "GespraechsFolgenServer.dll")
					? new ValueTask<object>(output ?? "")
					: new ValueTask<object>(antwort.GetBody(Zusammenstellen(vorlagen), BackendErrorCodes.None, null, true)),
			typeof(EmptyRequestData))
	})
{
	// Typen, deren Ziel/Wert der Client wirklich auswertet. Alle anderen
	// (Finde Gegenstand, Toete X ...) kommen nur mit Id und Typ - der Client
	// schaut fuer die nur nach, ob sie im Profil schon als erledigt stehen.
	private static readonly HashSet<string> Auswertbar = new(StringComparer.Ordinal)
	{
		"GlobalVariableValue", "Quest", "Level", "TraderLoyalty", "TraderStanding"
	};

	private static readonly object Sperre = new();
	private static Dictionary<string, object>? _fertig;

	private static Dictionary<string, object> Zusammenstellen(TemplateTable vorlagen)
	{
		lock (Sperre)
		{
			if (_fertig != null)
				return _fertig;

			var alle = new Dictionary<string, object>(StringComparer.Ordinal);
			foreach (var quest in vorlagen.Quests.Values)
			{
				if (quest?.Conditions == null)
					continue;
				alle[quest.Id.ToString()] = new Dictionary<string, object?>
				{
					["t"] = quest.TraderId.ToString(),
					["s"] = Liste(quest.Conditions.AvailableForStart),
					["f"] = Liste(quest.Conditions.AvailableForFinish),
					["x"] = Liste(quest.Conditions.Fail),
				};
			}
			return _fertig = alle;
		}
	}

	private static List<Dictionary<string, object?>> Liste(List<QuestCondition>? bedingungen)
	{
		var liste = new List<Dictionary<string, object?>>();
		if (bedingungen == null)
			return liste;

		foreach (var b in bedingungen)
		{
			if (b == null)
				continue;
			var typ = b.ConditionType ?? "";
			// "Notwendig" wie im Spiel (Condition.IsNecessary): entweder
			// ausdruecklich markiert oder eine Bedingung der obersten Ebene.
			var notwendig = b.IsNecessary == true || string.IsNullOrEmpty(b.ParentId);
			var eintrag = new Dictionary<string, object?>
			{
				["i"] = b.Id.ToString(),
				["c"] = typ,
				["n"] = notwendig,
			};
			if (Auswertbar.Contains(typ))
			{
				eintrag["z"] = Ziele(b.Target);
				eintrag["v"] = b.Value ?? 0;
				eintrag["m"] = b.CompareMethod ?? ">=";
				if (b.Status != null && b.Status.Count > 0)
					eintrag["st"] = b.Status.Select(s => (int)s).ToList();
			}
			liste.Add(eintrag);
		}
		return liste;
	}

	private static List<string> Ziele(SPTarkov.Server.Core.Utils.Json.ListOrT<string>? ziel)
	{
		if (ziel == null)
			return new List<string>();
		if (ziel.IsItem)
			return string.IsNullOrEmpty(ziel.Item) ? new List<string>() : new List<string> { ziel.Item! };
		return ziel.List?.Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();
	}
}
