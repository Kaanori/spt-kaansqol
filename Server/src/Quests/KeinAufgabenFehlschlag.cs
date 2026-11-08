// ---------------------------------------------------------------
//  Kein Aufgaben-Fehlschlag  (frueher eigener Server-Mod KeinAufgabenFehlschlag 1.0.0)
//
//  Logik 1:1 uebernommen. Geaendert nur: die Einstellungen kommen aus dem
//  Abschnitt noQuestFailures der config.json von Kaan's QoL (Einstellungen.cs),
//  und der Umbau laeuft nicht, wenn der alte Einzel-Mod noch installiert ist.
//
//  WARUM: Am 03.11.2026 ist die Skier-Aufgabe "Unsinn" (Bullshit)
//  fuenfmal hintereinander fehlgeschlagen - einmal, als alle vier
//  Ziele schon erledigt waren: ein Scav auf Customs angegriffen,
//  Kaan wehrt sich, Aufgabe weg, alles von vorn.
//
//  WIE AUFGABEN IN SPT 4.1.6 FEHLSCHLAGEN (aus Client- und Server-
//  Dekompilat belegt):
//   - Jede Aufgabe hat eine Liste conditions.Fail. Das SPIEL prueft
//     sie (Quest.CheckForStatusChange, "TestAny"): ist EINE davon
//     erfuellt, steht die Aufgabe auf Fail bzw. FailRestartable
//     (Restartable=true). Nach dem Raid uebernimmt der Server den
//     Stand des Spiels (LocationLifecycleService.ProcessPostRaidQuests).
//   - Ausserhalb des Raids schickt das Spiel "QuestFail" an den Server
//     (QuestHelper.FailQuest), z. B. bei Haendler-Ruf (TraderStanding).
//   - Gibt man eine Aufgabe ab, scheitern alle Aufgaben, deren Fail-
//     Liste diese Aufgabe mit Status "Erfolg" nennt
//     (QuestHelper.GetQuestsFromProfileFailedByCompletingQuest) -
//     das sind Story-Verzweigungen ("entweder A oder B").
//   - Zeitlimits: Bedingung "QuestTime" in der Fail-Liste (Zeit seit
//     Annahme). Taegliche/Woechentliche laufen mit dem Wechsel ab
//     (endTime); der Server loescht dann angenommene, nicht fertige
//     Aufgaben samt Fortschritt (RepeatableQuestController.
//     ProcessExpiredQuests).
//
//  WAS DER MOD MACHT (beim Serverstart, nach allen anderen Mods, nur
//  im Speicher - keine Datei wird veraendert):
//   1. Fail-Bedingungen entfernen, die durch Spielweise ausgeloest
//      werden (Kills, Tod/Raid verlassen, Ruf, falscher Ort, ...).
//   2. Story-Verzweigungen behalten: Fail-Bedingungen vom Typ "Quest"
//      (entweder/oder) und "GlobalVariableValue" (Dialog-Entscheidung)
//      bleiben, unsichtbare Hilfsaufgaben (notDisplayedQuest, VisitAPI)
//      werden gar nicht angefasst. Ebenso ALLE Fail-Bedingungen einer
//      Aufgabe, deren Fehlschlag eine andere Aufgabe freischaltet
//      (z. B. "Hot Wheels" -> "Let's Try Again" -> "Inevitable
//      Response") - sonst waere dieser Zweig nie mehr erreichbar.
//   3. Zeitlimits ("QuestTime") entfernen.
//   4. Taegliche/Woechentliche: angenommene, noch nicht fertige
//      Aufgaben ueberleben den Wechsel mit ihrem Fortschritt.
//   Optional: Ein-Raid-Pflicht aufheben, Wartezeiten aufheben.
// ---------------------------------------------------------------
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using HarmonyLib;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Reflection.Patching;
// Path gibt es auch in den EFT-Modellen - Alias setzen.
using Path = System.IO.Path;

namespace KaansQoL.Server;

