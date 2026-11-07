// ---------------------------------------------------------------
//  Heli ohne Fackel
//
//  Der Hubschrauber-Ausgang auf dem Eisbrecher (Manimal's
//  Icebreaker) ist erst offen, wenn man in der Landezone eine
//  gruene Leuchtpistole abschiesst. Das klappt nicht zuverlaessig,
//  und der Mod hat keinen Schalter dafuer.
//
//  WIE DER MOD ES MACHT (IcebreakerHeliExfil.Start):
//  Er schiebt den Ausgang 1000 m unter das Schiff, baut eine
//  Leuchtpistolen-Zone und wartet auf deren Ereignis. Kommt es,
//  laeuft CallHeli(): Anflug-Animation, 44 s Wartezeit, dann
//  HeliArrival() - Ausgang zurueck an seinen Platz, Status offen.
//
//  WAS HIER PASSIERT: ein Postfix hinter Start() ruft CallHeli()
//  sofort selbst auf. Der Heli kommt also zu Raidbeginn und ist
//  nach 44 s da - ohne Fackel. Der ganze Rest (Animation, Ton,
//  Freischalten) bleibt die Logik des Mods.
//
//  Die Kosten (2400 Euro) sind NICHT hier geregelt, sondern im
//  Mod selbst: BepInEx/config/com.manimal.icebreaker.cfg,
//  HeliExfilCost = 0.
//
//  Alles laeuft ueber Reflection, weil die Klasse des Mods
//  internal Member hat und wir nicht gegen seine DLL bauen.
// ---------------------------------------------------------------
using System;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace KaansQoL.HeliOhneFackel
{
    internal sealed class HeliOhneFackelModule : QolModule
    {
        public override string Id => "HeliOhneFackel";

        public override string Section => "10 Icebreaker Heli Without Flare";

        public override string Beschreibung =>
            "The Icebreaker helicopter exit (Manimal's Icebreaker) is called at raid start without having to fire the green flare. "
            + "Takes effect at the next raid start. The exit cost is set in the Icebreaker's own config (HeliExfilCost).";

        public override string AlteGuid => "kaan.heliohnefackel";

        public override string AlterName => "HeliOhneFackel";

        public override string[] BenoetigtPlugins => new[] { "com.manimal.icebreaker" };

        // Die alte Mod hatte keine Einstellungen - nichts zu uebernehmen.

        protected override void BindSettings(ConfigFile cfg)
        {
        }

        public override void Apply(Harmony h)
        {
            var typ = AccessTools.TypeByName("Manimal.Icebreaker.IcebreakerHeliExfil");
            var start = typ == null ? null : AccessTools.Method(typ, "Start");
            Modul.CallHeli = typ == null ? null : AccessTools.Method(typ, "CallHeli");
            Modul.Unsubscribe = typ == null ? null : AccessTools.Field(typ, "_unsubscribe");
            if (start == null || Modul.CallHeli == null || Modul.Unsubscribe == null)
            {
                Modul.Log.LogError("[HeliOhneFackel] IcebreakerHeliExfil nicht gefunden - Eisbrecher-Mod geaendert? Nichts gepatcht.");
                return;
            }
            h.Patch(start, postfix: new HarmonyMethod(typeof(Modul), nameof(Modul.NachStart)));
            Modul.Log.LogInfo("[HeliOhneFackel] bereit");
        }
    }

    internal static class Modul
    {
        internal static ManualLogSource Log => KaansQoL.Plugin.Log;

        internal static MethodInfo CallHeli;

        internal static FieldInfo Unsubscribe;

        internal static void NachStart(object __instance)
        {
            try
            {
                // _unsubscribe ist nur gesetzt, wenn der Mod die Fackel-
                // Sperre wirklich scharf gemacht hat. Sonst ist der Ausgang
                // ohnehin offen oder das Objekt wurde schon zerstoert.
                if (Unsubscribe.GetValue(__instance) == null)
                    return;
                CallHeli.Invoke(__instance, new object[] { false });
                Log.LogInfo("[HeliOhneFackel] Heli ohne Fackel gerufen - Ausgang in 44 s offen");
            }
            catch (Exception ex)
            {
                Log.LogWarning("[HeliOhneFackel] Heli-Ruf fehlgeschlagen: " + ex.Message);
            }
        }
    }
}
