// ---------------------------------------------------------------
//  Holt einmal pro Spielstart (nach dem Login) drei Tabellen vom Server.
//  Alle drei Abfragen sind rein lesend:
//
//    /gespraechsfolgen/quests     - unser Server-Teil: Bedingungen ALLER
//                                   Aufgaben (auch gesperrter)
//    /visitapi/quest/flags        - VisitAPI: autoStart, autoFinish,
//                                   startAfter, setVariables, chapter ...
//    /visitapi/variable/groups    - VisitAPI: Gruppen-Variablen (Summen)
//
//  Fehlt unser Server-Teil, nehmen wir die Aufgaben, die der Client
//  ohnehin kennt (QuestController). Dann fehlen nur Vorhersagen fuer
//  Aufgaben, die noch gar nicht sichtbar sind (Freischaltungen).
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.Quests;
using KaansQoL.GespraechsFolgen.Kern;
using Newtonsoft.Json.Linq;
using SPT.Common.Http;

namespace KaansQoL.GespraechsFolgen
{
	internal static class Daten
	{
		private sealed class Kennzeichen
		{
			public bool AutoStart, AutoAbschluss, Kapitel, Versteckt;
			public List<string> StartNach = new List<string>();
			public Dictionary<string, Dictionary<string, int>> SetztVariablen = new Dictionary<string, Dictionary<string, int>>();
		}

		private static volatile bool _geladen;
		private static volatile bool _laedt;
		private static Dictionary<string, QuestInfo> _server;
		private static Dictionary<string, Kennzeichen> _kennzeichen;
		private static VariablenGruppen _gruppen = new VariablenGruppen();

		// Version der geladenen Daten -> Zwischenspeicher in Anzeige.cs
		internal static int Stand;

		private static Dictionary<string, QuestInfo> _gemischt;
		private static int _gemischtStand = -1;
		private static int _gemischtClientAnzahl = -1;

		public static bool Geladen => _geladen;
		public static bool Laedt => _laedt;
		public static VariablenGruppen Gruppen => _gruppen;

		// Zuletzt gebaute Aufgabenliste (fuer Namen-Ersatz)
		public static Dictionary<string, QuestInfo> Letzte => _gemischt;

		public static bool SitzungDa()
		{
			try
			{
				return Singleton<ClientApplication<IEftSession>>.Instance?.GetClientBackEndSession() != null;
			}
			catch
			{
				return false;
			}
		}

		public static void Laden()
		{
			if (_laedt)
				return;
			_laedt = true;
			Task.Run(() =>
			{
				try
				{
					var server = Abfrage("/gespraechsfolgen/quests", ServerLesen);
					var kennz = Abfrage("/visitapi/quest/flags", KennzeichenLesen);
					var gruppen = Abfrage("/visitapi/variable/groups", GruppenLesen);

					_server = server;
					_kennzeichen = kennz ?? new Dictionary<string, Kennzeichen>();
					if (gruppen != null)
						_gruppen = gruppen;
					Interlocked.Increment(ref Stand);
					_geladen = true;

					Plugin.Log.LogInfo($"Loaded: {server?.Count.ToString() ?? "no"} quest templates from the server part, "
					                   + $"{_kennzeichen.Count} VisitAPI quest flags, {_gruppen.Anzahl} variable groups.");
					if (server == null)
						Plugin.Log.LogWarning("Server part (user/mods/KaansQoL) not answering - using only the quests the client knows. "
						                      + "Unlocks of still locked quests can't be predicted.");
				}
				catch (Exception e)
				{
					Plugin.Log.LogWarning("Loading the quest data failed: " + e.Message);
				}
				finally
				{
					_laedt = false;
				}
			});
		}