/// <summary>Laufzeitwerte des Umbaus (aus dem Abschnitt noQuestFailures der config.json).</summary>
public sealed class AufgabenEinstellungen
{
    public bool KeineFehlschlaege = true;
    public bool KeineZeitlimits = true;
    public bool AuchTaeglich = true;
    public bool StoryBehalten = true;
    public bool EinRaidAufheben = false;
    public bool KeineWartezeiten = false;
    public HashSet<string> Ausnahmen = new(StringComparer.OrdinalIgnoreCase);
    public bool Ausfuehrlich = true;

    public static AufgabenEinstellungen Von(FehlschlagEinstellungen c)
    {
        var e = new AufgabenEinstellungen
        {
            KeineFehlschlaege = c.NoFailures,
            KeineZeitlimits = c.NoTimeLimits,
            AuchTaeglich = c.IncludeDailies,
            StoryBehalten = c.KeepStoryBranches,
            EinRaidAufheben = c.LiftSingleRaid,
            KeineWartezeiten = c.NoWaitTimes,
            Ausfuehrlich = c.VerboseLog
        };
        foreach (var a in c.Exceptions)
            if (!string.IsNullOrWhiteSpace(a))
                e.Ausnahmen.Add(a.Trim());
        return e;
    }
}

// Moeglichst spaet, damit alle Mods ihre Aufgaben schon angelegt haben
// (VisitAPI, QuestBackport, WTT, Painter, Icebreaker ... laufen bis ~1000021).
[Injectable(TypePriority = int.MaxValue - 900)]
public class AufgabenUmbau(ISptLogger<AufgabenUmbau> logger, TemplateTable vorlagen) : IOnLoad
{
    private const string P = "[Kein Aufgaben-Fehlschlag] ";

    private sealed class Zaehler
    {
        public int Quests, FailEntfernt, ZeitEntfernt, VerzweigungBehalten, FailPfadQuests, EinRaid, Warte, Versteckt;
    }

    private static bool Versteckt(Quest q)
    {
        if (q.ExtensionData is null || !q.ExtensionData.TryGetValue("notDisplayedQuest", out var v) || v is null)
            return false;
        return v switch
        {
            bool b => b,
            JsonElement e => e.ValueKind == JsonValueKind.True,
            _ => string.Equals(v.ToString(), "true", StringComparison.OrdinalIgnoreCase)
        };
    }

    public Task OnLoadAsync(CancellationToken abbruch)
    {
        string ordner = Path.GetDirectoryName(typeof(AufgabenUmbau).Assembly.Location) ?? ".";

        // Ist der alte Einzel-Mod noch da, macht er genau diese Arbeit schon - nicht doppelt ausfuehren
        // (der Fail-Umbau waere harmlos, der Patch fuer Taegliche/Woechentliche wuerde aber doppelt greifen).
        if (AlteMods.Da("KeinAufgabenFehlschlag", "KeinAufgabenFehlschlag.dll"))
        {
            logger.Warning(P + "The old server mod KeinAufgabenFehlschlag is still installed - this part stays OFF so nothing is done twice. "
                           + "Move the folder out of user/mods to use the built-in version.");
            return Task.CompletedTask;
        }

        AufgabenEinstellungen cfg;
        try
        {
            var qol = Einstellungen.Laden(ordner, s => logger.Info(P + s), s => logger.Warning(P + s));
            if (!qol.NoQuestFailures.Enabled)
            {
                logger.Info(P + "noQuestFailures.enabled = false in config.json - quests stay as they are.");
                return Task.CompletedTask;
            }
            cfg = AufgabenEinstellungen.Von(qol.NoQuestFailures);
        }
        catch (Exception ex)
        {
            logger.Error(P + "config.json not readable, this part stays off: " + ex.Message);
            return Task.CompletedTask;
        }

        if (cfg.AuchTaeglich && cfg.KeineZeitlimits)
        {
            TaeglicheBehalten.Logger = logger;
            try { new TaeglicheBehalten().Enable(); }
            catch (Exception ex) { logger.Error(P + "Patch fuer Taegliche/Woechentliche nicht aktiv: " + ex.Message); }
        }

        try
        {
            Verarbeiten(cfg, ordner);
        }
        catch (Exception ex)
        {
            logger.Error(P + "Fehler beim Umbau der Aufgaben: " + ex);
        }
        return Task.CompletedTask;
    }

