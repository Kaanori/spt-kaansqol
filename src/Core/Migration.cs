// ---------------------------------------------------------------
//  Einmalige Uebernahme der Einstellungen aus den alten Einzel-Mods.
//
//  So geht es: Vor den Binds merkt sich Vorhandene() alle Eintraege, die
//  die cfg von Kaan's QoL schon enthaelt. Nach den Binds geht Run() fuer
//  ein Modul die Umzugstabelle durch (alte Sektion/Schluessel -> neuer
//  Eintrag); fuer jeden neuen Eintrag, den es NICHT schon in der eigenen
//  cfg gab, wird der Wert aus BepInEx/config/<alte GUID>.cfg gelesen und
//  hineingeschrieben.
//
//  Die alten cfg-Dateien werden nur GELESEN (eigener kleiner Parser,
//  kein ConfigFile - so kann nichts versehentlich zurueckgeschrieben
//  werden) und NIE geloescht oder veraendert.
//
//  Diese Datei haengt nur an BepInEx - sie wird auch vom Offline-Test
//  verwendet.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;

namespace KaansQoL
{
	internal static class Migration
	{
		// Aufzaehlungen, die umbenannt wurden: Typname -> alter Wert -> neuer Wert.
		private static readonly Dictionary<string, Dictionary<string, string>> Werte =
			new Dictionary<string, Dictionary<string, string>>
			{
				["StartMode"] = new Dictionary<string, string>
				{
					["Umschalten"] = "Toggle", ["Halten"] = "Hold"
				}
			};

		/// <summary>
		/// BepInEx haelt Eintraege der cfg, die (noch) zu keinem Bind gehoeren, in der
		/// privaten Eigenschaft ConfigFile.OrphanedEntries.
		/// </summary>
		private static Dictionary<ConfigDefinition, string> Waisen(ConfigFile c)
		{
			PropertyInfo pi = typeof(ConfigFile).GetProperty("OrphanedEntries",
				BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			return pi?.GetValue(c, null) as Dictionary<ConfigDefinition, string>;
		}

		/// <summary>VOR den Binds aufrufen: alles, was die cfg schon enthaelt.</summary>
		public static HashSet<ConfigDefinition> Vorhandene(ConfigFile c)
		{
			Dictionary<ConfigDefinition, string> w = Waisen(c);
			return w == null ? new HashSet<ConfigDefinition>() : new HashSet<ConfigDefinition>(w.Keys);
		}

		/// <summary>Liest eine BepInEx-cfg als Tabelle (Sektion, Schluessel) -> Rohtext des Werts.</summary>
		internal static Dictionary<ConfigDefinition, string> LiesDatei(string pfad)
		{
			Dictionary<ConfigDefinition, string> ergebnis = new Dictionary<ConfigDefinition, string>();
			string sektion = "";
			foreach (string roh in File.ReadAllLines(pfad))
			{
				string zeile = roh.Trim();
				if (zeile.Length == 0 || zeile.StartsWith("#"))
				{
					continue;
				}
				if (zeile.StartsWith("[") && zeile.EndsWith("]"))
				{
					sektion = zeile.Substring(1, zeile.Length - 2);
					continue;
				}
				int gleich = zeile.IndexOf('=');
				if (gleich <= 0)
				{
					continue;
				}
				string key = zeile.Substring(0, gleich).Trim();
				string wert = zeile.Substring(gleich + 1).Trim();
				ergebnis[new ConfigDefinition(sektion, key)] = wert;
			}
			return ergebnis;
		}

		private static string Umsetzen(Type typ, string alt)
		{
			string w = alt.Trim();
			if (Werte.TryGetValue(typ.Name, out var karte) && karte.TryGetValue(w, out var neu))
			{
				return neu;
			}
			return alt;
		}

		/// <summary>
		/// NACH den Binds aufrufen. Liefert, wie viele Einstellungen des Moduls aus der
		/// alten cfg uebernommen wurden. Ueberschreibt nie einen Wert, den es in der
		/// eigenen cfg schon gab.
		/// </summary>
		public static int Run(ConfigFile c, QolModule m, HashSet<ConfigDefinition> vorher,
			string configOrdner, Action<string> warnung)
		{
			if (m.AlteGuid == null || m.Umzug.Length == 0)
			{
				return 0;
			}
			string pfad = Path.Combine(configOrdner, m.AlteGuid + ".cfg");
			if (!File.Exists(pfad))
			{
				return 0;
			}
			Dictionary<ConfigDefinition, string> alt;
			try
			{
				alt = LiesDatei(pfad);
			}
			catch (Exception ex)
			{
				warnung?.Invoke("Could not read " + pfad + " for migration: " + ex.Message);
				return 0;
			}

			int anzahl = 0;
			foreach (MigRow z in m.Umzug)
			{
				ConfigDefinition neu = new ConfigDefinition(m.Section, z.NeuKey);
				if (vorher.Contains(neu) || !c.ContainsKey(neu))
				{
					continue;
				}
				if (!alt.TryGetValue(new ConfigDefinition(z.AltSektion, z.AltKey), out var wert))
				{
					continue;
				}
				ConfigEntryBase e = c[neu];
				try
				{
					e.SetSerializedValue(Umsetzen(e.SettingType, wert));
					anzahl++;
				}
				catch
				{
					// Wert nicht lesbar: neuer Eintrag behaelt seinen Standard.
					warnung?.Invoke("Setting '" + z.AltKey + "' of " + m.AlterName
						+ " could not be read (value '" + wert + "') - default kept.");
				}
			}
			return anzahl;
		}
	}
}
