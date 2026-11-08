using UnityEngine;

namespace Repulsor
{
    /// <summary>
    /// An enemy's body: one of the CC0 models built into Resources/Enemies (Quaternius mechs, robot and sci-fi kit),
    /// walking or idling, with glowing red eyes and a white flash when hit. Which model each enemy kind uses is picked
    /// in the Variant Lab; the last choice is the old placeholder shape.
    /// </summary>
    public sealed class EnemyLook : MonoBehaviour
    {
        public static readonly string[] RobotKeys = { "George", "Leela", "Mike", "Stan", "Classic", "QuadShell", null };
        public static readonly string[] RobotNames = { "A  GEORGE", "B  LEELA", "C  MIKE", "D  STAN", "E  CLASSIC", "F  SHELL", "G  PILL" };
        public static readonly string[] DroneKeys = { "EyeDrone", null };
        public static readonly string[] DroneNames = { "A  EYE DRONE", "B  DISC" };
        public const string RobotPickKey = "variant.robot";
        public const string DronePickKey = "variant.drone";
        static readonly Color EyeRed = new Color(1.5f, 0.12f, 0.06f);
        /// <summary>The robot used until one is picked: Stan, the mech with two big eyes.</summary>
        const int DefaultRobot = 3;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        Renderer[] renderers;
        Animation animation;
        MaterialPropertyBlock flashBlock;
        MaterialPropertyBlock plainBlock;
        bool flashing;
        string playing;
        float shootFor;

        public static int Picked(EnemyKind kind) =>
            Mathf.Clamp(kind == EnemyKind.Robot ? PlayerPrefs.GetInt(RobotPickKey, DefaultRobot) : PlayerPrefs.GetInt(DronePickKey, 0), 0,
                (kind == EnemyKind.Robot ? RobotKeys : DroneKeys).Length - 1);

        /// <summary>The model key for an enemy kind's picked look; null for the placeholder shape.</summary>
        public static string KeyFor(EnemyKind kind, int style) =>
            (kind == EnemyKind.Robot ? RobotKeys : DroneKeys)[style];

        /// <summary>Makes a model's body under <paramref name="parent"/>; null if the key is the placeholder or missing.</summary>
        public static EnemyLook Create(string key, Transform parent)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            var prefab = Resources.Load<GameObject>("Enemies/" + key);
            if (prefab == null)
            {
                Debug.LogWarning($"[Enemy] No model Enemies/{key}");
                return null;
            }
            GameObject go = Instantiate(prefab, parent, false);
            go.name = "Look " + key;
            var look = go.AddComponent<EnemyLook>();
            look.renderers = go.GetComponentsInChildren<Renderer>();
            look.animation = go.GetComponentInChildren<Animation>();
            look.flashBlock = new MaterialPropertyBlock();
            look.flashBlock.SetColor(BaseColorId, new Color(3f, 3f, 3f, 1f));
            look.plainBlock = new MaterialPropertyBlock();
            foreach (EnemyEye eye in go.GetComponentsInChildren<EnemyEye>())
            {
                float scale = Mathf.Max(1e-5f, eye.transform.lossyScale.x);
                FxSpin glow = BallLook.Sprite("Eye Glow", eye.transform, "FxGlow", eye.Size / scale, EyeRed).Set(billboard: true, flicker: 0.08f);
                glow.transform.localPosition = Vector3.zero;
            }
            return look;
        }

        /// <summary>Walks while moving, idles when stopped, and shows the hit flash.</summary>
        public void Tick(float dt, bool walking, float flash)
        {
            bool flashNow = flash > 0f;
            if (flashNow != flashing)
            {
                flashing = flashNow;
                foreach (Renderer renderer in renderers)
                    renderer.SetPropertyBlock(flashNow ? flashBlock : plainBlock);
            }
            if (animation == null)
                return;
            shootFor -= dt;
            if (shootFor > 0f)
                return;
            string clip = walking && animation.GetClip("walk") != null ? "walk" : animation.GetClip("idle") != null ? "idle" : "walk";
            AnimationState state = animation[clip];
            if (clip != playing && state != null)
            {
                playing = clip;
                state.wrapMode = WrapMode.Loop;
                animation.CrossFade(clip, 0.2f);
            }
        }

        /// <summary>Editor previews: poses the model partway through its walk.</summary>
        public void Pose(float seconds)
        {
            AnimationClip clip = animation != null ? animation.GetClip("walk") ?? animation.GetClip("idle") : null;
            if (clip != null)
                clip.SampleAnimation(animation.gameObject, seconds);
        }

        /// <summary>Plays the shooting move, if the model has one.</summary>
        public void Shoot()
        {
            if (animation == null || animation["shoot"] == null)
                return;
            animation["shoot"].wrapMode = WrapMode.Once;
            animation.CrossFade("shoot", 0.1f);
            shootFor = animation["shoot"].length * 0.9f;
            playing = "shoot";
        }
    }
}
