using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// The other player as you see them: their real body is in passthrough, so this only adds what the game
    /// knows. That means a name and health bar over their head, their shield bubble, and small markers at their
    /// head and palms. The markers show how well calibration lined the two headsets up.
    /// </summary>
    public sealed class RemotePlayer
    {
        const float Smoothing = 15f;
        const float BarHeight = 0.32f;

        readonly Transform head;
        readonly RectTransform tag;
        readonly Text tagText;
        readonly Bar bar;
        readonly Bubble shield;
        readonly Bubble[] markers = new Bubble[3];
        PlayerSnapshot latest;
        Vector3 shownHead;
        Vector3 shownLeft;
        Vector3 shownRight;
        float sinceUpdate = 99f;
        float shieldShown;

        public bool Visible { get; private set; }
        public PlayerSnapshot Latest => latest;
        public bool ShowMarkers { get; set; } = true;

        public RemotePlayer(Transform parent, Transform localHead)
        {
            head = localHead;
            tag = Ui.Canvas("Remote Tag", parent, new Vector2(200f, 50f));
            tagText = Ui.Label(tag, "", new Vector2(0f, 12f), new Vector2(200f, 24f), 18, TextAnchor.MiddleCenter, FontStyle.Bold);
            bar = Ui.Bar(tag, new Vector2(0f, -12f), new Vector2(180f, 16f), new Color(0.05f, 0.05f, 0.07f, 0.9f), new Color(0.3f, 1f, 0.4f));
            bar.Trail = true;
            shield = Bubble.Create("Remote Shield", parent);
            for (int i = 0; i < markers.Length; i++)
                markers[i] = Bubble.Create("Remote Marker " + i, parent);
            Hide();
        }

        /// <summary>A new snapshot, already turned into this headset's world.</summary>
        public void Receive(in PlayerSnapshot snapshot)
        {
            bool first = sinceUpdate > 2f;
            latest = snapshot;
            sinceUpdate = 0f;
            if (first)
            {
                shownHead = snapshot.Head;
                shownLeft = snapshot.LeftPalm;
                shownRight = snapshot.RightPalm;
            }
        }

        public void Tick(float dt)
        {
            sinceUpdate += dt;
            Visible = sinceUpdate < NetSession.TimeoutSeconds;
            if (!Visible)
            {
                Hide();
                return;
            }

            float follow = 1f - Mathf.Exp(-Smoothing * dt);
            shownHead = Vector3.Lerp(shownHead, latest.Head, follow);
            shownLeft = Vector3.Lerp(shownLeft, latest.LeftPalm, follow);
            shownRight = Vector3.Lerp(shownRight, latest.RightPalm, follow);
            Color color = latest.Color;

            tag.gameObject.SetActive(true);
            Vector3 tagPoint = shownHead + Vector3.up * BarHeight;
            Vector3 toTag = tagPoint - head.position;
            tag.position = tagPoint;
            tag.rotation = Quaternion.LookRotation(toTag, Vector3.up);
            tag.localScale = Vector3.one * (Mathf.Clamp(toTag.magnitude, 1f, 5f) * Ui.MetresPerUnit * 0.7f);
            CharacterDef character = CharacterDef.Get((CharacterId)Mathf.Clamp(latest.Character, 0, CharacterDef.All.Length - 1));
            tagText.text = latest.Dead ? "KNOCKED OUT" : $"{character.Name.ToUpperInvariant()}  LV {latest.Level}";
            tagText.color = color;
            bar.Fraction = latest.MaxHp > 0f ? latest.Hp / latest.MaxHp : 0f;
            bar.Tick(dt);

            shieldShown = Mathf.MoveTowards(shieldShown, latest.ShieldUp ? 1f : 0f, 8f * dt);
            shield.transform.position = shownHead + Vector3.down * 0.35f;
            shield.transform.localScale = Vector3.one * 1.24f;
            shield.Show(color, BubbleStyle.Hex, shieldShown);

            ShowMarker(0, shownHead + Vector3.up * 0.15f, 0.06f, color, ShowMarkers);
            ShowMarker(1, shownLeft, 0.04f, color, ShowMarkers && latest.LeftTracked);
            ShowMarker(2, shownRight, 0.04f, color, ShowMarkers && latest.RightTracked);
        }

        void ShowMarker(int index, Vector3 at, float size, Color color, bool on)
        {
            markers[index].transform.position = at;
            markers[index].transform.localScale = Vector3.one * size;
            markers[index].Show(color, BubbleStyle.Energy, on ? 0.8f : 0f);
        }

        void Hide()
        {
            tag.gameObject.SetActive(false);
            shield.Show(Color.white, BubbleStyle.Hex, 0f);
            foreach (Bubble marker in markers)
                marker.Show(Color.white, BubbleStyle.Energy, 0f);
        }
    }
}