    private static readonly HashSet<QuestStatusEnum> FailStatus =
        new() { QuestStatusEnum.Fail, QuestStatusEnum.FailRestartable, QuestStatusEnum.MarkedAsFailed };

    private static IEnumerable<string> Ziele(QuestCondition c)
    {
        if (c.Target is null) yield break;
        if (c.Target.IsList && c.Target.List != null)
            foreach (var t in c.Target.List) yield return t;
        else if (c.Target.IsItem && c.Target.Item != null)
            yield return c.Target.Item;
    }

    private void Verarbeiten(AufgabenEinstellungen cfg, string modOrdner)
    {
        var quests = vorlagen.Quests;
        string Name(string id) =>
            MongoId.IsValidMongoId(id) && quests.TryGetValue(new MongoId(id), out var q) ? (q.QuestName ?? id) : id;

        // Aufgaben, deren FEHLSCHLAG eine andere Aufgabe freischaltet
        // (Status-Liste enthaelt Fail, aber nicht Erfolg).
        var failPfad = new Dictionary<string, List<string>>();
        foreach (var (qid, q) in quests)
            foreach (var c in q.Conditions?.AvailableForStart ?? new())
            {
                if (c.ConditionType != "Quest" || c.Status is null) continue;
                if (!c.Status.Overlaps(FailStatus) || c.Status.Contains(QuestStatusEnum.Success)) continue;
                foreach (var ziel in Ziele(c))
                {
                    if (!failPfad.TryGetValue(ziel, out var l)) failPfad[ziel] = l = new();
                    l.Add(q.QuestName ?? qid.ToString());
                }
            }

        var herkunft = new Herkunft(Path.GetDirectoryName(modOrdner) ?? modOrdner);
        var jeMod = new SortedDictionary<string, Zaehler>(StringComparer.OrdinalIgnoreCase);
        var details = new List<string>();
        var ausnahmenGenutzt = new List<string>();
        var failPfadGemeldet = new List<string>();
        int verzweigungsQuests = 0, versteckt = 0;

        foreach (var (qid, q) in quests)
        {
            if (q.Conditions is null) continue;
            string id = qid.ToString();
            string name = q.QuestName ?? id;

            bool hatEtwas = (q.Conditions.Fail?.Count ?? 0) > 0
                            || (cfg.EinRaidAufheben && (q.Conditions.AvailableForFinish?.Any(c => c.OneSessionOnly == true
                                || c.Counter?.Conditions?.Any(s => s.ResetOnSessionEnd == true) == true) ?? false))
                            || (cfg.KeineWartezeiten && (q.Conditions.AvailableForStart?.Any(c => c.AvailableAfter > 0 || c.Dispersion > 0) ?? false));
            if (!hatEtwas) continue;

            if (cfg.Ausnahmen.Contains(id) || cfg.Ausnahmen.Contains(name))
            {
                ausnahmenGenutzt.Add($"{name} ({id})");
                continue;
            }

            // Unsichtbare Hilfsaufgaben (notDisplayedQuest, z. B. VisitAPI-Storylines)
            // sind Story-Mechanik: ihr Fehlschlag ist gewollt (Zweig/Zustand umschalten).
            if (cfg.StoryBehalten && Versteckt(q))
            {
                if ((q.Conditions.Fail?.Count ?? 0) > 0)
                {
                    versteckt++;
                    var s0 = jeMod.TryGetValue(herkunft.Von(id), out var v0) ? v0 : jeMod[herkunft.Von(id)] = new Zaehler();
                    s0.Versteckt++;
                }
                continue;
            }

            var z = new Zaehler();
            var teile = new List<string>();

            // --- Fail-Liste ---
            var fail = q.Conditions.Fail;
            if (fail is { Count: > 0 })
            {
                if (cfg.StoryBehalten && failPfad.TryGetValue(id, out var folgen)
                    && fail.Any(c => c.ConditionType != "Quest"))
                {
                    // Fehlschlag ist hier ein eigener Story-Weg: nichts anfassen.
                    z.FailPfadQuests++;
                    failPfadGemeldet.Add($"{name} ({id}): Fehlschlag schaltet {string.Join(", ", folgen.Distinct())} frei - "
                                         + $"Fail-Bedingungen ({string.Join(", ", fail.Select(c => c.ConditionType))}) bleiben.");
                }
                else
                {
                    var entfernt = new List<string>();
                    fail.RemoveAll(c =>
                    {
                        bool zeit = c.ConditionType == "QuestTime";
                        // "Quest" = entweder/oder; "GlobalVariableValue" = Storyline-Variable
                        // (Dialog-Entscheidung, z. B. VisitAPI).
                        bool verzweigung = c.ConditionType is "Quest" or "GlobalVariableValue";
                        bool weg = (zeit && (cfg.KeineZeitlimits || cfg.KeineFehlschlaege))
                                   || (!zeit && cfg.KeineFehlschlaege && !(verzweigung && cfg.StoryBehalten));
                        if (!weg)
                        {
                            if (verzweigung) z.VerzweigungBehalten++;
                            return false;
                        }
                        if (zeit) z.ZeitEntfernt++; else z.FailEntfernt++;
                        entfernt.Add(Beschreiben(c, Name));
                        return true;
                    });
                    if (entfernt.Count > 0) teile.Add("Fail entfernt: " + string.Join("; ", entfernt));
                    if (z.VerzweigungBehalten > 0) verzweigungsQuests++;
                }
            }

            // --- Ein-Raid-Pflicht ---
            if (cfg.EinRaidAufheben)
                foreach (var c in q.Conditions.AvailableForFinish ?? new())
                {
                    if (c.OneSessionOnly == true) { c.OneSessionOnly = false; z.EinRaid++; }
                    foreach (var s in c.Counter?.Conditions ?? new())
                        if (s.ResetOnSessionEnd == true) { s.ResetOnSessionEnd = false; z.EinRaid++; }
                }
            if (z.EinRaid > 0) teile.Add($"Ein-Raid-Pflicht aufgehoben: {z.EinRaid}");

            // --- Wartezeiten ---
            if (cfg.KeineWartezeiten)
                foreach (var c in q.Conditions.AvailableForStart ?? new())
                    if (c.AvailableAfter > 0 || c.Dispersion > 0)
                    {
                        teile.Add($"Wartezeit {c.AvailableAfter ?? 0}s (+{c.Dispersion ?? 0}) aufgehoben");
                        c.AvailableAfter = 0;
                        c.Dispersion = 0;
                        z.Warte++;
                    }

            bool geaendert = z.FailEntfernt + z.ZeitEntfernt + z.EinRaid + z.Warte > 0;
            if (!geaendert && z.VerzweigungBehalten == 0 && z.FailPfadQuests == 0) continue;

            var s2 = jeMod.TryGetValue(herkunft.Von(id), out var vorh) ? vorh : jeMod[herkunft.Von(id)] = new Zaehler();
            if (geaendert) s2.Quests++;
            s2.FailEntfernt += z.FailEntfernt;
            s2.ZeitEntfernt += z.ZeitEntfernt;
            s2.VerzweigungBehalten += z.VerzweigungBehalten;
            s2.FailPfadQuests += z.FailPfadQuests;
            s2.EinRaid += z.EinRaid;
            s2.Warte += z.Warte;

            if (geaendert)
                details.Add($"  {name} ({id}) [{herkunft.Von(id)}]: {string.Join(" | ", teile)}"
                            + (z.VerzweigungBehalten > 0 ? $" | {z.VerzweigungBehalten} Verzweigung(en) behalten" : ""));
        }

        // Vorlagen der Taeglichen/Woechentlichen (ab Werk ohne Fail-Liste).
        int taeglichFail = 0;
        if (cfg.AuchTaeglich && cfg.KeineFehlschlaege)
        {
            var t = vorlagen.RepeatableQuests?.Templates;
            foreach (var rq in new[] { t?.Elimination, t?.Completion, t?.Exploration, t?.Pickup })
            {
                if (rq?.Conditions?.Fail is not { Count: > 0 } f) continue;
                taeglichFail += f.Count;
                f.Clear();
            }
        }

        // --- Protokoll ---
        var summe = new Zaehler();
        foreach (var (mod, z) in jeMod)
        {
            summe.Quests += z.Quests; summe.FailEntfernt += z.FailEntfernt; summe.ZeitEntfernt += z.ZeitEntfernt;
            summe.VerzweigungBehalten += z.VerzweigungBehalten; summe.FailPfadQuests += z.FailPfadQuests;
            summe.EinRaid += z.EinRaid; summe.Warte += z.Warte;
        }

        logger.Success(P + $"{summe.Quests} Aufgabe(n) geaendert: {summe.FailEntfernt} Fail-Bedingung(en) und "
                       + $"{summe.ZeitEntfernt} Zeitlimit(s) entfernt"
                       + (cfg.EinRaidAufheben ? $", {summe.EinRaid} Ein-Raid-Pflicht(en) aufgehoben" : "")
                       + (cfg.KeineWartezeiten ? $", {summe.Warte} Wartezeit(en) aufgehoben" : "")
                       + $". Behalten: {summe.VerzweigungBehalten} Story-Verzweigung(en) in {verzweigungsQuests} Aufgabe(n), "
                       + $"{summe.FailPfadQuests} Aufgabe(n) mit Fehlschlag-Pfad, {versteckt} unsichtbare Story-Hilfsaufgabe(n).");
        foreach (var (mod, z) in jeMod)
            logger.Info(P + $"  {mod}: {z.Quests} Aufgabe(n), {z.FailEntfernt} Fail, {z.ZeitEntfernt} Zeit"
                        + (cfg.EinRaidAufheben ? $", {z.EinRaid} Ein-Raid" : "")
                        + (cfg.KeineWartezeiten ? $", {z.Warte} Wartezeit" : "")
                        + $" | behalten: {z.VerzweigungBehalten} Verzweigung(en), {z.FailPfadQuests} Fehlschlag-Pfad, {z.Versteckt} Hilfsaufgabe(n)");
        if (taeglichFail > 0)
            logger.Info(P + $"  Taegliche/Woechentliche Vorlagen: {taeglichFail} Fail-Bedingung(en) entfernt.");
        foreach (var m in failPfadGemeldet)
            logger.Warning(P + "Ausnahme (Story): " + m);
        foreach (var a in ausnahmenGenutzt)
            logger.Info(P + "Ausnahme (config): " + a + " - nicht angefasst.");
        if (cfg.Ausfuehrlich)
            foreach (var d in details)
                logger.Info(P + d);
        if (cfg.AuchTaeglich && cfg.KeineZeitlimits)
            logger.Info(P + "Taegliche/Woechentliche: angenommene Aufgaben ueberleben den Wechsel.");
    }

