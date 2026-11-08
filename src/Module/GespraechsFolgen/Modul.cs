// ---------------------------------------------------------------
//  Gespraechsfolgen (Dialogue Consequences) - SPT 4.1.6
//
//  Zeigt in VisitAPI-Gespraechen unter jeder Antwort, was sie mit den
//  Aufgaben macht - BEVOR man sie waehlt:
//      rot   = Aufgabe scheitert
//      gruen = Aufgabe wird erledigt / startet / wird freigeschaltet
//      grau  = keine Aufgabenfolgen (nur, wenn eine andere Antwort
//              derselben Auswahl Folgen hat)
//
//  Anlass: In "Blue Fire" fragt Mechanic nach dem Fragment von Objekt
//  1156. "Nein, das Fragment bleibt bei mir" setzt eine Profilvariable,
//  und an genau der haengt die Fail-Bedingung der Aufgabe "Uebergib das
//  Fragment an Mechanic". Davon stand nirgends etwas.
//
//  Wie es arbeitet:
//    1. Harmony-Postfix auf TraderDialogWindowOptionRow.Show - so baut
//       das Spiel jede Antwortzeile auf (Anzeige.cs).
//    2. Das Gespraech wird ab dieser Antwort im Kopf weitergespielt,
//       mit den echten Dialogvorlagen aus DialogStorage und einem
//       Schatten-Variablenstand (Kern/DialogSimulator.cs).
//    3. Die dabei gesetzten Profilvariablen und Aufgaben-Aktionen werden
//       gegen die Bedingungen aller Aufgaben gerechnet, ein bis zwei
//       Ebenen tief (Kern/FolgenRechner.cs).
//  Es wird nichts veraendert und nichts an den Server geschickt ausser
//  drei lesenden Abfragen beim Start (Daten.cs).
//
//  Weiche Abhaengigkeit: ohne VisitAPI tut das Modul nichts.
// ---------------------------------------------------------------
using System;
using BepInEx.Configuration;
using HarmonyLib;
using KaansQoL.GespraechsFolgen.Kern;
using UnityEngine;

namespace KaansQoL.GespraechsFolgen
{
	public enum Anzeigeart { Inline, Tooltip }

	internal sealed class GespraechsFolgenModule : QolModule
	{
		public const string VisitApiGuid = "com.sora.visitapi";

		public override string Id => "GespraechsFolgen";

		public override string Section => "15 Dialogue Consequences";

		public override string Beschreibung => "Show what each dialogue answer does to your quests, before you pick it.";

		public override string AlteGuid => "com.kaan.gespraechsfolgen";

		public override string AlterName => "Gespraechsfolgen";

		public override string[] BenoetigtPlugins => new[] { VisitApiGuid };

		public override MigRow[] Umzug => new[]
		{
			new MigRow("1. General", "Enabled", "Enabled"),
			new MigRow("1. General", "Display mode", "Display mode"),
			new MigRow("1. General", "Detail level", "Detail level"),
			new MigRow("2. Filter", "Show only fails", "Show only fails"),
			new MigRow("2. Filter", "Show unlocks", "Show unlocks")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			string s = Section;
			Modul.Eingeschaltet = Enabled;
			Modul.Art = Cfg.Bind(cfg, s, "Display mode", Anzeigeart.Inline,
				"Inline: small coloured lines under the answer. Tooltip: only a marker [!] / [+] after the answer, details on mouse-over.");
			Modul.Detail = Cfg.Bind(cfg, s, "Detail level", Detailstufe.Chain,
				"Direct: only quests the answer itself completes/fails/starts. Chain: also one step further (a quest that fails because another one completes). ChainAndObjectives: additionally single objectives and hidden helper quests.");
			Modul.NurFehlschlaege = Cfg.Bind(cfg, s, "Show only fails", false,
				"Only warn about quests that would fail.");
			Modul.Freischaltungen = Cfg.Bind(cfg, s, "Show unlocks", true,
				"Also show quests that an answer starts or unlocks.");

			cfg.SettingChanged += (_, __) => Modul.EinstellungsStand++;
		}

		public override void Apply(Harmony h)
		{
			h.PatchAll(typeof(Anzeige.ZeilenPatch));
		}

		// Daten erst holen, wenn eine Sitzung zum Server besteht (nach dem Login).
		public override void Tick()
		{
			if (Daten.Geladen || Daten.Laedt || Time.realtimeSinceStartup < Modul.NaechsterVersuch)
				return;
			Modul.NaechsterVersuch = Time.realtimeSinceStartup + 3f;
			if (Daten.SitzungDa())
				Daten.Laden();
		}
	}

	internal static class Modul
	{
		internal static ConfigEntry<bool> Eingeschaltet;
		internal static ConfigEntry<Anzeigeart> Art;
		internal static ConfigEntry<Detailstufe> Detail;
		internal static ConfigEntry<bool> NurFehlschlaege;
		internal static ConfigEntry<bool> Freischaltungen;

		// Wird bei jeder Einstellungsaenderung hochgezaehlt -> Zwischenspeicher neu
		internal static int EinstellungsStand;

		internal static float NaechsterVersuch;

		internal static Anzeigeoptionen Optionen()
		{
			return new Anzeigeoptionen
			{
				NurFehlschlaege = NurFehlschlaege.Value,
				Freischaltungen = Freischaltungen.Value,
				Detail = Detail.Value,
				Deutsch = Namen.Deutsch(),
			};
		}
	}
}
