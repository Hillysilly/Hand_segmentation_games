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
        readonly Bubble glow;
        readonly Bubble[] markers = new Bubble[3];
        PlayerSnapshot latest;
        Vector3 shownHead;
        Vector3 shownLeft;
        Vector3 shownRight;
        float sinceUpdate = 99f;
        float shieldShown;

        public bool Visible { get; private set; }
        /// <summary>Where their head is drawn (smoothed).</summary>
        public Vector3 ShownHead => shownHead;
        /// <summary>The floor's height, so their shield can wrap their whole body.</summary>
        public float FloorY { get; set; }
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
            glow = Bubble.Create("Remote Powerup Glow", parent);
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
            bool archer = character.Id == CharacterId.Archer;
            if (archer)
                ShieldView.Place(shield.transform, shownHead, latest.HeadRotation * Vector3.forward, 1f);
            else
            {
                // A bubble round the whole body, head to feet (yours is placed for your own view, round your upper body).
                float tall = Mathf.Clamp(shownHead.y - FloorY, 1f, 2.2f);
                shield.transform.position = new Vector3(shownHead.x, FloorY + tall * 0.5f + 0.05f, shownHead.z);
                shield.transform.rotation = Quaternion.identity;
                float grow = Mathf.Lerp(0.6f, 1f, 1f - (1f - shieldShown) * (1f - shieldShown));
                shield.transform.localScale = new Vector3(1.05f, tall + 0.4f, 1.05f) * grow;
            }
            shield.Show(color, archer ? BubbleStyle.Glass : BubbleStyle.Hex, shieldShown);

            // Powerups others can see: a gold star-like glow when invincible, red when doing 1.5x damage.
            bool powered = latest.Invincible || latest.Boosted;
            glow.transform.position = shownHead + Vector3.down * 0.6f;
            glow.transform.localScale = new Vector3(0.8f, 1.7f, 0.8f);
            glow.Show(latest.Invincible ? new Color(1f, 0.85f, 0.3f) : new Color(1f, 0.2f, 0.15f), BubbleStyle.Energy,
                powered ? 0.45f + 0.15f * Mathf.Sin(Time.time * 10f) : 0f);

            // Their aura is the real one, drawn by the arena (Arena.NetLooks).

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
            glow.Show(Color.white, BubbleStyle.Energy, 0f);
            foreach (Bubble marker in markers)
                marker.Show(Color.white, BubbleStyle.Energy, 0f);
        }
    }
}
