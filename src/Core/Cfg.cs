// ---------------------------------------------------------------
//  Kleine Hilfe zum Anlegen der Einstellungen: vergibt die Reihenfolge
//  im F12-Menue automatisch nach der Reihenfolge der Aufrufe (der
//  ConfigurationManager sortiert sonst alphabetisch). "Enabled" wird
//  als Erstes gebunden und steht damit oben in jeder Sektion.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace KaansQoL
{
	internal static class Cfg
	{
		private static readonly Dictionary<string, int> _platz = new Dictionary<string, int>();

		internal static ConfigEntry<T> Bind<T>(ConfigFile cfg, string section, string key, T standard,
			string beschreibung, AcceptableValueBase bereich = null,
			Action<ConfigEntryBase> zeichner = null, bool erweitert = false)
		{
			int platz;
			if (!_platz.TryGetValue(section, out platz))
			{
				platz = 1000;
			}
			_platz[section] = platz - 1;
			ConfigurationManagerAttributes attr = new ConfigurationManagerAttributes
			{
				Order = platz,
				CustomDrawer = zeichner,
				IsAdvanced = erweitert ? (bool?)true : null
			};
			return cfg.Bind(section, key, standard, new ConfigDescription(beschreibung ?? "", bereich, attr));
		}
	}
}
