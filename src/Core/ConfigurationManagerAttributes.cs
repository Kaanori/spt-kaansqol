// ---------------------------------------------------------------
//  Erkennungszeichen fuer den ConfigurationManager (das F12-Menue).
//
//  Der liest diese Klasse per Reflexion aus und ordnet die Felder
//  ueber ihren NAMEN zu - die Klasse muss deshalb genau so heissen
//  und die Felder auch. Ein fester Verweis auf den
//  ConfigurationManager waere das Gegenteil von gut: fehlt er,
//  wuerde das Plugin gar nicht erst laden. So bleibt er optional.
//  (Frueher hatten AutoLaufen und Zielhilfe je eine eigene Fassung;
//  hier sind es alle Felder in einer Klasse.)
// ---------------------------------------------------------------
using System;
using BepInEx.Configuration;

namespace KaansQoL
{
	internal sealed class ConfigurationManagerAttributes
	{
		// Eigener Zeichner (z.B. Tastenlisten mit "+ Taste hinzufuegen").
		public Action<ConfigEntryBase> CustomDrawer;

		// Hoeher = weiter oben in der Sektion (der Manager sortiert absteigend).
		public int? Order;

		public bool? IsAdvanced;
	}
}
