// ---------------------------------------------------------------
//  Einstellungen des Server-Teils (config.json neben der DLL) und
//  einmalige Uebernahme aus den alten Server-Mods.
//
//  Die alten Mods hatten je eine kleine JSON-Datei:
//    user/mods/BalsamDauer/balsam.json       { minuten, effekte[] }
//    user/mods/AlleAusgaenge/ausgaenge.json  { chance, einstiegspunkte, zeitfenster }
//  Gibt es beim Start noch KEINE config.json, wird sie angelegt und
//  mit den Werten aus diesen Dateien gefuellt (soweit sie existieren).
//  Sind die alten Mod-Ordner schon weggeraeumt, reicht es, balsam.json bzw.
//  ausgaenge.json vorher in den Ordner dieses Mods zu kopieren.
//  Die alten Dateien werden nur gelesen, nie veraendert oder geloescht.
//
//  Diese Datei haengt nicht am SPT-Server - der Offline-Test benutzt sie direkt.
// ---------------------------------------------------------------
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KaansQoL.Server;

public sealed class BalsamEinstellungen
{
	public bool Enabled { get; set; } = true;
	public double Minutes { get; set; } = 15;
	public List<string> Effects { get; set; } = new() { "Pain", "RadExposure" };
}

public sealed class AusgangsEinstellungen
{
	public bool Enabled { get; set; } = true;
	public bool Chance { get; set; } = true;
	public bool EntryPoints { get; set; } = true;
	public bool TimeWindows { get; set; } = true;
}

public sealed class QolEinstellungen
{
	public BalsamEinstellungen BalsamDuration { get; set; } = new();
	public AusgangsEinstellungen AllExtracts { get; set; } = new();
}

public static class Einstellungen
{
	public const string Dateiname = "config.json";

	private static readonly JsonSerializerOptions Optionen = new()
	{
		WriteIndented = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
	};

	/// <summary>
	/// Liest config.json aus dem Modordner. Gibt es sie nicht, wird sie angelegt - mit den Werten
	/// der alten Mods, falls deren Dateien neben dem Modordner liegen (user/mods/BalsamDauer usw.).
	/// </summary>
	public static QolEinstellungen Laden(string modOrdner, Action<string> info, Action<string> warnung)
	{
		string datei = Path.Combine(modOrdner, Dateiname);
		if (File.Exists(datei))
		{
			try
			{
				string text = File.ReadAllText(datei);
				return JsonSerializer.Deserialize<QolEinstellungen>(text, Optionen) ?? new QolEinstellungen();
			}
			catch (Exception ex)
			{
				warnung("config.json could not be read (" + ex.Message + ") - using defaults.");
				return new QolEinstellungen();
			}
		}

		QolEinstellungen neu = new();
		string modsOrdner = Path.GetDirectoryName(modOrdner.TrimEnd(Path.DirectorySeparatorChar, '/')) ?? ".";

		// Erst der alte Mod-Ordner, dann - falls der schon weggeraeumt wurde - eine hierher kopierte Datei.
		int n = 0;
		foreach (string pfad in new[] { Path.Combine(modsOrdner, "BalsamDauer", "balsam.json"), Path.Combine(modOrdner, "balsam.json") })
		{
			n = UebernehmeBalsam(pfad, neu.BalsamDuration, warnung);
			if (n > 0) break;
		}
		if (n > 0) info("Migrated " + n + " settings from BalsamDauer (balsam.json)");

		int a = 0;
		foreach (string pfad in new[] { Path.Combine(modsOrdner, "AlleAusgaenge", "ausgaenge.json"), Path.Combine(modOrdner, "ausgaenge.json") })
		{
			a = UebernehmeAusgaenge(pfad, neu.AllExtracts, warnung);
			if (a > 0) break;
		}
		if (a > 0) info("Migrated " + a + " settings from AlleAusgaenge (ausgaenge.json)");

		Speichern(modOrdner, neu, warnung);
		return neu;
	}

	public static void Speichern(string modOrdner, QolEinstellungen e, Action<string> warnung)
	{
		try
		{
			string datei = Path.Combine(modOrdner, Dateiname);
			JsonNode? knoten = JsonSerializer.SerializeToNode(e, Optionen);
			if (knoten is JsonObject o)
			{
				var mitHinweis = new JsonObject
				{
					["_hint"] = "Kaan's QoL (server part). Takes effect on the next server start, no rebuild needed. "
						+ "balsamDuration: minutes = how long Golden Star Balm effects last, effects = which damage effects get that duration. "
						+ "allExtracts: makes every extract always reachable (chance 100 %, all entry points, time windows open; trains stay excluded)."
				};
				foreach (var kv in o.ToList())
				{
					o.Remove(kv.Key);
					mitHinweis[kv.Key] = kv.Value;
				}
				knoten = mitHinweis;
			}
			File.WriteAllText(datei, knoten!.ToJsonString(Optionen) + "\n");
		}
		catch (Exception ex)
		{
			warnung("config.json could not be written (" + ex.Message + ").");
		}
	}

	private static JsonObject? LiesObjekt(string pfad, Action<string> warnung)
	{
		if (!File.Exists(pfad)) return null;
		try
		{
			return JsonNode.Parse(File.ReadAllText(pfad), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject;
		}
		catch (Exception ex)
		{
			warnung("Could not read " + pfad + " for migration: " + ex.Message);
			return null;
		}
	}

	/// <summary>balsam.json -> BalsamDuration. Liefert die Zahl der uebernommenen Einstellungen.</summary>
	public static int UebernehmeBalsam(string pfad, BalsamEinstellungen ziel, Action<string> warnung)
	{
		JsonObject? o = LiesObjekt(pfad, warnung);
		if (o == null) return 0;
		int n = 0;
		if (o["minuten"] is JsonValue m && m.TryGetValue<double>(out double min))
		{
			ziel.Minutes = min;
			n++;
		}
		if (o["effekte"] is JsonArray arr)
		{
			var liste = arr.Select(x => x?.GetValue<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToList();
			if (liste.Count > 0)
			{
				ziel.Effects = liste;
				n++;
			}
		}
		return n;
	}

	/// <summary>ausgaenge.json -> AllExtracts. Liefert die Zahl der uebernommenen Einstellungen.</summary>
	public static int UebernehmeAusgaenge(string pfad, AusgangsEinstellungen ziel, Action<string> warnung)
	{
		JsonObject? o = LiesObjekt(pfad, warnung);
		if (o == null) return 0;
		int n = 0;
		bool Lies(string name, Action<bool> setze)
		{
			if (o[name] is JsonValue v && v.TryGetValue<bool>(out bool b))
			{
				setze(b);
				return true;
			}
			return false;
		}
		if (Lies("chance", b => ziel.Chance = b)) n++;
		if (Lies("einstiegspunkte", b => ziel.EntryPoints = b)) n++;
		if (Lies("zeitfenster", b => ziel.TimeWindows = b)) n++;
		return n;
	}
}
