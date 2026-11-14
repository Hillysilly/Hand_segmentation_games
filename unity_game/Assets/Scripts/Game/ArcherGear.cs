using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// The Valkyr's weapons as real models (CC0: KayKit crossbow and bolt, Quaternius bow and arrow):
    /// - Level 1: a crossbow appears in each finger-gun hand. While it reloads a bolt slides onto it and its tip
    ///   glows brighter; when it's ready the bolt flares. The shot flies as the bolt.
    /// - Level 2: making the bow puts a longbow in the front fist, its string running to the drawing hand, with an
    ///   arrow nocked. The arrowhead glows and a ring spins up as you draw. The shot flies as the arrow.
    /// The models come from Resources/Props (built by PropModels).
    /// </summary>
    public sealed class ArcherGear
    {
        readonly Transform parent;
        readonly Transform[] crossbows = new Transform[2];
        readonly Transform[] bolts = new Transform[2];
        readonly FxSpin[] boltGlows = new FxSpin[2];
        readonly FxSpin[] crossRings = new FxSpin[2];
        Transform bow;
        Transform nocked;
        FxSpin arrowGlow;
        FxSpin bowRing;
        LineRenderer bowString;

        /// <summary>Bow string ends, in the bow's own space (from the model's measured size).</summary>
        static readonly Vector3 TopTip = new Vector3(0f, 0.56f, -0.29f);
        static readonly Vector3 BottomTip = new Vector3(0f, -0.56f, -0.29f);

        public ArcherGear(Transform parent) => this.parent = parent;

        static Transform Load(string key, Transform parent)
        {
            var prefab = Resources.Load<GameObject>("Props/" + key);
            if (prefab == null)
            {
                Debug.LogWarning($"[Arena] No prop Props/{key}");
                return new GameObject(key).transform;
            }
            Transform made = Object.Instantiate(prefab, parent, false).transform;
            made.name = key;
            return made;
        }

        /// <summary>
        /// One hand's crossbow: shown while the finger gun is held. <paramref name="ready"/> goes 0 to 1 as it reloads.
        /// </summary>
        public void Crossbow(int hand, bool show, Vector3 palm, Vector3 direction, float ready, Color color)
        {
            if (crossbows[hand] == null)
            {
                if (!show)
                    return;
                crossbows[hand] = Load("Crossbow", parent);
                bolts[hand] = Load("Bolt", crossbows[hand]);
                boltGlows[hand] = BallLook.Sprite("Bolt Glow", crossbows[hand], "FxGlow", 0.06f, color).Set(billboard: true, flicker: 0.15f);
                crossRings[hand] = BallLook.Sprite("Charge Ring", crossbows[hand], "FxRing", 0.12f, color).Set(roll: 360f);
            }
            Transform crossbow = crossbows[hand];
            if (crossbow.gameObject.activeSelf != show)
                crossbow.gameObject.SetActive(show);
            if (!show)
                return;
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, direction);
            crossbow.SetPositionAndRotation(palm + direction * 0.06f + up.normalized * 0.03f,
                Quaternion.LookRotation(direction, up.sqrMagnitude > 1e-4f ? up : Vector3.up));
            // The bolt slides from the back of the stock onto the rail as it reloads; ready, it sits at the front.
            float eased = Mathf.SmoothStep(0f, 1f, ready);
            bolts[hand].localPosition = new Vector3(0f, 0.035f, Mathf.Lerp(-0.28f, -0.1f, eased));
            bolts[hand].gameObject.SetActive(ready > 0.05f);
            Vector3 tip = bolts[hand].localPosition + Vector3.forward * 0.32f;
            boltGlows[hand].transform.localPosition = tip;
            bool full = ready >= 1f;
            float pulse = full ? 1f + 0.25f * Mathf.Sin(Time.time * 14f) : 1f;
            boltGlows[hand].transform.localScale = Vector3.one * Mathf.Lerp(0.02f, 0.09f, eased) * pulse;
            SetColor(boltGlows[hand], color * (full ? 2f : 1f));
            crossRings[hand].transform.localPosition = new Vector3(0f, 0.035f, 0.2f);
            crossRings[hand].transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * Mathf.Lerp(90f, 720f, eased));
            crossRings[hand].transform.localScale = Vector3.one * (full ? 0.13f * pulse : Mathf.Lerp(0.03f, 0.1f, eased));
            SetColor(crossRings[hand], color * (full ? 1.6f : 0.8f));
        }

        /// <summary>The longbow: shown while formed. <paramref name="draw"/> goes 0 to 1 as the string is pulled.</summary>
        public void Bow(bool show, Vector3 grip, Vector3 stringHand, Vector3 direction, float draw, Color color)
        {
            if (bow == null)
            {
                if (!show)
                    return;
                bow = Load("Bow", parent);
                nocked = Load("Arrow", parent);
                arrowGlow = BallLook.Sprite("Arrow Glow", nocked, "FxGlow", 0.08f, color).Set(billboard: true, flicker: 0.12f);
                bowRing = BallLook.Sprite("Bow Ring", bow, "FxRing", 0.2f, color).Set(roll: 360f);
                var go = new GameObject("Bow String", typeof(LineRenderer));
                go.transform.SetParent(parent, false);
                bowString = go.GetComponent<LineRenderer>();
                bowString.sharedMaterial = Resources.Load<Material>("AimLine");
                bowString.positionCount = 3;
                bowString.useWorldSpace = true;
                bowString.widthMultiplier = 0.004f;
                bowString.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                Tint.Line(bowString, new Color(0.9f, 0.9f, 0.85f));
            }
            if (bow.gameObject.activeSelf != show)
            {
                bow.gameObject.SetActive(show);
                nocked.gameObject.SetActive(show);
                bowString.enabled = show;
            }
            if (!show)
                return;
            // Held upright, canted a little, grip in the front fist.
            Vector3 up = Vector3.ProjectOnPlane(Vector3.up, direction).normalized;
            Quaternion facing = Quaternion.LookRotation(direction, up) * Quaternion.Euler(0f, 0f, -12f);
            bow.SetPositionAndRotation(grip, facing);
            Vector3 top = bow.TransformPoint(TopTip);
            Vector3 bottom = bow.TransformPoint(BottomTip);
            // The string runs to the drawing hand once pulled past where it rests.
            Vector3 rest = bow.TransformPoint(new Vector3(0f, 0f, -0.29f));
            Vector3 pulled = draw > 0.02f ? stringHand : rest;
            bowString.SetPosition(0, top);
            bowString.SetPosition(1, pulled);
            bowString.SetPosition(2, bottom);
            nocked.SetPositionAndRotation(pulled, Quaternion.LookRotation(direction, up));
            arrowGlow.transform.localPosition = Vector3.forward * 0.75f;
            arrowGlow.transform.localScale = Vector3.one * Mathf.Lerp(0.02f, 0.14f, draw) * (draw >= 0.95f ? 1f + 0.2f * Mathf.Sin(Time.time * 16f) : 1f);
            SetColor(arrowGlow, color * Mathf.Lerp(0.6f, 2f, draw));
            bowRing.transform.localPosition = new Vector3(0f, 0f, 0.05f);
            bowRing.transform.localRotation = Quaternion.Euler(0f, 0f, Time.time * Mathf.Lerp(60f, 900f, draw));
            bowRing.transform.localScale = Vector3.one * Mathf.Lerp(0.05f, 0.24f, draw);
            SetColor(bowRing, color * Mathf.Lerp(0.3f, 1.5f, draw));
        }

        public void Hide()
        {
            Crossbow(0, false, default, Vector3.forward, 0f, Color.white);
            Crossbow(1, false, default, Vector3.forward, 0f, Color.white);
            Bow(false, default, default, Vector3.forward, 0f, Color.white);
        }

        static readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        static void SetColor(FxSpin sprite, Color color)
        {
            var renderer = sprite.GetComponent<Renderer>();
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }

        /// <summary>Makes a fired shot look like a bolt or an arrow flying (the shot's crackling ball hidden).</summary>
        public static void Dress(Blast shot, string key, Color color)
        {
            BallLook.HideCrackle(shot.Head, false);
            foreach (Renderer renderer in shot.Head.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
            shot.TailScale = 0.35f;
            Transform model = Load(key, shot.transform);
            // The shot's root faces backwards (its tail runs along +Z); the model points the way it flies.
            float length = key == "Arrow" ? 0.75f : 0.32f;
            model.localRotation = Quaternion.Euler(0f, 180f, 0f);
            model.localPosition = Vector3.forward * (length * 0.6f);
            BallLook.Sprite("Tip Glow", shot.transform, "FxGlow", 0.1f, color * 1.5f).Set(billboard: true, flicker: 0.2f)
                .transform.localPosition = Vector3.back * (length * 0.4f);
        }
    }
}