    private static string Beschreiben(QuestCondition c, Func<string, string> name)
    {
        switch (c.ConditionType)
        {
            case "Quest":
                return $"Quest({string.Join("/", Ziele(c).Select(name))} Status {string.Join(",", c.Status ?? new())})";
            case "CounterCreator":
                var unter = c.Counter?.Conditions?.Select(s =>
                {
                    string ziel = s.Target is null ? "" : s.Target.IsList ? string.Join(",", s.Target.List ?? new()) : s.Target.Item ?? "";
                    if (s.SavageRole is { Count: > 0 }) ziel += (ziel.Length > 0 ? "," : "") + string.Join(",", s.SavageRole);
                    if (s.Status is { Count: > 0 }) ziel += (ziel.Length > 0 ? "," : "") + string.Join(",", s.Status);
                    return ziel.Length > 0 ? $"{s.ConditionType}:{ziel}" : s.ConditionType;
                }) ?? Enumerable.Empty<string>();
                return $"Zaehler[{string.Join(" + ", unter)}]";
            default:
                return $"{c.ConditionType}({string.Join(",", Ziele(c))})";
        }
    }
}

/// <summary>
/// Ordnet eine Quest-ID dem Mod zu, dessen Aufgaben-Datei sie als
/// Schluessel oder _id enthaelt. Sonst "SPT-Basis".
/// </summary>
internal sealed class Herkunft
{
    private readonly List<(string Mod, string Text)> _dateien = new();
    private readonly Dictionary<string, string> _cache = new();

