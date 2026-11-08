// ZonenKills (08.11.2026)
//
// Tarkov prueft bei Kill-Aufgaben mit Zone ("ConditionInZone") nur die Zonen
// des OPFERS: Player.cs ruft beim Tod StatisticsManager.OnEnemyKill(...,
// TriggerZones, ...) mit den TriggerZones des getoeteten Spielers auf. Wer in
// der Zone steht und einen Gegner ausserhalb erschiesst, bekommt nichts.
//
// Dieser Prefix nimmt die Zonen des Schuetzen (BaseStatisticsManager.Player)
// dazu: Ein Kill zaehlt, wenn der Gegner ODER man selbst in der Zone ist.
// Die Liste des Opfers wird nicht veraendert (es wird eine neue Liste gebaut).
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using EFT;
using HarmonyLib;

namespace KaansQoL.ZonenKills
{
	internal sealed class ZonenKillsModule : QolModule
	{
		public override string Id => "ZonenKills";

		public override string Section => "14 Zone Kills";

		public override string Beschreibung => "Kills count for zone quests while YOU stand in the zone.";

		public override string AlteGuid => "com.kaan.zonenkills";

		public override string AlterName => "ZonenKills";

		public override MigRow[] Umzug => new[]
		{
			new MigRow("Allgemein", "Kills aus der Zone heraus zaehlen", "Enabled")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			Modul.An = Enabled;
		}

		public override void Apply(Harmony h)
		{
			var m = AccessTools.Method(typeof(BaseStatisticsManager), nameof(BaseStatisticsManager.OnEnemyKill));
			if (m == null)
			{
				Plugin.Log.LogError("[ZonenKills] BaseStatisticsManager.OnEnemyKill nicht gefunden - Modul inaktiv.");
				return;
			}
			h.Patch(m, prefix: new HarmonyMethod(typeof(Modul), nameof(Modul.Vorher)));
		}
	}

	internal static class Modul
	{
		internal static ConfigEntry<bool> An;

		public static void Vorher(BaseStatisticsManager __instance, ref List<string> zoneIds)
		{
			try
			{
				if (!An.Value)
				{
					return;
				}
				List<string> eigene = __instance?.Player?.TriggerZones;
				if (eigene == null || eigene.Count == 0)
				{
					return;
				}
				var alle = (zoneIds != null) ? new List<string>(zoneIds) : new List<string>();
				int vorher = alle.Count;
				foreach (string z in eigene)
				{
					if (!alle.Contains(z))
					{
						alle.Add(z);
					}
				}
				if (alle.Count != vorher)
				{
					Plugin.Log.LogInfo("[ZonenKills] Kill: eigene Zonen dazugenommen [" + string.Join(", ", eigene) + "]");
				}
				zoneIds = alle;
			}
			catch (Exception e)
			{
				Plugin.Log.LogError("[ZonenKills] OnEnemyKill: " + e.Message);
			}
		}
	}
}
