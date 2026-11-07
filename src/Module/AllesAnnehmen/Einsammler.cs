// ---------------------------------------------------------------
//  Einsammler: die eigentliche Arbeit.
//
//  Ablauf
//    1. Alle Gespraeche mit Anhaengen heraussuchen.
//    2. Fuer jedes die Anhaenge vom Server holen
//       (SocialNetwork.AllAttachmentsFromDialog).
//    3. Profilboni (Geld, Erfahrung, Haendlerruf) einloesen.
//    4. Alle Gegenstaende in ein Zwischenraster (TemporaryGrid) legen
//       und einen nach dem anderen ins Versteck schieben. Genau so
//       macht es das Spiel im Uebergabefenster; wir sparen nur das
//       Fenster.
//
//  Wichtig: forcePutInStash: true. Ohne das wuerde das Spiel die
//  Sachen auch in Taschen und Rucksack stopfen - gewollt ist aber
//  ausdruecklich das Versteck.
// ---------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChatShared;
using EFT;
using Diz.LanguageExtensions;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Chat;
using UnityEngine;

namespace KaansQoL.AllesAnnehmen
{
	internal static class Einsammler
	{
		private static bool _laeuft;

		internal static void Starten(ChatScreen fenster, SocialNetwork netz, ItemController verwalter, DefaultUIButton knopf)
		{
			if (_laeuft)
			{
				Melden("Laeuft noch ...");
				return;
			}

			// Bewusst ohne await: der Aufrufer ist ein Klick-Verteiler.
			// Arbeiten() faengt selbst alles ab.
			var _ = Arbeiten(fenster, netz, verwalter, knopf);
		}

		private static async Task Arbeiten(ChatScreen fenster, SocialNetwork netz, ItemController verwalter, DefaultUIButton knopf)
		{
			_laeuft = true;
			if (knopf != null)
			{
				knopf.Interactable = false;
			}

			try
			{
				List<DialogueChatMessage> nachrichten = await NachrichtenHolen(netz);
				if (nachrichten.Count == 0)
				{
					// Auch ohne Anhaenge soll die Post als gelesen gelten.
					int nurGelesen = Modul.AlsGelesenMarkieren.Value
						? AlsGelesenMarkieren(fenster, netz)
						: 0;
					Melden(nurGelesen > 0
						? "Keine Anhaenge da, " + nurGelesen + " Gespraeche als gelesen markiert."
						: "Keine Anhaenge da.");
					return;
				}

				int boni = 0;
				if (Modul.ProfilBoniEinloesen.Value)
				{
					boni = await BoniEinloesen(netz, nachrichten);
				}

				Ergebnis erg = await GegenstaendeInsVersteck(nachrichten, verwalter);

				foreach (DialogueChatMessage n in nachrichten)
				{
					if (n != null && n.UpdateRewardStatus != null)
					{
						n.UpdateRewardStatus.Invoke();
					}
				}

				int gelesen = Modul.AlsGelesenMarkieren.Value
					? AlsGelesenMarkieren(fenster, netz)
					: 0;

				Melden(Bilanz(erg, boni, gelesen), erg.Abgebrochen);
			}
			catch (Exception e)
			{
				Modul.Protokoll.LogError("Einsammeln fehlgeschlagen: " + e);
				Melden("Fehlgeschlagen - siehe BepInEx-Protokoll.", true);
			}
			finally
			{
				_laeuft = false;
				if (knopf != null)
				{
					knopf.Interactable = true;
				}
			}
		}

		// --- Schritt 1 + 2: Nachrichten mit Anhaengen einsammeln ---------

