// ---------------------------------------------------------------
//  Alles Annehmen  -  SPT 4.1.6
//
//  Ein Knopf im Nachrichtenfenster, der ALLE Anhaenge von ALLEN
//  Haendlern einsammelt und direkt ins Versteck legt. Kein
//  Uebergabefenster, kein Ziehen mit der Maus, kein Haendler-fuer-
//  Haendler-Durchklicken.
//
//  Wie das Spiel es selbst macht (nachgesehen im dekompilierten
//  Assembly-CSharp von SPT 4.1.6):
//    ChatScreen.TransferAll(dialog)
//      -> SocialNetwork.AllAttachmentsFromDialog(id, cb)   holt die
//         Nachrichten eines Gespraechs mit ihren Anhaengen vom Server
//      -> ChatScreen.ProcessRewards(...)                   sortiert
//         Aufgabengegenstaende / normale Gegenstaende / Profilboni
//      -> TransferItemsScreen.TransferAllItems()           schiebt
//         jeden Gegenstand mit QuickFindAppropriatePlace +
//         TryRunNetworkTransaction ins Versteck
//  Diese Mod macht genau dasselbe, nur ueber alle Gespraeche hinweg
//  und ohne die Fenster dazwischen.
// ---------------------------------------------------------------
using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.UI.Chat;
using HarmonyLib;
using UnityEngine;

namespace KaansQoL.AllesAnnehmen
{
	internal sealed class AllesAnnehmenModule : QolModule
	{
		public override string Id => "AllesAnnehmen";

		public override string Section => "04 Accept All";

		public override string Beschreibung =>
			"A button in the messenger window (and a hotkey) that takes ALL trader attachments from ALL traders straight into the stash.";

		public override string AlteGuid => "com.kaan.allesannehmen";

		public override string AlterName => "AllesAnnehmen";

		public override MigRow[] Umzug => new[]
		{
			new MigRow("Bedienung", "Knopf anzeigen", "Show button"),
			new MigRow("Bedienung", "Knopfbeschriftung", "Button label"),
			new MigRow("Bedienung", "Tastenkuerzel", "Hotkey"),
			new MigRow("Bedienung", "Anhang-Knopf umbiegen", "Redirect attachment button"),
			new MigRow("Umfang", "Profilboni mit einloesen", "Also redeem profile bonuses"),
			new MigRow("Umfang", "Alles als gelesen markieren", "Mark everything as read")
		};

		protected override void BindSettings(ConfigFile cfg)
		{
			string s = Section;
			Modul.An = Enabled;
			Modul.KnopfZeigen = Cfg.Bind(cfg, s, "Show button", true,
				"Puts a button at the top of the messenger window. Off: hotkey only.");
			Modul.KnopfText = Cfg.Bind(cfg, s, "Button label", "ACCEPT ALL", "What is written on the button.");
			Modul.Taste = Cfg.Bind(cfg, s, "Hotkey", new KeyboardShortcut(KeyCode.F9),
				"Only works while the messenger window is open. Also works if the button could not be placed.");
			Modul.AnhangKnopfUmbiegen = Cfg.Bind(cfg, s, "Redirect attachment button", true,
				"The paperclip button next to every trader (tooltip \"Accept All\") then clears ALL traders at once, "
				+ "instead of just that one and instead of opening the transfer window. Off: the button behaves "
				+ "like in the unmodified game.");
			Modul.ProfilBoniEinloesen = Cfg.Bind(cfg, s, "Also redeem profile bonuses", true,
				"Also accept rewards without an item (money, experience, trader reputation) immediately, "
				+ "instead of showing the reward window.");
			Modul.AlsGelesenMarkieren = Cfg.Bind(cfg, s, "Mark everything as read", true,
				"Clears the green unread counters at the end.");
		}

		public override void Apply(Harmony h)
		{
			h.PatchAll(typeof(AnhangKnopfPatch));
			h.PatchAll(typeof(NachrichtenfensterPatch));
		}
	}

	internal static class Modul
	{
		internal static ManualLogSource Log => KaansQoL.Plugin.Log;

		// Frueherer Name im Quelltext der Einzel-Mod.
		internal static ManualLogSource Protokoll => KaansQoL.Plugin.Log;

		internal static ConfigEntry<bool> An;
		internal static ConfigEntry<bool> KnopfZeigen;
		internal static ConfigEntry<string> KnopfText;
		internal static ConfigEntry<KeyboardShortcut> Taste;
		internal static ConfigEntry<bool> ProfilBoniEinloesen;
		internal static ConfigEntry<bool> AlsGelesenMarkieren;
		internal static ConfigEntry<bool> AnhangKnopfUmbiegen;
	}

	/// <summary>
	/// Biegt den spieleigenen Anhang-Knopf um. ChatScreen.TransferAll
	/// holt normalerweise nur EIN Gespraech ab und oeffnet danach das
	/// Uebergabefenster - wir machen daraus "alle, direkt ins Versteck".
	/// </summary>
	[HarmonyPatch(typeof(ChatScreen), "TransferAll")]
	internal static class AnhangKnopfPatch
	{
		[HarmonyPrefix]
		private static bool Davor(ChatScreen __instance)
		{
			if (!Modul.AnhangKnopfUmbiegen.Value)
			{
				return true; // Originalverhalten
			}

			try
			{
				Bedienteil.Ausloesen(__instance, null);
			}
			catch (Exception e)
			{
				Modul.Protokoll.LogError("Anhang-Knopf: " + e);
				return true;
			}
			return false; // Original nicht mehr ausfuehren
		}
	}

	/// <summary>
	/// Haengt sich an das Oeffnen des Nachrichtenfensters. ChatScreen.Show
	/// gibt es mehrfach (einmal geerbt von EftScreen) - wir nehmen die
	/// Fassung mit fuenf Parametern, das ist die echte.
	/// </summary>
	[HarmonyPatch]
	internal static class NachrichtenfensterPatch
	{
		[HarmonyPrepare]
		private static bool Vorbereiten()
		{
			return ZielMethode() != null;
		}

		[HarmonyTargetMethod]
		private static MethodBase ZielMethode()
		{
			return AccessTools.GetDeclaredMethods(typeof(ChatScreen))
				.FirstOrDefault(m => m.Name == "Show" && m.GetParameters().Length == 5);
		}

		[HarmonyPostfix]
		private static void Danach(ChatScreen __instance)
		{
			try
			{
				Bedienteil.Anbauen(__instance);
			}
			catch (Exception e)
			{
				Modul.Protokoll.LogError("Knopf/Taste liessen sich nicht anbauen: " + e);
			}
		}
	}
}
