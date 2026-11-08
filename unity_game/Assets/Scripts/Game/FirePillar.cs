using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The Mage's level 3 attack: a column of fire bursting up from the floor where the triangle aimed. It erupts
    /// fast, roars for a moment and fades. Looks to choose between in the Variant Lab: A flame column, B energy
    /// column, C flame inside energy with a ring on the floor.
    /// </summary>
    public sealed class FirePillar : MonoBehaviour
    {
        public static readonly string[] StyleNames = { "A  FLAME", "B  ENERGY", "C  BOTH" };
        const float Height = 3f;
        const float Radius = 0.45f;
        const float RiseSeconds = 0.25f;
        const float LifeSeconds = 1.6f;

        Bubble flame;
        Bubble energy;
        Bubble ring;
        Color color;
        int style;
        float age;

        /// <summary>Editor preview: shows the pillar this many seconds in.</summary>
        public void ShowAt(float seconds)
        {
            age = seconds;
            Update();
        }

        public static FirePillar Spawn(Vector3 floorPoint, Color color, int style, Transform parent)
        {
            var go = new GameObject("Fire Pillar");
            go.transform.SetParent(parent, false);
            go.transform.position = floorPoint;
            var pillar = go.AddComponent<FirePillar>();
            pillar.color = color;
            pillar.style = style;
            Mesh column = Resources.Load<Mesh>("CylinderMesh");
            if (style != 1)
            {
                pillar.flame = Bubble.Create("Flame", go.transform);
                pillar.flame.GetComponent<MeshFilter>().sharedMesh = column;
            }
            if (style != 0)
            {
                pillar.energy = Bubble.Create("Energy", go.transform);
                pillar.energy.GetComponent<MeshFilter>().sharedMesh = column;
            }
            if (style == 2)
                pillar.ring = Bubble.Create("Ring", go.transform);
            pillar.Update();
            return pillar;
        }

        void Update()
        {
            age += Time.deltaTime;
            float rise = Mathf.Clamp01(age / RiseSeconds);
            float eased = 1f - (1f - rise) * (1f - rise);
            float fade = Mathf.Clamp01((LifeSeconds - age) / 0.5f);
            float height = Height * eased;
            float flicker = 1f + Mathf.Sin(age * 37f) * 0.05f;

            if (flame != null)
            {
                flame.transform.localPosition = Vector3.up * (height * 0.5f);
                flame.transform.localScale = new Vector3(Radius * 2f * flicker, Mathf.Max(0.02f, height * 0.5f), Radius * 2f * flicker);
                flame.Show(color, BubbleStyle.Flame, fade);
            }
            if (energy != null)
            {
                float widen = style == 2 ? 1.35f : 1f;
                energy.transform.localPosition = Vector3.up * (height * 0.5f);
                energy.transform.localScale = new Vector3(Radius * 2f * widen, Mathf.Max(0.02f, height * 0.525f), Radius * 2f * widen);
                energy.Show(Color.Lerp(color, Color.white, 0.3f), BubbleStyle.Energy, fade * (style == 2 ? 0.6f : 1f));
            }
            if (ring != null)
            {
                float spread = Mathf.Clamp01(age / 0.6f);
                ring.transform.localPosition = Vector3.up * 0.02f;
                ring.transform.localScale = new Vector3(Radius * 2f * (1f + 2f * spread), 0.04f, Radius * 2f * (1f + 2f * spread));
                ring.Show(color, BubbleStyle.Energy, (1f - spread) * fade);
            }
            if (age >= LifeSeconds)
                Destroy(gameObject);
        }
    }
}
