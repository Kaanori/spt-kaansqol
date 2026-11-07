// ---------------------------------------------------------------
//  Balsam-Dauer  (frueher eigener Server-Mod BalsamDauer)
//
//  Setzt beim Serverstart die Wirkungsdauer des Goldstern-Balsams
//  (golden_star_balm) auf einen einstellbaren Wert. Standard: 15 min.
//
//  Ab Werk haelt der Balsam
//      Schmerz (Pain)          350 s  = 5:50 min
//      Strahlung (RadExposure) 400 s  = 6:40 min
//  Beide werden auf die eingestellte Dauer gehoben.
//
//  Warum ein eigener Mod:
//  SVM kennt keine Effektdauern einzelner Gegenstaende - im ganzen
//  Vorgabensatz steht dazu nichts. Und die Datenbank direkt anfassen
//  geht seit SPT 4.1 nicht mehr: sie ist pruefsummengesichert, eine
//  geaenderte items.json bricht den Serverstart ab. Mods duerfen
//  dieselbe Tabelle zur Laufzeit aendern - dafuer sind sie da.
//
//  Absichtlich NICHT angefasst: die drei StimulatorBuffs des Balsams
//  (BuffsGoldenStarBalm, je 5 s). Das sind +1 Energie und +1 Fluessig-
//  keit PRO SEKUNDE - auf 15 min gezogen waeren das +900 Energie und
//  +900 Fluessigkeit aus einem Balsam. Die 5 s sind hier kein Tippfehler
//  von BSG, sondern die Art, wie die Aufnahme verrechnet wird.
//
//  Die Dauer steht in config.json (balsamDuration.minutes).
// ---------------------------------------------------------------
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace KaansQoL.Server;

internal static class BalsamDauer
{
	// Goldstern-Balsam ("Zvezdochka"), Vorlagen-ID aus templates/items.json.
	private const string BalsamId = "5751a89d24597722aa0e8db0";

	public static void Anwenden(ISptLogger<KaansQoLServer> logger, TemplateTable vorlagen, BalsamEinstellungen einst)
	{
		double minuten = einst.Minutes;
		List<string> effekte = einst.Effects.Count > 0 ? einst.Effects : new List<string> { "Pain", "RadExposure" };

		if (minuten <= 0)
		{
			logger.Info("[Balsam-Dauer] minutes <= 0 - nothing to do.");
			return;
		}

		var sekunden = minuten * 60.0;

		if (!vorlagen.Items.TryGetValue(BalsamId, out var balsam))
		{
			logger.Warning($"[Balsam-Dauer] Vorlage {BalsamId} nicht gefunden - "
			               + "hat sich die ID geaendert?");
			return;
		}

		var schaden = balsam.Properties?.EffectsDamage;
		if (schaden is null || schaden.Count == 0)
		{
			logger.Warning("[Balsam-Dauer] Der Balsam hat keine effects_damage - "
			               + "nichts zu aendern.");
			return;
		}

		var geaendert = new List<string>();

		foreach (var name in effekte)
		{
			if (!Enum.TryParse<DamageEffectType>(name, ignoreCase: true, out var art))
			{
				logger.Warning($"[Balsam-Dauer] Unbekannter Effekt \"{name}\" in "
				               + "config.json - uebersprungen.");
				continue;
			}

			if (!schaden.TryGetValue(art, out var eigenschaften))
			{
				logger.Warning($"[Balsam-Dauer] Der Balsam hat keinen Effekt {art} - "
				               + "uebersprungen.");
				continue;
			}

			var vorher = eigenschaften.Duration ?? 0;
			eigenschaften.Duration = sekunden;
			geaendert.Add($"{art} {vorher:0} s -> {sekunden:0} s");
		}

		if (geaendert.Count == 0)
		{
			logger.Warning("[Balsam-Dauer] Kein einziger Effekt geaendert.");
			return;
		}

		logger.Success($"[Balsam-Dauer] Goldstern-Balsam auf {minuten} min: "
		               + string.Join(", ", geaendert) + ".");
	}
}
