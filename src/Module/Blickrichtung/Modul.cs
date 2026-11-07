// ---------------------------------------------------------------
//  Blickrichtung
//
//  Mit gehaltener Umsehen-Taste (ab Werk mittlere Maustaste) dreht
//  sich nur der Kopf. Laesst man los, holt das Spiel den Kopf
//  zurueck nach vorne. Dieses Plugin dreht es um: beim Loslassen
//  dreht sich der KOERPER in die Richtung, in die man gerade
//  schaut - die Kamera bleibt stehen.
//
//  WO DER HEBEL SITZT: Die Kopfdrehung liegt in Player._horizontal
//  (links/rechts) und Player._vertical (hoch/runter). Loslassen der
//  Taste loest ECommand.ResetLookDirection aus, das setzt nur
//  _resetLook = true. Im naechsten Player.Look() wird das
//  ausgewertet und der Kopf faedelt per Lerp zurueck auf 0.
//
//  Wir haengen uns VOR Player.Look(): steht _resetLook an, geben
//  wir den Kopfwinkel per Player.Rotate() an den Koerper weiter und
//  setzen ihn auf 0. Dann gibt es nichts mehr zurueckzuholen.
//
//  VORZEICHEN: Look() rechnet _horizontal -= MausX, Rotate() dagegen
//  Gierwinkel += MausX. Deshalb wandert -_horizontal in den Koerper.
//  Hoch/runter laufen gleichsinnig, dort wird der tatsaechlich
//  angezeigte Kopfwinkel (mit der Abschwaechung aus Look()) genommen.
//
//  Einstellbar unter F12 -> 02 Turn To Look Direction.
// ---------------------------------------------------------------
using BepInEx;
using BepInEx.Configuration;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace KaansQoL.Blickrichtung
{
    internal sealed class BlickrichtungModule : QolModule
    {
        public override string Id => "Blickrichtung";

        public override string Section => "02 Turn To Look Direction";

        public override string Beschreibung =>
            "On releasing the look-around key (middle mouse) the body turns to the look direction instead of the head snapping back.";

        public override string AlteGuid => "kaan.blickrichtung";

        public override string AlterName => "Blickrichtung";

        public override MigRow[] Umzug => new[]
        {
            new MigRow("Allgemein", "Aktiv", "Enabled"),
            new MigRow("Allgemein", "Hoehe uebernehmen", "Adopt pitch")
        };

        protected override void BindSettings(ConfigFile cfg)
        {
            Modul.Aktiv = Enabled;
            Modul.HoeheUebernehmen = Cfg.Bind(cfg, Section, "Adopt pitch", true,
                "Also adopt looking up/down. Off = left/right only, the pitch snaps back.");
        }

        public override void Apply(Harmony h)
        {
            h.PatchAll(typeof(LookPatch));
        }
    }

    internal static class Modul
    {
        internal static ConfigEntry<bool> Aktiv;
        internal static ConfigEntry<bool> HoeheUebernehmen;
    }

    [HarmonyPatch(typeof(Player), nameof(Player.Look))]
    internal static class LookPatch
    {
        [HarmonyPrefix]
        private static void Prefix(Player __instance)
        {
            if (!Modul.Aktiv.Value || !__instance._resetLook || !__instance.IsYourPlayer)
                return;

            float gier = -__instance._horizontal;
            float nick = Modul.HoeheUebernehmen.Value ? __instance.HeadRotation.x : 0f;
            if (Mathf.Abs(gier) < 0.01f && Mathf.Abs(nick) < 0.01f)
                return;

            // ignoreClamp: sonst begrenzt die Drehgeschwindigkeit pro Bild
            // (und die Mausempfindlichkeit) den Winkel und der Koerper
            // dreht nur ein Stueck weit mit.
            __instance.Rotate(new Vector2(gier, nick), ignoreClamp: true);

            __instance._horizontal = 0f;
            if (Modul.HoeheUebernehmen.Value)
                __instance._vertical = 0f;
        }
    }
}
