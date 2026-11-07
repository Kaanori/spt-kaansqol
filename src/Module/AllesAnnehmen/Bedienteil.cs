// ---------------------------------------------------------------
//  Bedienteil: der Knopf im Nachrichtenfenster und das Tastenkuerzel.
//
//  Der Knopf ist eine KOPIE des spieleigenen Senden-Knopfes
//  (ChatScreen._sendButton). So sieht er aus wie alles andere im
//  Spiel - gleiche Schrift, gleiche Farben, gleicher Klickton - ohne
//  dass wir ein eigenes Aussehen bauen muessten.
//
//  Der Klick-Verteiler eines frisch kopierten DefaultUIButton ist
//  leer: OnClick ist ein readonly-Feld, Unity kopiert es nicht mit.
//  Wir bekommen also keine Senden-Funktion mit vererbt.
// ---------------------------------------------------------------
using System;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Chat;
using UnityEngine;
using UnityEngine.UI;

namespace KaansQoL.AllesAnnehmen
{
	internal static class Bedienteil
	{
		private const string KnopfName = "AlleAnnehmenKnopf";

		/// <summary>Einmal pro Nachrichtenfenster: Knopf setzen und den
		/// Tastenwaechter anhaengen.</summary>
		internal static void Anbauen(ChatScreen fenster)
		{
			if (fenster == null)
			{
				return;
			}

			// Der Waechter fuer das Tastenkuerzel haengt am Fenster selbst -
			// er laeuft damit nur, solange es das Fenster gibt.
			if (fenster.gameObject.GetComponent<Tastenwaechter>() == null)
			{
				fenster.gameObject.AddComponent<Tastenwaechter>().Fenster = fenster;
			}

			if (!Modul.KnopfZeigen.Value)
			{
				return;
			}

			// Show() laeuft bei jedem Oeffnen erneut - den Knopf aber nur
			// einmal bauen.
			if (fenster.transform.Find(KnopfName) != null || Suche(fenster) != null)
			{
				return;
			}

			DefaultUIButton vorlage = fenster._sendButton;
			if (vorlage == null)
			{
				Modul.Protokoll.LogWarning(
					"Kein Senden-Knopf zum Nachbauen gefunden - es bleibt beim Tastenkuerzel.");
				return;
			}

			Transform eltern = fenster._friendsButton != null
				? fenster._friendsButton.transform.parent
				: fenster.transform;

			DefaultUIButton knopf = UnityEngine.Object.Instantiate(vorlage, eltern, false);
			knopf.name = KnopfName;
			knopf.transform.localScale = Vector3.one;
			knopf.SetRawText(Modul.KnopfText.Value, 18);
			// SetEnabledTooltip legt selbst eine HoverTooltipArea an -
			// SetTooltips wuerde nur die Felder beschreiben.
			knopf.SetEnabledTooltip(
				"Take all attachments from all traders into the stash", true);

			// Wenn die Kopfleiste ihre Kinder selbst anordnet, lassen wir sie
			// das tun. Sonst setzen wir den Knopf von Hand neben "FREUNDE".
			LayoutElement platz = knopf.GetComponent<LayoutElement>();
			bool wirdAngeordnet = eltern.GetComponent<LayoutGroup>() != null;

			if (wirdAngeordnet)
			{
				if (fenster._friendsButton != null)
				{
					knopf.transform.SetSiblingIndex(fenster._friendsButton.transform.GetSiblingIndex());
				}
				if (platz != null)
				{
					// Ohne Hoehe drueckt eine Layout-Gruppe den Knopf auf
					// 0 Pixel zusammen - er belegt dann Platz, ist aber
					// unsichtbar. Genau das ist beim ersten Versuch passiert.
					platz.ignoreLayout = false;
					platz.minWidth = 190f;
					platz.preferredWidth = 190f;
					platz.flexibleWidth = 0f;
					platz.minHeight = 26f;
					platz.preferredHeight = 26f;
					platz.flexibleHeight = 0f;
				}
			}
			else
			{
				if (platz != null)
				{
					platz.ignoreLayout = true;
				}
				VonHandSetzen(fenster, knopf);
			}

			knopf.OnClick.AddListener(() => Ausloesen(fenster, knopf));
			knopf.gameObject.SetActive(true);

			Modul.Protokoll.LogInfo(
				"Knopf gesetzt an: " + Pfad(knopf.transform) + (wirdAngeordnet ? " (Layout)" : " (von Hand)"));

			// Erst nach einem Bild steht fest, wie gross die Layout-Gruppe
			// den Knopf gemacht hat. Ist er platt, setzen wir ihn von Hand.
			fenster.StartCoroutine(Nachmessen(fenster, knopf));
		}

