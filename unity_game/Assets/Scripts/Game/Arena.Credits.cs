using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// The credits screen (title screen > CREDITS): every downloaded asset and its licence, from Resources/Credits.txt,
    /// a page at a time. CC BY assets need their credit shown somewhere the player can find it; this is that place.
    /// </summary>
    public sealed partial class Arena
    {
        const int CreditLinesPerPage = 22;
        int creditsPage;

        static List<string> CreditPages()
        {
            var pages = new List<string>();
            var asset = Resources.Load<TextAsset>("Credits");
            string[] lines = asset != null ? asset.text.Replace("\r", "").Split('\n') : new[] { "Credits file missing." };
            // Break at blank lines, so an entry isn't split across pages.
            var page = new List<string>();
            var entry = new List<string>();
            void Flush()
            {
                if (page.Count + entry.Count > CreditLinesPerPage && page.Count > 0)
                {
                    pages.Add(string.Join("\n", page).Trim());
                    page.Clear();
                }
                page.AddRange(entry);
                entry.Clear();
            }
            foreach (string line in lines)
            {
                entry.Add(line);
                if (line.Trim().Length == 0)
                    Flush();
            }
            Flush();
            if (page.Count > 0)
                pages.Add(string.Join("\n", page).Trim());
            pages.RemoveAll(p => p.Length == 0);
            return pages;
        }

        void ShowCredits()
        {
            List<string> pages = CreditPages();
            creditsPage = Mathf.Clamp(creditsPage, 0, Mathf.Max(0, pages.Count - 1));
            RectTransform s = OpenScreen(new Vector2(560f, 420f), "CREDITS");
            Text text = Ui.Label(s, pages.Count > 0 ? pages[creditsPage] : "", new Vector2(0f, 10f), new Vector2(520f, 320f), 11,
                TextAnchor.UpperLeft);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Ui.Label(s, $"{creditsPage + 1} / {Mathf.Max(1, pages.Count)}", new Vector2(0f, -175f), new Vector2(120f, 24f), 13).color = Ui.Muted;
            Ui.Button(s, "<", new Vector2(-90f, -175f), new Vector2(44f, 32f), () =>
            {
                creditsPage = Mathf.Max(0, creditsPage - 1);
                ShowCredits();
            }, 16);
            Ui.Button(s, ">", new Vector2(90f, -175f), new Vector2(44f, 32f), () =>
            {
                creditsPage = Mathf.Min(pages.Count - 1, creditsPage + 1);
                ShowCredits();
            }, 16);
            Ui.Button(s, "BACK", new Vector2(-215f, -175f), new Vector2(100f, 32f), ShowTitle, 14);
        }

        /// <summary>For tests: how many pages the credits take.</summary>
        public static int TestCreditPages => CreditPages().Count;
    }
}
