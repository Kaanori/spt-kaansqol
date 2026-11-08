// ---------------------------------------------------------------
//  Kaan's QoL  -  Server-Teil
//
//  Fasst die frueheren Server-Mods BalsamDauer, AlleAusgaenge,
//  KeinAufgabenFehlschlag und den Server-Teil der GespraechsFolgen
//  zusammen. Die ersten drei arbeiten beim Serverstart (IOnLoad) an der
//  Datenbank im Speicher (Aufgaben-Umbau: Quests/KeinAufgabenFehlschlag.cs,
//  eigener IOnLoad mit spaeter Prioritaet); der Router (AufgabenRouter.cs)
//  beantwortet die Abfrage des Clients. Die Einstellungen stehen in
//  config.json neben der DLL (siehe Einstellungen.cs).
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
	public SemanticVersioning.Version Version { get; init; } = new(1, 1, 0);
	public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1");
	public bool HasPrepatcher { get; init; } = false;
	public List<string>? Incompatibilities { get; init; } = new();
	public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new();
	public string? Url { get; init; } = "";
	public string License { get; init; } = "MIT";
}

/// <summary>Erkennt, ob ein alter Einzel-Mod noch in user/mods liegt.</summary>
internal static class AlteMods
{
	public static bool Da(string ordnerName, string dll)
	{
		try
		{
			string ordner = Pfad.GetDirectoryName(typeof(AlteMods).Assembly.Location) ?? ".";
			string mods = Pfad.GetDirectoryName(ordner.TrimEnd(Pfad.DirectorySeparatorChar, '/')) ?? ".";
			return File.Exists(Pfad.Combine(mods, ordnerName, dll));
		}
		catch
		{
			return false;
		}
	}
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
			foreach (var alt in new[] { ("BalsamDauer", "BalsamDauer.dll"), ("AlleAusgaenge", "AlleAusgaenge.dll"),
				("KeinAufgabenFehlschlag", "KeinAufgabenFehlschlag.dll"), ("GespraechsFolgen", "GespraechsFolgenServer.dll") })
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
