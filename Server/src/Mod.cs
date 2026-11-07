// ---------------------------------------------------------------
//  Kaan's QoL  -  Server-Teil
//
//  Fasst die frueheren Server-Mods BalsamDauer und AlleAusgaenge
//  zusammen. Beide arbeiten beim Serverstart (IOnLoad) an der
//  Datenbank im Speicher; die Einstellungen stehen in config.json
//  neben der DLL (siehe Einstellungen.cs).
// ---------------------------------------------------------------
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;

// „Path“ gibt es auch in den EFT-Modellen (Wegpunkte) - Alias setzen.
using Pfad = System.IO.Path;

namespace KaansQoL.Server;

public record ModMetadata : IModMetadata
{
	public string ModGuid { get; init; } = "com.kaan.qol";
	public string Name { get; init; } = "KaansQoL";
	public string Author { get; init; } = "Kaan";
	public List<string>? Contributors { get; init; } = new();
	public SemanticVersioning.Version Version { get; init; } = new(1, 0, 0);
	public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1");
	public bool HasPrepatcher { get; init; } = false;
	public List<string>? Incompatibilities { get; init; } = new();
	public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new();
	public string? Url { get; init; } = "";
	public string License { get; init; } = "MIT";
}

[Injectable(InjectionType.Singleton)]
public class KaansQoLServer(
	ISptLogger<KaansQoLServer> logger,
	TemplateTable vorlagen,
	LocationTable karten) : IOnLoad
{
	public Task OnLoadAsync(CancellationToken abbruch)
	{
		try
		{
			string ordner = Pfad.GetDirectoryName(typeof(KaansQoLServer).Assembly.Location) ?? ".";

			// Sind die alten Einzel-Mods noch da? Dann setzen beide dieselben Werte (harmlos, aber doppelt).
			string mods = Pfad.GetDirectoryName(ordner.TrimEnd(Pfad.DirectorySeparatorChar, '/')) ?? ".";
			foreach (var alt in new[] { ("BalsamDauer", "BalsamDauer.dll"), ("AlleAusgaenge", "AlleAusgaenge.dll") })
			{
				if (File.Exists(Pfad.Combine(mods, alt.Item1, alt.Item2)))
				{
					logger.Warning($"[Kaan's QoL] The old server mod {alt.Item1} is still installed - it does the same work "
					               + "as the matching part of this mod. Move the folder out of user/mods to avoid doing it twice.");
				}
			}

			var einst = Einstellungen.Laden(ordner, s => logger.Info("[Kaan's QoL] " + s), s => logger.Warning("[Kaan's QoL] " + s));

			if (einst.BalsamDuration.Enabled)
			{
				BalsamDauer.Anwenden(logger, vorlagen, einst.BalsamDuration);
			}
			if (einst.AllExtracts.Enabled)
			{
				AlleAusgaenge.Anwenden(logger, karten, einst.AllExtracts);
			}
		}
		catch (Exception ex)
		{
			logger.Error("[Kaan's QoL] " + ex.Message);
		}
		return Task.CompletedTask;
	}
}
