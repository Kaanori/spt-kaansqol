// ---------------------------------------------------------------
//  Namen in der Spielsprache (ge, sonst was eingestellt ist; das Spiel
//  faellt selbst auf en zurueck).
//
//  VisitAPI-Unteraufgaben haben einen LEEREN Namen - im Kapitel-Fenster
//  steht stattdessen der Text ihres Ziels ("Behalte das Fragment von
//  Objekt 1156 fuer dich"). Genau den nehmen wir dann auch.
// ---------------------------------------------------------------
using System;
using EFT;

namespace KaansQoL.GespraechsFolgen
{
	internal static class Namen
	{
		public static bool Deutsch()
		{
			try
			{
				var c = LocalizationManager.Instance?.Culture ?? "";
				return c.StartsWith("ge", StringComparison.OrdinalIgnoreCase) || c.StartsWith("de", StringComparison.OrdinalIgnoreCase);
			}
			catch
			{
				return false;
			}
		}

		public static string Text(string schluessel)
		{
			if (string.IsNullOrEmpty(schluessel))
				return null;
			try
			{
				var s = schluessel.Localized();
				return string.IsNullOrWhiteSpace(s) || s == schluessel ? null : s;
			}
			catch
			{
				return null;
			}
		}

		public static string Quest(string id)
		{
			var n = Text(id + " name");
			if (n != null)
				return n;
			var quests = Daten.Letzte;
			if (quests != null && quests.TryGetValue(id, out var q))
				foreach (var b in q.Ziel)
				{
					var t = Text(b.Id);
					if (t != null)
						return t;
				}
			return id;
		}

		public static string Bedingung(string id) => Text(id) ?? id;
	}
}