		private static T Abfrage<T>(string pfad, Func<JToken, T> lesen) where T : class
		{
			for (var versuch = 1; versuch <= 3; versuch++)
			{
				try
				{
					var text = RequestHandler.PostJson(pfad, "{}");
					if (string.IsNullOrEmpty(text))
						return null;
					var daten = JObject.Parse(text)["data"];
					return daten == null || daten.Type == JTokenType.Null ? null : lesen(daten);
				}
				catch (Newtonsoft.Json.JsonException)
				{
					return null;   // Route unbekannt -> keine JSON-Antwort
				}
				catch (Exception e)
				{
					if (versuch == 3)
					{
						Plugin.Log.LogWarning(pfad + ": " + e.Message);
						return null;
					}
					Thread.Sleep(3000);
				}
			}
			return null;
		}

		// ---- /gespraechsfolgen/quests ----
		private static Dictionary<string, QuestInfo> ServerLesen(JToken daten)
		{
			if (!(daten is JObject o))
				return null;
			var d = new Dictionary<string, QuestInfo>(StringComparer.Ordinal);
			foreach (var p in o.Properties())
			{
				var q = new QuestInfo { Id = p.Name, Haendler = (string)p.Value["t"] };
				q.Start = Bedingungen(p.Value["s"]);
				q.Ziel = Bedingungen(p.Value["f"]);
				q.Fehlschlag = Bedingungen(p.Value["x"]);
				d[q.Id] = q;
			}
			return d;
		}

		private static List<Bedingung> Bedingungen(JToken t)
		{
			var l = new List<Bedingung>();
			if (!(t is JArray a))
				return l;
			foreach (var b in a.OfType<JObject>())
			{
				l.Add(new Bedingung
				{
					Id = (string)b["i"],
					Typ = (string)b["c"] ?? "",
					Notwendig = (bool?)b["n"] ?? true,
					Ziele = (b["z"] as JArray)?.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList() ?? new List<string>(),
					Wert = (double?)b["v"] ?? 0,
					Vergleich = (string)b["m"] ?? ">=",
					Status = (b["st"] as JArray)?.Select(x => (int)x).ToList() ?? new List<int>(),
				});
			}
			return l;
		}

		// ---- /visitapi/quest/flags ----
		private static Dictionary<string, Kennzeichen> KennzeichenLesen(JToken daten)
		{
			if (!(daten is JObject o))
				return null;
			var d = new Dictionary<string, Kennzeichen>(StringComparer.Ordinal);
			foreach (var p in o.Properties())
			{
				try
				{
					var v = p.Value;
					var k = new Kennzeichen
					{
						AutoStart = Wahr(v["autoStart"]),
						AutoAbschluss = Wahr(v["autoFinish"]),
						Kapitel = Wahr(v["chapter"]),
						Versteckt = Wahr(v["hidden"]),
					};
					var sa = v["startAfter"];
					if (sa is JArray sl)
						k.StartNach = sl.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList();
					else if (sa != null && sa.Type == JTokenType.String)
						k.StartNach = new List<string> { (string)sa };
					if (v["setVariables"] is JObject sv)
						foreach (var st in sv.Properties())
							if (st.Value is JObject vars)
								k.SetztVariablen[st.Name] = vars.Properties()
									.Where(x => x.Value.Type == JTokenType.Integer || x.Value.Type == JTokenType.Float)
									.ToDictionary(x => x.Name, x => (int)x.Value);
					d[p.Name] = k;
				}
				catch
				{
					// einzelne kaputte Eintraege ueberspringen
				}
			}
			return d;
		}

		private static bool Wahr(JToken t) => t != null && t.Type == JTokenType.Boolean && (bool)t;

		// ---- /visitapi/variable/groups ----
		private static VariablenGruppen GruppenLesen(JToken daten)
		{
			if (!(daten is JArray a))
				return null;
			var g = new VariablenGruppen();
			foreach (var e in a.OfType<JObject>())
				g.Hinzufuegen((string)e["id"], (e["variables"] as JArray)?.Select(x => (string)x).Where(x => !string.IsNullOrEmpty(x)).ToList());
			return g;
		}