		private static async Task<List<DialogueChatMessage>> NachrichtenHolen(SocialNetwork netz)
		{
			List<UpdatableChatDialogue> gespraeche = Gespraeche(netz);
			var gesammelt = new List<DialogueChatMessage>();
			var gesehen = new HashSet<string>();

			foreach (UpdatableChatDialogue g in gespraeche)
			{
				DialogueChatMessage[] teil = await AnhaengeHolen(netz, g._id);
				if (teil == null)
				{
					continue;
				}

				foreach (DialogueChatMessage n in teil)
				{
					if (n == null || !n.HasRewards)
					{
						continue;
					}
					// Abgelaufene Pakete gibt der Server nicht mehr heraus -
					// falls doch, ueberspringen wir sie lieber.
					if (n.MaxStorageTime > 0L && n.ExpirationTime <= DateTimeExtensions.UtcNow)
					{
						continue;
					}
					if (!string.IsNullOrEmpty(n._id) && !gesehen.Add(n._id))
					{
						continue;
					}
					gesammelt.Add(n);
				}
			}

			return gesammelt;
		}

		/// <summary>Gespraeche, in denen etwas liegt. Findet der Filter
		/// nichts, fragen wir zur Sicherheit alle ab.</summary>
		private static List<UpdatableChatDialogue> Gespraeche(SocialNetwork netz)
		{
			var alle = netz.Dialogues
				.Where(d => d != null && !string.IsNullOrEmpty(d._id) && d.Type != EMessageType.GlobalChat)
				.ToList();

			var mitAnhang = alle.Where(HatAnhang).ToList();
			return mitAnhang.Count > 0 ? mitAnhang : alle;
		}

