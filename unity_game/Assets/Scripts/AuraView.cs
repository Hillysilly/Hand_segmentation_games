using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// A burning aura standing at a spot on the floor, with rising embers and a low roar. It has several
    /// looks to try, each a child object, and one shows at a time. Suits count as further styles.
    /// </summary>
    public sealed class AuraView : MonoBehaviour
    {
        const float IgniteSeconds = 0.3f;
        const float ExtinguishSeconds = 0.5f;

        static readonly int GlobalPower = Shader.PropertyToID("_AuraPower");
        static readonly int Foot = Shader.PropertyToID("_AuraFoot");
        static readonly int Tint = Shader.PropertyToID("_AuraTint");
        const string ColorKey = "AuraColor";
        static readonly Color DefaultTint = new Color(1f, 0.38f, 0.03f);

        [SerializeField] GameObject[] styles;
        [SerializeField] ColorPicker picker;
        [SerializeField] ParticleSystem embers;
        [SerializeField] AudioSource roar;

        SuitView suits;
        float power;
        bool lit;
        int style;

        public bool IsLit => lit;
        public int StyleCount => styles.Length + (suits == null ? 0 : suits.Count);
        public int StyleNumber => style + 1;
        public string StyleName => IsSuit ? suits.NameOf(style - styles.Length) : styles[style].name;

        /// <summary>Whether the current style is a suit on the person rather than an effect around them.</summary>
        public bool IsSuit => style >= styles.Length;
        public ColorPicker Picker => picker;

        void Awake()
        {
            roar.clip = Sfx.Flame();
            picker.transform.SetParent(null);
            SetTint(LoadTint(), false);
            Apply(0f);
        }

        public void UseSuits(SuitView suitView) => suits = suitView;

        /// <summary>A style's own object (null for suits or out of range), to copy for the other player's aura.</summary>
        public GameObject StyleObject(int index) => index >= 0 && index < styles.Length ? styles[index] : null;

        /// <summary>Colours every style, and remembers the colour for next time if asked to.</summary>
        public void SetTint(Color tint, bool remember)
        {
            Shader.SetGlobalVector(Tint, tint);
            if (remember)
                PlayerPrefs.SetString(ColorKey, ColorUtility.ToHtmlStringRGB(tint));
        }

        static Color LoadTint() =>
            ColorUtility.TryParseHtmlString("#" + PlayerPrefs.GetString(ColorKey), out Color tint) ? tint : DefaultTint;

        /// <summary>Lights the aura standing on the floor at this point.</summary>
        public void Ignite(Vector3 point)
        {
            transform.position = new Vector3(point.x, 0f, point.z);
            lit = true;
            if (!roar.isPlaying && !IsSuit)
                roar.Play();
        }

        /// <summary>Moves a lit aura to stay on someone.</summary>
        public void MoveTo(Vector3 point) => transform.position = new Vector3(point.x, 0f, point.z);

        public void Extinguish()
        {
            lit = false;
            roar.Stop();
        }

        public void NextStyle() => ShowStyle((style + 1) % StyleCount);

        /// <summary>Switches look. Public so the editor preview and development test can pick one.</summary>
        public void ShowStyle(int index)
        {
            style = index;
            for (int i = 0; i < styles.Length; i++)
                styles[i].SetActive(i == style && (power > 0f || !Application.isPlaying));

            if (suits != null)
                suits.Show(IsSuit && lit ? style - styles.Length : -1);
            if (IsSuit)
                roar.Stop();
            else if (lit && !roar.isPlaying)
                roar.Play();
        }

        void Update()
        {
            float seconds = lit ? IgniteSeconds : ExtinguishSeconds;
            Apply(Mathf.MoveTowards(power, lit ? 1f : 0f, Time.deltaTime / seconds));
            Shader.SetGlobalVector(Foot, transform.position);
        }

        void Apply(float newPower)
        {
            bool wasVisible = power > 0f;
            power = newPower;
            bool visible = power > 0f;
            if (visible != wasVisible || !visible)
                ShowStyle(style);

            if (visible)
                Shader.SetGlobalFloat(GlobalPower, power);

            ParticleSystem.EmissionModule emission = embers.emission;
            emission.enabled = lit && !IsSuit;
        }
    }
}
