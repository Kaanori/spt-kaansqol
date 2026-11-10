// ---------------------------------------------------------------
//  Kaan's QoL  -  eine Mod statt vieler kleiner.
//
//  Fasst die frueheren Einzel-Mods AutoLaufen, Blickrichtung,
//  Sortierknopf, AllesAnnehmen, BehaelterOhneGewicht, Windstille,
//  KeinSchmerzmittelFilter, KartentuerHacken, TuerAbschliessen,
//  HeliOhneFackel, GuertelFix, PfeilSpitze, ZonenKills und
//  GespraechsFolgen zusammen und bringt das neue Modul
//  "Magazine Swap" mit. Seit 1.1.0 gehoeren die Aufgaben-Module
//  (ZonenKills, GespraechsFolgen) dazu; der Server-Teil bringt
//  zusaetzlich "Kein Aufgaben-Fehlschlag" und den Datenpunkt fuer
//  die Gespraechsfolgen mit (siehe Server/). Seit 1.2.0 neu:
//  "Smooth Sun" (SonneGlatt) - Sonnenlicht gleitet statt zu springen.
//
//  So funktioniert es:
//   - Jedes Modul steht in src/Module/<Name>/ und hat in F12 eine
//     eigene Sektion mit dem Schalter "Enabled" ganz oben.
//   - Dieses Plugin legt alle Einstellungen an, uebernimmt beim
//     ersten Start die Werte aus den alten cfg-Dateien (Migration.cs)
//     und haengt die Harmony-Patches eines Moduls nur ein, wenn es
//     eingeschaltet ist. Jedes Modul hat seine eigene Harmony-
//     Instanz - Ein-/Ausschalten geht auch mitten im Spiel.
//   - Ist das alte Einzel-Plugin eines Moduls noch installiert, laeuft
//     dieses Modul NICHT (sonst wuerde alles doppelt gepatcht).
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace KaansQoL
{
	[BepInPlugin(Guid, "Kaan's QoL", Version)]
	[BepInDependency("com.SPT.core")]
	// Die alten Einzel-Mods: nur damit sie VOR uns geladen sind und wir sie
	// in Chainloader.PluginInfos finden. Pflicht ist keiner davon.
	[BepInDependency("com.kaan.autolaufen", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("kaan.blickrichtung", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.sortierknopf", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.allesannehmen", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.behaelterohnegewicht", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.windstille", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.keinschmerzmittelfilter", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.kartentuerhacken", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.tuerabschliessen", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("kaan.heliohnefackel", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.guertelfix", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.pfeilspitze", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.zonenkills", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.kaan.gespraechsfolgen", BepInDependency.DependencyFlags.SoftDependency)]
	// Fremd-Mods, die einzelne Module brauchen bzw. vor denen sie laden sollen.
	[BepInDependency("com.manimal.icebreaker", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.trenchfoot.beltslot", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency("com.mpstark.dynamicmaps", BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(GespraechsFolgen.GespraechsFolgenModule.VisitApiGuid, BepInDependency.DependencyFlags.SoftDependency)]
	[BepInDependency(TuerAbschliessen.OrbitSperre.OrbitGuid, BepInDependency.DependencyFlags.SoftDependency)]
	public class Plugin : BaseUnityPlugin
	{
		internal const string Guid = "com.kaan.qol";
		internal const string Version = "1.2.0";

		internal static ManualLogSource Log;

		internal static Plugin Instance;

		internal static List<QolModule> Module;

		private readonly HashSet<QolModule> _tickFehler = new HashSet<QolModule>();

		private void Awake()
		{
			Log = Logger;
			Instance = this;

			Module = Einrichten(Config, BepInEx.Paths.ConfigPath, s => Log.LogInfo(s), s => Log.LogWarning(s));

			foreach (QolModule m in Module)
			{
				Pruefen(m);
			}
			foreach (QolModule m in Module)
			{
				Abgleichen(m);
			}

			int an = 0;
			foreach (QolModule m in Module)
			{
				if (m.Aktiv)
				{
					an++;
				}
			}
			Log.LogInfo("Kaan's QoL " + Version + ": " + an + " of " + Module.Count + " modules active.");
		}

		/// <summary>
		/// Alle Module anlegen, Einstellungen binden und die alten cfg-Dateien
		/// uebernehmen. Ohne Unity-Instanz aufrufbar - der Offline-Test nutzt das.
		/// </summary>
		internal static List<QolModule> Einrichten(ConfigFile cfg, string configOrdner,
			Action<string> info, Action<string> warnung)
		{
			List<QolModule> liste = new List<QolModule>
			{
				new AutoLaufen.AutoLaufenModule(),
				new Blickrichtung.BlickrichtungModule(),
				new Sortierknopf.SortierknopfModule(),
				new AllesAnnehmen.AllesAnnehmenModule(),
				new BehaelterOhneGewicht.BehaelterOhneGewichtModule(),
				new Windstille.WindstilleModule(),
				new KeinSchmerzmittelFilter.KeinSchmerzmittelFilterModule(),
				new KartentuerHacken.KartentuerHackenModule(),
				new TuerAbschliessen.TuerAbschliessenModule(),
				new HeliOhneFackel.HeliOhneFackelModule(),
				new GuertelFix.GuertelFixModule(),
				new PfeilSpitze.PfeilSpitzeModule(),
				new MagazineSwap.MagazineSwapModule(),
				new ZonenKills.ZonenKillsModule(),
				new GespraechsFolgen.GespraechsFolgenModule(),
				new SonneGlatt.SonneGlattModule()
			};

			HashSet<ConfigDefinition> vorher = Migration.Vorhandene(cfg);
			foreach (QolModule m in liste)
			{
				m.BindAll(cfg);
			}

			// Die Uebernahme schreibt viele Werte - erst am Ende speichern.
			bool alt = cfg.SaveOnConfigSet;
			cfg.SaveOnConfigSet = false;
			int gesamt = 0;
			try
			{
				foreach (QolModule m in liste)
				{
					int n = Migration.Run(cfg, m, vorher, configOrdner, warnung);
					if (n > 0)
					{
						info("Migrated " + n + " settings from " + m.AlterName);
					}
					gesamt += n;
				}
			}
			finally
			{
				cfg.SaveOnConfigSet = alt;
			}
			if (gesamt > 0)
			{
				cfg.Save();
			}
			return liste;
		}

		/// <summary>Sperrt das Modul, wenn das alte Einzel-Plugin noch da ist oder ein Fremd-Plugin fehlt.</summary>
		private static void Pruefen(QolModule m)
		{
			if (m.AlteGuid != null && Chainloader.PluginInfos.ContainsKey(m.AlteGuid))
			{
				m.Gesperrt = "old stand-alone mod " + m.AlterName + " (" + m.AlteGuid + ") is still installed";
				if (m.Enabled.Value)
				{
					Log.LogWarning("Module '" + m.Section + "' stays OFF: the old stand-alone mod " + m.AlterName
						+ " (" + m.AlteGuid + ") is still installed - running both would patch everything twice. "
						+ "Move " + m.AlterName + " out of BepInEx/plugins to use the module.");
				}
				return;
			}
			foreach (string guid in m.BenoetigtPlugins)
			{
				if (!Chainloader.PluginInfos.ContainsKey(guid))
				{
					m.Gesperrt = "required plugin " + guid + " is not installed";
					if (m.Enabled.Value)
					{
						Log.LogInfo("Module '" + m.Section + "' stays off: required plugin " + guid + " is not installed.");
					}
					return;
				}
			}
		}

		private static void Abgleichen(QolModule m)
		{
			bool soll = (m.Enabled.Value || m.PatchBleibtBeiAus) && m.Gesperrt == null && !m.Fehlgeschlagen;
			if (soll && !m.Aktiv)
			{
				Einschalten(m);
			}
			else if (!soll && m.Aktiv)
			{
				Ausschalten(m);
			}
		}

		private static void Einschalten(QolModule m)
		{
			Harmony h = new Harmony(Guid + "." + m.Id);
			try
			{
				m.Apply(h);
				m.Harmony = h;
				m.Aktiv = true;
				Log.LogInfo("Module '" + m.Section + "' on.");
			}
			catch (Exception ex)
			{
				m.Fehlgeschlagen = true;
				try
				{
					h.UnpatchSelf();
				}
				catch
				{
				}
				Log.LogError("Module '" + m.Section + "': patching failed, module stays off - " + ex);
			}
		}

		private static void Ausschalten(QolModule m)
		{
			try
			{
				m.Harmony?.UnpatchSelf();
			}
			catch (Exception ex)
			{
				Log.LogError("Module '" + m.Section + "': unpatching failed - " + ex);
			}
			m.Harmony = null;
			m.Aktiv = false;
			try
			{
				m.Entfernt();
			}
			catch (Exception ex)
			{
				Log.LogError("Module '" + m.Section + "': cleanup failed - " + ex);
			}
			Log.LogInfo("Module '" + m.Section + "' off.");
		}

		private void Update()
		{
			if (Module == null)
			{
				return;
			}
			foreach (QolModule m in Module)
			{
				Abgleichen(m);
				if (!m.Aktiv)
				{
					continue;
				}
				try
				{
					m.Tick();
				}
				catch (Exception ex)
				{
					if (_tickFehler.Add(m))
					{
						Log.LogError("Module '" + m.Section + "': error in Update - " + ex);
					}
				}
			}
		}

		private void OnGUI()
		{
			if (Module == null)
			{
				return;
			}
			foreach (QolModule m in Module)
			{
				if (!m.Aktiv)
				{
					continue;
				}
				try
				{
					m.Gui();
				}
				catch (Exception ex)
				{
					if (_tickFehler.Add(m))
					{
						Log.LogError("Module '" + m.Section + "': error in OnGUI - " + ex);
					}
				}
			}
		}
	}
}