		// ---- Zusammenfuehren: Server-Vorlagen + Client-Aufgaben + Kennzeichen ----
		public static Dictionary<string, QuestInfo> Quests(QuestController qc)
		{
			var client = new List<Quest>();
			try
			{
				if (qc?.Quests != null)
					client.AddRange(qc.Quests);
			}
			catch
			{
				// Aufgabenbuch gerade im Umbau - dann eben ohne
			}

			if (_gemischt != null && _gemischtStand == Stand && _gemischtClientAnzahl == client.Count)
				return _gemischt;

			var d = new Dictionary<string, QuestInfo>(StringComparer.Ordinal);
			var server = _server;
			if (server != null)
				foreach (var kv in server)
					d[kv.Key] = kv.Value;
			foreach (var q in client)
			{
				try
				{
					if (q?.Template == null || d.ContainsKey(q.Id))
						continue;
					d[q.Id] = AusClient(q);
				}
				catch
				{
					// einzelne Aufgabe ueberspringen
				}
			}

			var kennz = _kennzeichen;
			if (kennz != null)
				foreach (var q in d.Values)
					if (kennz.TryGetValue(q.Id, out var k))
					{
						q.AutoStart = k.AutoStart;
						q.AutoAbschluss = k.AutoAbschluss;
						q.Kapitel = k.Kapitel;
						q.Versteckt = k.Versteckt;
						q.StartNach = k.StartNach;
						q.SetztVariablen = k.SetztVariablen;
					}

			_gemischt = d;
			_gemischtStand = Stand;
			_gemischtClientAnzahl = client.Count;
			return d;
		}

		// Ersatz ohne Server-Teil: Bedingungen aus der Client-Vorlage
		private static QuestInfo AusClient(Quest q)
		{
			var qi = new QuestInfo { Id = q.Id, Haendler = q.Template.TraderId };
			var c = q.Template.Conditions;
			if (c != null)
			{
				qi.Start = Wandeln(c, EQuestStatus.AvailableForStart);
				qi.Ziel = Wandeln(c, EQuestStatus.AvailableForFinish);
				qi.Fehlschlag = Wandeln(c, EQuestStatus.Fail);
			}
			return qi;
		}

		private static List<Bedingung> Wandeln(ConditionsDict c, EQuestStatus status)
		{
			var l = new List<Bedingung>();
			if (!c.TryGetValue(status, out var liste) || liste == null)
				return l;
			foreach (var b in liste)
			{
				if (b == null)
					continue;
				var neu = new Bedingung
				{
					Id = b.id.ToString(),
					Notwendig = b.IsNecessary,
					Wert = b.value,
					Vergleich = Vergleich(b.compareMethod),
				};
				switch (b)
				{
					case ConditionGlobalVariableValue g:
						neu.Typ = "GlobalVariableValue";
						neu.Ziele.Add(g.target);
						break;
					case ConditionQuest cq:
						neu.Typ = "Quest";
						neu.Ziele.Add(cq.target);
						if (cq.statuses != null)
							neu.Status = cq.statuses.Select(s => (int)s).ToList();
						break;
					case ConditionLevel _:
						neu.Typ = "Level";
						break;
					case ConditionTraderLoyalty tl:
						neu.Typ = "TraderLoyalty";
						neu.Ziele.Add(tl.target);
						break;
					case ConditionTraderStanding ts:
						neu.Typ = "TraderStanding";
						neu.Ziele.Add(ts.target);
						break;
					default:
						neu.Typ = b.GetType().Name;
						break;
				}
				l.Add(neu);
			}
			return l;
		}

		private static string Vergleich(ECompareMethod m)
		{
			switch (m)
			{
				case ECompareMethod.Equal: return "==";
				case ECompareMethod.NotEqual: return "!=";
				case ECompareMethod.More: return ">";
				case ECompareMethod.Less: return "<";
				case ECompareMethod.LessOrEqual: return "<=";
				default: return ">=";
			}
		}
	}
}