    public Herkunft(string modsOrdner)
    {
        try
        {
            foreach (var datei in Directory.EnumerateFiles(modsOrdner, "*.json", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(modsOrdner, datei);
                if (rel.Contains("bundles", StringComparison.OrdinalIgnoreCase)) continue;
                if (!rel.Contains("quest", StringComparison.OrdinalIgnoreCase)) continue;
                var info = new FileInfo(datei);
                if (info.Length > 20_000_000) continue;
                string mod = rel.Split(Path.DirectorySeparatorChar)[0];
                _dateien.Add((mod, File.ReadAllText(datei)));
            }
        }
        catch
        {
            // Zuordnung ist nur fuers Protokoll
        }
    }

    public string Von(string id)
    {
        if (_cache.TryGetValue(id, out var m)) return m;
        var schluessel = new Regex("\"" + Regex.Escape(id) + "\"\\s*:\\s*\\{|\"_id\"\\s*:\\s*\"" + Regex.Escape(id) + "\"");
        var mods = _dateien.Where(d => d.Text.Contains(id) && schluessel.IsMatch(d.Text))
                           .Select(d => d.Mod).Distinct().ToList();
        return _cache[id] = mods.Count == 0 ? "SPT-Basis" : string.Join("+", mods);
    }
}

/// <summary>
/// Taegliche/Woechentliche: beim Wechsel loescht der Server angenommene,
/// nicht fertige Aufgaben (RepeatableQuestController.ProcessExpiredQuests;
/// fertige zum Abgeben behaelt er schon von sich aus). Hier werden auch die
/// angenommenen behalten - Status und Zaehler bleiben im Profil.
/// </summary>
public class TaeglicheBehalten : AbstractPatch
{
    public static ISptLogger<AufgabenUmbau>? Logger;

