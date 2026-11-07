// ---------------------------------------------------------------
//  Gemeinsame Vorlage fuer alle Module von Kaan's QoL.
//
//  Jedes Modul war frueher ein eigenes Plugin. Jetzt bringt es nur
//  noch mit: seinen Namen, seine F12-Sektion, seine Einstellungen,
//  die Liste seiner Harmony-Patches - und ggf. Update()/OnGUI().
//  Plugin.cs entscheidet, ob ein Modul laeuft:
//    - nur wenn "Enabled" an ist,
//    - nicht, wenn das alte Einzel-Plugin noch installiert ist,
//    - nicht, wenn ein benoetigtes Fremd-Plugin fehlt.
//  Jedes Modul hat seine EIGENE Harmony-Instanz; Ausschalten heisst
//  UnpatchSelf() - das geht auch mitten im Spiel.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace KaansQoL
{
	/// <summary>Eine Zeile der Umzugstabelle: alter Platz in der alten cfg -> neuer Eintrag.</summary>
	internal struct MigRow
	{
		public string AltSektion, AltKey, NeuKey;

		public MigRow(string altSektion, string altKey, string neuKey)
		{
			AltSektion = altSektion;
			AltKey = altKey;
			NeuKey = neuKey;
		}
	}

	internal abstract class QolModule
	{
		/// <summary>Kurzname = Ordnername unter src/Module.</summary>
		public abstract string Id { get; }

		/// <summary>F12-Sektion, z.B. "01 Auto Run".</summary>
		public abstract string Section { get; }

		/// <summary>Text des Schalters "Enabled".</summary>
		public abstract string Beschreibung { get; }

		/// <summary>GUID des frueheren Einzel-Plugins (null = neues Modul).</summary>
		public virtual string AlteGuid => null;

		/// <summary>Anzeigename des frueheren Plugins fuer das Protokoll.</summary>
		public virtual string AlterName => Id;

		/// <summary>Fremd-Plugins (GUIDs), die geladen sein muessen, damit das Modul laeuft.</summary>
		public virtual string[] BenoetigtPlugins => new string[0];

		/// <summary>Zeilen fuer die Uebernahme aus der alten cfg.</summary>
		public virtual MigRow[] Umzug => new MigRow[0];

		/// <summary>
		/// true = die Patches bleiben auch bei "Enabled = aus" eingehaengt (das Modul fragt
		/// den Schalter selbst ab). Noetig, wenn das Ausschalten erst durch den Patch
		/// selbst sauber zurueckgenommen wird (KeinSchmerzmittelFilter).
		/// </summary>
		public virtual bool PatchBleibtBeiAus => false;

		public ConfigEntry<bool> Enabled { get; private set; }

		internal Harmony Harmony;

		internal bool Aktiv;

		/// <summary>Grund, warum das Modul nicht laufen darf (null = frei).</summary>
		internal string Gesperrt;

		/// <summary>Patch ist fehlgeschlagen - nicht jedes Bild neu versuchen.</summary>
		internal bool Fehlgeschlagen;

		public void BindAll(ConfigFile cfg)
		{
			Enabled = Cfg.Bind(cfg, Section, "Enabled", true, Beschreibung);
			BindSettings(cfg);
		}

		protected abstract void BindSettings(ConfigFile cfg);

		/// <summary>Patches einhaengen (auf der modul-eigenen Harmony-Instanz).</summary>
		public abstract void Apply(Harmony h);

		/// <summary>Nach dem Aushaengen der Patches: Zustand aufraeumen.</summary>
		public virtual void Entfernt()
		{
		}

		/// <summary>Jedes Bild, solange das Modul aktiv ist.</summary>
		public virtual void Tick()
		{
		}

		/// <summary>OnGUI, solange das Modul aktiv ist.</summary>
		public virtual void Gui()
		{
		}
	}
}