		private static System.Collections.IEnumerator Nachmessen(ChatScreen fenster, DefaultUIButton knopf)
		{
			yield return null;
			yield return null;

			RectTransform rt = knopf == null ? null : knopf.transform as RectTransform;
			if (rt == null)
			{
				yield break;
			}

			Rect r = rt.rect;
			Modul.Protokoll.LogInfo(
				"Knopfgroesse: " + r.width.ToString("0") + " x " + r.height.ToString("0"));

			if (r.width >= 20f && r.height >= 10f)
			{
				yield break;
			}

			Modul.Protokoll.LogWarning(
				"Knopf wurde von der Layout-Gruppe plattgedrueckt - setze ihn von Hand.");
			LayoutElement platz = knopf.GetComponent<LayoutElement>();
			if (platz != null)
			{
				platz.ignoreLayout = true;
			}
			VonHandSetzen(fenster, knopf);
		}

		/// <summary>Ohne Layout-Gruppe: den Knopf an die Stelle links neben
		/// den Freunde-Knopf rechnen.</summary>
		private static void VonHandSetzen(ChatScreen fenster, DefaultUIButton knopf)
		{
			RectTransform ziel = knopf.transform as RectTransform;
			if (ziel == null)
			{
				return;
			}

			RectTransform vorbild = fenster._friendsButton != null
				? fenster._friendsButton.transform as RectTransform
				: null;

			if (vorbild != null)
			{
				ziel.anchorMin = vorbild.anchorMin;
				ziel.anchorMax = vorbild.anchorMax;
				ziel.pivot = vorbild.pivot;
				float hoehe = vorbild.rect.height > 10f ? vorbild.rect.height : 26f;
				ziel.sizeDelta = new Vector2(190f, hoehe);
				ziel.anchoredPosition = vorbild.anchoredPosition - new Vector2(200f, 0f);
			}
			else
			{
				// Letzte Rueckfallebene: oben links ins Fenster.
				ziel.anchorMin = new Vector2(0f, 1f);
				ziel.anchorMax = new Vector2(0f, 1f);
				ziel.pivot = new Vector2(0f, 1f);
				ziel.sizeDelta = new Vector2(190f, 26f);
				ziel.anchoredPosition = new Vector2(8f, -6f);
			}
		}

		private static DefaultUIButton Suche(ChatScreen fenster)
		{
			foreach (DefaultUIButton k in fenster.GetComponentsInChildren<DefaultUIButton>(true))
			{
				if (k != null && k.name == KnopfName)
				{
					return k;
				}
			}
			return null;
		}

		private static string Pfad(Transform t)
		{
			string p = t.name;
			while (t.parent != null)
			{
				t = t.parent;
				p = t.name + "/" + p;
			}
			return p;
		}

		/// <summary>Gemeinsamer Einstieg fuer Knopf und Taste.</summary>
		internal static void Ausloesen(ChatScreen fenster, DefaultUIButton knopf)
		{
			if (fenster == null)
			{
				return;
			}

			ItemController verwalter = fenster._itemController;
			if (fenster._social == null || verwalter == null)
			{
				Modul.Protokoll.LogWarning(
					"Nachrichtenfenster ist noch nicht fertig geladen.");
				return;
			}

			Einsammler.Starten(fenster, fenster._social, verwalter, knopf);
		}

		/// <summary>Horcht auf das Tastenkuerzel, solange das
		/// Nachrichtenfenster existiert.</summary>
		internal class Tastenwaechter : MonoBehaviour
		{
			internal ChatScreen Fenster;

			private void Update()
			{
				if (Fenster == null || !Fenster.gameObject.activeInHierarchy || !Modul.An.Value)
				{
					return;
				}

				try
				{
					if (Modul.Taste.Value.IsDown())
					{
						Ausloesen(Fenster, null);
					}
				}
				catch (Exception e)
				{
					Modul.Protokoll.LogError("Tastenkuerzel: " + e);
				}
			}
		}
	}
}