		private static bool HatAnhang(UpdatableChatDialogue d)
		{
			try
			{
				return d.HasMessagesWithRewards || d.AttachmentsNew > 0 || d.AnyRewardMessage;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>Der Serveraufruf des Spiels arbeitet mit Rueckruf - hier
		/// zu einem await umgebogen.</summary>
		private static Task<DialogueChatMessage[]> AnhaengeHolen(SocialNetwork netz, string gespraechId)
		{
			var warte = new TaskCompletionSource<DialogueChatMessage[]>();
			try
			{
				netz.AllAttachmentsFromDialog(gespraechId, nachrichten => warte.TrySetResult(nachrichten));
			}
			catch (Exception e)
			{
				Modul.Protokoll.LogWarning("Anhaenge von " + gespraechId + ": " + e.Message);
				warte.TrySetResult(null);
			}
			return warte.Task;
		}

		// --- Schritt 3: Profilboni ---------------------------------------

		private static async Task<int> BoniEinloesen(SocialNetwork netz, List<DialogueChatMessage> nachrichten)
		{
			List<ProfileChangeEvent> offen = nachrichten
				.Where(n => n.ProfileChangeEvents != null)
				.SelectMany(n => n.ProfileChangeEvents)
				.Where(e => e != null && !e.Redeemed)
				.ToList();

			if (offen.Count == 0)
			{
				return 0;
			}

			bool ok = await netz.RedeemProfileRewards(offen);
			if (!ok)
			{
				Modul.Protokoll.LogWarning("Server hat die Profilboni abgelehnt.");
				return 0;
			}

			foreach (ProfileChangeEvent e in offen)
			{
				e.Redeem();
			}
			return offen.Count;
		}

		// --- Schritt 4: Gegenstaende ins Versteck ------------------------

		private struct Ergebnis
		{
			internal int Verschoben;
			internal int Gesamt;
			internal bool Abgebrochen;
		}

		private static async Task<Ergebnis> GegenstaendeInsVersteck(
			List<DialogueChatMessage> nachrichten, ItemController verwalter)
		{
			var erg = default(Ergebnis);

			ItemUiContext oberflaeche = ItemUiContext.Instance;
			if (oberflaeche == null)
			{
				Modul.Protokoll.LogWarning("ItemUiContext fehlt - nichts verschoben.");
				erg.Abgebrochen = true;
				return erg;
			}

			// Nachricht fuer Nachricht. Das Zwischenraster des Spiels ist
			// fest 10x25 Felder gross - schuettet man ALLE Haendler auf
			// einmal hinein, kann es ueberlaufen. Pro Nachricht passt es
			// immer.
			foreach (DialogueChatMessage nachricht in nachrichten)
			{
				if (nachricht.Items == null || nachricht.Items.Stash == null)
				{
					continue;
				}

				Item[] sachen = nachricht.Items.Stash.Grid.Items.Where(i => i != null).ToArray();
				if (sachen.Length == 0)
				{
					continue;
				}

				erg.Gesamt += sachen.Length;
				Teilergebnis teil = await EineNachricht(sachen, verwalter, oberflaeche);
				erg.Verschoben += teil.Verschoben;
				if (!teil.Weiter)
				{
					// Versteck voll oder Server sagt nein - alles Weitere
					// waere vergebliche Muehe.
					erg.Abgebrochen = true;
					erg.Gesamt += nachrichten
						.SkipWhile(n => n != nachricht).Skip(1)
						.Where(n => n.Items != null && n.Items.Stash != null)
						.Sum(n => n.Items.Stash.Grid.Items.Count(i => i != null));
					break;
				}
			}

			return erg;
		}

		private struct Teilergebnis
		{
			internal int Verschoben;
			internal bool Weiter;
		}

		/// <summary>Schiebt die Anhaenge EINER Nachricht ins Versteck.
		/// Weiter == false heisst: nicht mehr weitermachen.</summary>
		private static async Task<Teilergebnis> EineNachricht(
			Item[] sachen, ItemController verwalter, ItemUiContext oberflaeche)
		{
			int verschoben = 0;
			bool weiter = true;

			TemporaryGrid zwischen;
			try
			{
				zwischen = new TemporaryGrid(sachen, 10, 25, true);
			}
			catch (Exception e)
			{
				Modul.Protokoll.LogWarning(
					"Zwischenraster liess sich nicht fuellen: " + e.Message);
				return new Teilergebnis { Verschoben = 0, Weiter = true };
			}

			try
			{
				var quelle = new DefaultItemContext<Item>(
					zwischen.Grid.ParentItem, EItemViewType.TransferTrader);

				for (Item stueck = zwischen.Grid.Items.FirstOrDefault();
					stueck != null;
					stueck = zwischen.Grid.Items.FirstOrDefault())
				{
					OperationResult plan = oberflaeche.QuickFindAppropriatePlace(
						quelle.CreateChild(stueck), verwalter,
						forcePutInStash: true, displayWarnings: false, simulate: true);

					bool schiefgegangen = plan.Failed || !verwalter.CanExecute(plan.Value);
					if (!schiefgegangen)
					{
						schiefgegangen = (await verwalter.TryRunNetworkTransaction(plan)).Failed;
					}

					if (schiefgegangen)
					{
						Modul.Protokoll.LogWarning(
							"Stopp bei \"" + Name(stueck) + "\": "
							+ (plan.Failed ? plan.Error.ToString() : "Versteck voll oder Zug abgelehnt"));
						weiter = false;
						break;
					}

					verschoben++;
					await Task.Yield();
				}
			}
			finally
			{
				zwischen.Dispose();
			}

			return new Teilergebnis { Verschoben = verschoben, Weiter = weiter };
		}

		private static string Name(Item stueck)
		{
			try
			{
				return stueck.LocalizedName();
			}
			catch
			{
				return stueck != null ? stueck.Id : "?";
			}
		}

		// --- Nachbereitung ------------------------------------------------

		/// <summary>Markiert ALLE Gespraeche als gelesen - auf dem Server
		/// und in der Anzeige. Gibt zurueck, wie viele es waren.
		///
		/// Zwei Fallstricke, die beide zubeissen:
		///  * SocialNetwork.AddDialogueToReadQueue nimmt ein Gespraech nur
		///    an, wenn New > 0 UND AttachmentsNew != 0. Ein Gespraech mit
		///    ungelesener Nachricht aber ohne Anhang faellt also durch.
		///    Deshalb schreiben wir direkt in die Warteschlange.
		///  * Der Serveraufruf allein aendert die Zahlen an den Gespraechen
		///    nicht. Die stehen in dialogue.New / dialogue.AttachmentsNew
		///    und werden nur ueber die Setzer der DialogueView neu
		///    gezeichnet.</summary>
		private static int AlsGelesenMarkieren(ChatScreen fenster, SocialNetwork netz)
		{
			int anzahl = 0;

			try
			{
				if (netz.ReadDialoguesQueue == null)
				{
					netz.ReadDialoguesQueue = new List<UpdatableChatDialogue>();
				}

				foreach (UpdatableChatDialogue g in netz.Dialogues.ToList())
				{
					if (g == null || string.IsNullOrEmpty(g._id) || g.Type == EMessageType.GlobalChat)
					{
						continue;
					}
					if (g.New <= 0 && g.AttachmentsNew <= 0)
					{
						continue; // war schon gelesen
					}
					if (!netz.ReadDialoguesQueue.Contains(g))
					{
						netz.ReadDialoguesQueue.Add(g);
						anzahl++;
					}
				}

				if (anzahl > 0)
				{
					// Server: client/mail/dialog/read - loescht dort sowohl
					// den Ungelesen- als auch den Anhang-Hinweis.
					netz.ReadDialogues();
				}
			}
			catch (Exception e)
			{
				Modul.Protokoll.LogWarning("Als gelesen melden: " + e.Message);
			}

			try
			{
				if (fenster != null)
				{
					foreach (DialogueView ansicht in fenster.GetComponentsInChildren<DialogueView>(true))
					{
						if (ansicht == null || ansicht._dialogue == null)
						{
							continue;
						}
						ansicht.NewMessagesCount = 0;
						ansicht.NewAttachmentsMessagesCount = 0;
					}
				}

				// Sicherheitsnetz: Gespraeche, zu denen es gerade keine
				// sichtbare Zeile gibt (ausgeblendet im Scroller).
				foreach (UpdatableChatDialogue g in netz.Dialogues.ToList())
				{
					if (g != null)
					{
						g.New = 0;
						g.AttachmentsNew = 0;
					}
				}

				// Die Zahl unten rechts in der Menueleiste zieht sich ihre
				// Summe selbst aus den Gespraechen.
				MenuTaskBar leiste = UnityEngine.Object.FindObjectOfType<MenuTaskBar>();
				if (leiste != null)
				{
					leiste.SetNewMessagesCount();
				}
			}
			catch (Exception e)
			{
				Modul.Protokoll.LogWarning("Zaehler zuruecksetzen: " + e.Message);
			}

			Modul.Protokoll.LogInfo(anzahl + " Gespraeche als gelesen markiert.");
			return anzahl;
		}

		private static string Bilanz(Ergebnis erg, int boni, int gelesen)
		{
			string text = erg.Verschoben + " von " + erg.Gesamt + " Gegenstaenden ins Versteck";
			if (boni > 0)
			{
				text += ", " + boni + " Belohnungen eingeloest";
			}
			if (gelesen > 0)
			{
				text += ", " + gelesen + " Gespraeche gelesen";
			}
			if (erg.Abgebrochen)
			{
				text += " - Rest passt nicht mehr hinein";
			}
			return text + ".";
		}

		private static void Melden(string text, bool warnung = false)
		{
			Modul.Protokoll.LogInfo(text);
			try
			{
				NotificationManager.DisplayMessageNotification(
					"Alles Annehmen: " + text,
					ENotificationDurationType.Long,
					warnung ? ENotificationIconType.Alert : ENotificationIconType.Mail,
					warnung ? (Color?)Color.yellow : null);
			}
			catch
			{
				// Ohne Einblendung ist es auch nicht schlimm.
			}
		}
	}
}