    public TaeglicheBehalten() : base("com.kaan.qol.taegliche") { }

    protected override MethodBase? GetTargetMethod()
        => AccessTools.Method(typeof(RepeatableQuestController), "ProcessExpiredQuests",
                              new[] { typeof(PmcDataRepeatableQuest), typeof(PmcData) });

    [PatchPrefix]
    private static void Prefix(PmcDataRepeatableQuest generatedRepeatables, PmcData pmcData,
                               ref List<RepeatableQuest>? __state)
    {
        __state = null;
        var aktiv = generatedRepeatables?.ActiveQuests;
        if (aktiv is null || pmcData?.Quests is null) return;

        var behalten = aktiv.Where(r => pmcData.Quests.Any(s => s.QId == r.Id && s.Status == QuestStatusEnum.Started)).ToList();
        if (behalten.Count == 0) return;

        // Aus der Liste nehmen, damit der Server sie nicht als abgelaufen loescht.
        aktiv.RemoveAll(r => behalten.Contains(r));
        __state = behalten;
    }

    [PatchPostfix]
    private static void Postfix(PmcDataRepeatableQuest generatedRepeatables, ref List<RepeatableQuest>? __state)
    {
        if (__state is null || generatedRepeatables is null) return;
        generatedRepeatables.ActiveQuests ??= new();
        generatedRepeatables.ActiveQuests.InsertRange(0, __state);
        Logger?.Info($"[Kein Aufgaben-Fehlschlag] {generatedRepeatables.Name}: {__state.Count} angenommene Aufgabe(n) "
                     + "ueber den Wechsel behalten (" + string.Join(", ", __state.Select(r => r.Id.ToString())) + ").");
    }
}
