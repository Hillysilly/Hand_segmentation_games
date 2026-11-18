using System.Collections.Generic;
using System.IO;
using Repulsor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Graphics for the competition video (11/17), in the game's own look: the opening/closing logo sting (a robot
/// walks out of the dark, a lightsaber slice cuts it in two, the logo slams in), the full-screen transition cards
/// and the captions (bottom lines, and character names at the top). PNG frame sequences at 1920x1080, 30 fps,
/// under ../videos/edit (captions with a clear background, to lay over the gameplay).
/// Batch (with graphics): -executeMethod TrailerAssets.RenderAll
/// </summary>
public static class TrailerAssets
{
    const int W = 1920, H = 1080, Fps = 30;
    static readonly Color Navy = new Color(0.02f, 0.04f, 0.09f, 1f);
    static string Out => Path.GetFullPath("../videos/edit");

    public static readonly (string slug, string text)[] Cards =
    {
        ("fight_robots", "FIGHT ROBOTS"),
        ("level_up", "LEVEL UP"),
        ("get_cool_auras", "GET COOL AURAS"),
        ("team_up", "TEAM UP WITH FRIENDS"),
        ("crazy_bosses", "TO FIGHT CRAZY BOSSES"),
        ("fight_friends", "OR FIGHT YOUR FRIENDS"),
    };

    public static readonly (string slug, string text)[] BottomCaptions =
    {
        ("unique_characters", "8 UNIQUE CHARACTERS"),
        ("hand_controlled", "FULLY HAND CONTROLLED"),
        ("indoors_outdoors", "FIGHT ROBOTS INDOORS OR OUTDOORS"),
    };

    public static void RenderIntroAndCards()
    {
        RenderCards();
        RenderIntro();
    }

    public static void RenderAll()
    {
        RenderCards();
        RenderCaptions();
        RenderIntro();
    }

    // ------------------------------------------------------------------ shared

    static Camera MakeCamera(bool orthographic, Color clear)
    {
        var camera = new GameObject("Trailer Camera", typeof(Camera)).GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = clear;
        camera.orthographic = orthographic;
        camera.orthographicSize = H * Ui.MetresPerUnit * 0.5f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 50f;
        camera.targetTexture = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        return camera;
    }

    static Color32[] Grab(Camera camera)
    {
        Canvas.ForceUpdateCanvases();
        camera.Render();
        RenderTexture.active = camera.targetTexture;
        var image = new Texture2D(W, H, TextureFormat.RGBA32, false);
        image.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        Color32[] pixels = image.GetPixels32();
        Object.DestroyImmediate(image);
        return pixels;
    }

    static void Save(Color32[] pixels, string folder, int frame)
    {
        Directory.CreateDirectory(folder);
        var image = new Texture2D(W, H, TextureFormat.RGBA32, false);
        image.SetPixels32(pixels);
        image.Apply();
        File.WriteAllBytes(Path.Combine(folder, $"{frame:000}.png"), image.EncodeToPNG());
        Object.DestroyImmediate(image);
    }

    /// <summary>A full-frame canvas 1 m in front of a camera at the origin (1 canvas unit = 1 output pixel).</summary>
    static RectTransform FrameCanvas()
    {
        RectTransform canvas = Ui.Canvas("Trailer Canvas", null, new Vector2(W, H));
        canvas.position = new Vector3(0f, 0f, 1f);
        return canvas;
    }

    /// <summary>The slam: oversized and see-through, snapping down past full size, settling, then a slow drift up.</summary>
    static float SlamScale(float t) =>
        t < 0.12f ? Mathf.Lerp(1.7f, 0.94f, Mathf.SmoothStep(0f, 1f, t / 0.12f))
        : t < 0.22f ? Mathf.Lerp(0.94f, 1f, (t - 0.12f) / 0.1f)
        : 1f + 0.03f * (t - 0.22f);

    static float SlamFlash(float t) => t < 0.1f ? 0f : Mathf.Clamp01(1f - (t - 0.1f) / 0.3f);

    static Vector2 Shake(float t, int seed)
    {
        float strength = t < 0.1f ? 0f : Mathf.Clamp01(1f - (t - 0.1f) / 0.3f) * 14f;
        return new Vector2(Mathf.Sin(t * 91f + seed) * strength, Mathf.Cos(t * 73f + seed * 2f) * strength);
    }

    // ------------------------------------------------------------------ transition cards

    /// <summary>
    /// "Full transition" cards: the game's dark navy with its cyan-edged panel (the select screen's look), the line in
    /// big cyan capitals. It slams in with a flash and a shake, then holds. 1.6 s each.
    /// </summary>
    public static void RenderCards()
    {
        foreach ((string slug, string text) in Cards)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = MakeCamera(true, Navy);
            RectTransform canvas = FrameCanvas();

            // Background: faint grid lines, like the select screen's floor.
            for (int x = -W / 2; x <= W / 2; x += 120)
                Ui.Box(canvas, new Vector2(x, 0f), new Vector2(2f, H), new Color(0.2f, 0.45f, 0.7f, 0.08f), false);
            for (int y = -H / 2; y <= H / 2; y += 120)
                Ui.Box(canvas, new Vector2(0f, y), new Vector2(W, 2f), new Color(0.2f, 0.45f, 0.7f, 0.08f), false);

            RectTransform group = Ui.Rect("Card", canvas, Vector2.zero, new Vector2(W, H));
            Text label = Ui.Label(group, text, Vector2.zero, new Vector2(1800f, 240f), 170, TextAnchor.MiddleCenter, FontStyle.Bold);
            label.color = Ui.Accent;
            Canvas.ForceUpdateCanvases();
            float width = Mathf.Min(1840f, label.preferredWidth + 140f);
            Image edge = Ui.Box(group, Vector2.zero, new Vector2(width + 14f, 314f), Ui.PanelEdge);
            Image panel = Ui.Box(group, Vector2.zero, new Vector2(width, 300f), new Color(0.03f, 0.06f, 0.12f, 1f));
            Image bar = Ui.Box(group, new Vector2(0f, -112f), new Vector2(width * 0.5f, 10f), Ui.Accent, false);
            Text small = Ui.Label(group, "TIN CAN TAKEDOWN", new Vector2(0f, 196f), new Vector2(1000f, 56f), 40, TextAnchor.MiddleCenter, FontStyle.Bold);
            small.color = Ui.Muted;
            edge.transform.SetAsFirstSibling();
            panel.transform.SetSiblingIndex(1);
            Image flash = Ui.Box(canvas, Vector2.zero, new Vector2(W, H), new Color(1f, 1f, 1f, 0f), false);

            int frames = Mathf.RoundToInt(1.6f * Fps);
            for (int f = 0; f < frames; f++)
            {
                float t = f / (float)Fps;
                float scale = SlamScale(t);
                group.localScale = Vector3.one * scale;
                group.anchoredPosition = Shake(t, 3);
                float appear = Mathf.Clamp01(t / 0.08f);
                label.color = new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, appear);
                small.color = new Color(Ui.Muted.r, Ui.Muted.g, Ui.Muted.b, Mathf.Clamp01((t - 0.25f) / 0.2f));
                edge.color = new Color(Ui.PanelEdge.r, Ui.PanelEdge.g, Ui.PanelEdge.b, appear);
                panel.color = Color.Lerp(new Color(0.03f, 0.06f, 0.12f, appear), new Color(0.5f, 0.85f, 1f, appear), SlamFlash(t) * 0.6f);
                bar.rectTransform.sizeDelta = new Vector2(width * Mathf.Lerp(0f, 0.6f, Mathf.Clamp01((t - 0.15f) / 0.25f)), 10f);
                flash.color = new Color(1f, 1f, 1f, SlamFlash(t) * 0.45f);
                Color32[] pixels = Grab(camera);
                CardLightning(pixels, t, f, new Vector2(W * 0.5f, H * 0.5f) + Shake(t, 3), new Vector2(width + 14f, 314f) * scale);
                Save(pixels, Path.Combine(Out, "cards", slug), f);
            }
            Debug.Log($"[Trailer] Card {slug}: {frames} frames");
        }
    }

    // ------------------------------------------------------------------ captions

    /// <summary>
    /// Captions over the gameplay, on a clear background: bottom lines in a cyan-edged bar, and each character's name
    /// at the top in its colour with its select-screen icon. They slam in (0.5 s); the last frame holds.
    /// </summary>
    public static void RenderCaptions()
    {
        var jobs = new List<(string slug, string text, Color color, Sprite icon, bool top)>();
        foreach ((string slug, string text) in BottomCaptions)
            jobs.Add((slug, text, Color.white, null, false));
        foreach (CharacterDef def in CharacterDef.All)
            jobs.Add(("name_" + def.Name.ToLowerInvariant().Replace(' ', '_'), def.Name.ToUpperInvariant(),
                Arena.Hex(def.Color), Resources.Load<Sprite>("Icons/" + def.Id), true));

        foreach (var job in jobs)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Camera camera = MakeCamera(true, new Color(0f, 0f, 0f, 0f));
            RectTransform canvas = FrameCanvas();
            float y = job.top ? H * 0.5f - 120f : -H * 0.5f + 130f;
            RectTransform group = Ui.Rect("Caption", canvas, new Vector2(0f, y), new Vector2(W, 160f));
            int size = job.top ? 92 : 64;
            Text label = Ui.Label(group, job.text, Vector2.zero, new Vector2(1700f, 120f), size, TextAnchor.MiddleCenter, FontStyle.Bold);
            Canvas.ForceUpdateCanvases();
            float iconSize = job.icon != null ? 96f : 0f;
            float width = label.preferredWidth + 90f + (iconSize > 0f ? iconSize + 24f : 0f);
            float height = job.top ? 140f : 110f;
            Color edgeColor = job.top ? job.color : Ui.PanelEdge;
            Image edge = Ui.Box(group, Vector2.zero, new Vector2(width + 10f, height + 10f), edgeColor);
            Image panel = Ui.Box(group, Vector2.zero, new Vector2(width, height), new Color(0.03f, 0.06f, 0.12f, 0.92f));
            edge.transform.SetAsFirstSibling();
            panel.transform.SetSiblingIndex(1);
            Image icon = null;
            if (job.icon != null)
            {
                label.rectTransform.anchoredPosition = new Vector2((iconSize + 24f) * 0.5f, 0f);
                icon = Ui.Box(group, new Vector2(-width * 0.5f + 45f + iconSize * 0.5f, 0f), new Vector2(iconSize, iconSize), job.color, false);
                icon.sprite = job.icon;
                icon.preserveAspect = true;
            }

            int frames = Mathf.RoundToInt(0.5f * Fps);
            for (int f = 0; f <= frames; f++)
            {
                float t = f / (float)Fps;
                float scale = t < 0.1f ? Mathf.Lerp(1.6f, 0.95f, t / 0.1f) : t < 0.18f ? Mathf.Lerp(0.95f, 1f, (t - 0.1f) / 0.08f) : 1f;
                float appear = Mathf.Clamp01(t / 0.06f);
                float flashAmount = t < 0.08f ? 0f : Mathf.Clamp01(1f - (t - 0.08f) / 0.25f);
                group.localScale = Vector3.one * scale;
                label.color = new Color(job.color.r, job.color.g, job.color.b, appear);
                edge.color = new Color(edgeColor.r, edgeColor.g, edgeColor.b, appear);
                panel.color = Color.Lerp(new Color(0.03f, 0.06f, 0.12f, 0.92f * appear), new Color(1f, 1f, 1f, appear), flashAmount * 0.55f);
                if (icon != null)
                    icon.color = new Color(job.color.r, job.color.g, job.color.b, appear);
                Save(Grab(camera), Path.Combine(Out, "captions", job.slug), f);
            }
            Debug.Log($"[Trailer] Caption {job.slug}");
        }
    }

    // ------------------------------------------------------------------ the logo sting

    /// <summary>
    /// 4.5 s: dark; a gunner robot walks out of the dark toward you, red eyes first; a cyan lightsaber slash cuts across
    /// it with a flash and sparks, and the two halves slide apart and fade; the logo slams in and holds, slowly growing.
    /// The robot is rendered by the game (its model, walk and eye glows); the slice, sparks and flashes are drawn here.
    /// </summary>
    public static void RenderIntro()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientLight = new Color(0.16f, 0.17f, 0.22f);
        var key = new GameObject("Key", typeof(Light)).GetComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 1.1f;
        key.transform.rotation = Quaternion.Euler(35f, 150f, 0f);
        var rim = new GameObject("Rim", typeof(Light)).GetComponent<Light>();
        rim.type = LightType.Directional;
        rim.color = new Color(0.4f, 0.8f, 1f);
        rim.intensity = 1.6f;
        rim.transform.rotation = Quaternion.Euler(20f, -20f, 0f);

        var holder = new GameObject("Robot").transform;
        // The round drone with the one red eye: the robot on the logo.
        EnemyLook look = EnemyLook.Create("EyeDrone", holder);
        holder.rotation = Quaternion.Euler(0f, 180f, 0f);
        look.Pose(0f);
        var bounds = new Bounds(holder.position, Vector3.zero);
        foreach (Renderer part in holder.GetComponentsInChildren<Renderer>())
            if (!(part.GetComponent<FxSpin>()))
                bounds.Encapsulate(part.bounds);
        Vector3 middle = bounds.center - holder.position;
        float tall = bounds.size.y;

        Camera robotCamera = MakeCamera(false, new Color(0f, 0f, 0f, 1f));
        robotCamera.fieldOfView = 30f;

        // The logo layer: its own camera and canvas, clear background, far off to the side so neither sees the other.
        Camera logoCamera = MakeCamera(true, new Color(0f, 0f, 0f, 0f));
        logoCamera.transform.position = new Vector3(100f, 0f, 0f);
        RectTransform canvas = FrameCanvas();
        canvas.position = new Vector3(100f, 0f, 1f);
        Image logo = Ui.Box(canvas, Vector2.zero, new Vector2(1000f, 800f), Ui.Accent, false);
        logo.sprite = Resources.Load<Sprite>("Logo");
        logo.preserveAspect = true;
        Image glow = Ui.Box(canvas, Vector2.zero, new Vector2(1080f, 864f), new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, 0f), false);
        glow.sprite = logo.sprite;
        glow.preserveAspect = true;
        glow.transform.SetAsFirstSibling();

        // The slice: from upper right to lower left, through the robot's chest (found when it lands).
        var slashFrom = new Vector2(W * 0.5f, H * 0.5f);
        bool slashPlaced = false;
        Vector2 along = new Vector2(-1f, -0.42f).normalized;
        var normal = new Vector2(-along.y, along.x);
        var sparks = new List<(Vector2 p, Vector2 v, float life)>();
        var rng = new System.Random(7);
        for (int i = 0; i < 160; i++)
        {
            float a = (float)(rng.NextDouble() * 2.0 - 1.0) * 420f;
            Vector2 dir = (normal * (rng.NextDouble() < 0.5 ? 1f : -1f) * (float)(0.4 + rng.NextDouble()) + along * (float)(rng.NextDouble() - 0.5)).normalized;
            sparks.Add((along * a, dir * (float)(250 + rng.NextDouble() * 650), (float)(0.25 + rng.NextDouble() * 0.45)));
        }

        const float Total = 4.5f, WalkStart = 0.3f, SlashAt = 1.45f, SlashTime = 0.12f, LogoAt = 2.0f;
        int frames = Mathf.RoundToInt(Total * Fps);
        var output = new Color32[W * H];
        string folder = Path.Combine(Out, "intro");
        for (int f = 0; f < frames; f++)
        {
            float t = f / (float)Fps;

            // The robot walks toward you out of the dark; the camera eases in a little.
            float walk = Mathf.Clamp01((t - WalkStart) / (SlashAt - WalkStart));
            float easedWalk = 1f - (1f - walk) * (1f - walk);
            holder.position = new Vector3(0f, 0.06f * Mathf.Sin(t * 4.2f), Mathf.Lerp(2.6f, 0f, easedWalk));
            holder.rotation = Quaternion.Euler(4f * Mathf.Sin(t * 2.3f), 180f + 6f * Mathf.Sin(t * 1.7f), 3f * Mathf.Sin(t * 3.1f));
            look.Pose(t * 0.9f);
            Vector3 aimAt = holder.position + middle + Vector3.up * tall * 0.05f;
            robotCamera.transform.position = aimAt + new Vector3(0f, 0.05f, -Mathf.Lerp(4.4f, 3.1f, easedWalk) * tall / 1.3f);
            robotCamera.transform.rotation = Quaternion.LookRotation(aimAt - robotCamera.transform.position);
            if (!slashPlaced && t >= SlashAt)
            {
                Vector3 chest = robotCamera.WorldToScreenPoint(holder.position + middle);
                slashFrom = new Vector2(chest.x, chest.y);
                slashPlaced = true;
            }
            foreach (FxSpin spin in holder.GetComponentsInChildren<FxSpin>())
                spin.transform.rotation = Quaternion.LookRotation(spin.transform.position - robotCamera.transform.position);
            Color32[] robot = t < SlashAt + 1f ? Grab(robotCamera) : null;

            float reveal = Mathf.Clamp01((t - WalkStart) / 0.6f);
            float cut = Mathf.Clamp01((t - SlashAt - SlashTime) / 0.55f);       // 0 at the cut, 1 when the halves are gone
            float apart = Mathf.Pow(cut, 1.4f) * 300f;
            float pieceFade = 1f - cut;
            Vector2 offsetUpper = normal * apart + along * apart * 0.35f;
            Vector2 offsetLower = -normal * apart * 0.6f - along * apart * 0.25f;
            float sweep = Mathf.Clamp01((t - SlashAt) / SlashTime);
            float streakFade = t < SlashAt + SlashTime ? 1f : Mathf.Clamp01(1f - (t - SlashAt - SlashTime) / 0.45f);
            bool slashing = t >= SlashAt && streakFade > 0f;
            float flash = Mathf.Max(Pulse(t, SlashAt + SlashTime, 0.18f) * 0.85f, Pulse(t, LogoAt + 0.08f, 0.2f) * 0.7f);
            float headAlong = Mathf.Lerp(-1300f, 1300f, sweep);

            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float r = 0f, g = 0f, b = 0f;
                    if (robot != null && pieceFade > 0f)
                    {
                        var p = new Vector2(x, y);
                        bool beforeCut = t < SlashAt + SlashTime;
                        Color32 c = default;
                        bool hit = false;
                        if (beforeCut)
                        {
                            c = robot[y * W + x];
                            hit = true;
                        }
                        else
                        {
                            Vector2 q = p - offsetUpper;
                            if (Vector2.Dot(q - slashFrom, normal) > 0f && Inside(q))
                            {
                                c = robot[(int)q.y * W + (int)q.x];
                                hit = true;
                            }
                            else
                            {
                                q = p - offsetLower;
                                if (Vector2.Dot(q - slashFrom, normal) <= 0f && Inside(q))
                                {
                                    c = robot[(int)q.y * W + (int)q.x];
                                    hit = true;
                                }
                            }
                        }
                        if (hit)
                        {
                            float k = reveal * pieceFade / 255f;
                            r = c.r * k;
                            g = c.g * k;
                            b = c.b * k;
                        }
                    }
                    if (slashing)
                    {
                        Vector2 d = new Vector2(x, y) - slashFrom;
                        float a = Vector2.Dot(d, along);
                        if (a < headAlong)
                        {
                            float n = Vector2.Dot(d, normal);
                            float core = Mathf.Exp(-(n * n) / 30f);
                            float halo = Mathf.Exp(-(n * n) / 1600f) * 0.55f;
                            float tip = Mathf.Clamp01(1f - (headAlong - a) / 2600f);
                            float s = (core + halo) * streakFade * (0.5f + 0.5f * tip);
                            r += s * Mathf.Lerp(0.47f, 1f, core);
                            g += s * Mathf.Lerp(0.88f, 1f, core);
                            b += s;
                        }
                    }
                    if (flash > 0f)
                    {
                        r += flash * 0.9f;
                        g += flash * 0.95f;
                        b += flash;
                    }
                    output[y * W + x] = new Color32((byte)(Mathf.Clamp01(r) * 255f), (byte)(Mathf.Clamp01(g) * 255f),
                        (byte)(Mathf.Clamp01(b) * 255f), 255);
                }

            // Sparks thrown off the cut.
            float sparkAge = t - SlashAt - SlashTime * 0.5f;
            if (sparkAge > 0f)
                foreach (var spark in sparks)
                {
                    if (sparkAge > spark.life)
                        continue;
                    float life = 1f - sparkAge / spark.life;
                    Vector2 at = slashFrom + spark.p + spark.v * sparkAge + new Vector2(0f, -400f) * sparkAge * sparkAge;
                    Dot(output, at, 3, new Color(1f, 0.9f, 0.6f) * life + new Color(0.47f, 0.88f, 1f) * (1f - life) * 0.6f);
                }

            // The logo slams in and holds, growing slowly.
            if (t >= LogoAt)
            {
                float lt = t - LogoAt;
                float scale = SlamScale(lt) * (1f + 0.01f * lt);
                logo.rectTransform.localScale = Vector3.one * scale;
                glow.rectTransform.localScale = Vector3.one * scale * (1.02f + 0.04f * Mathf.Sin(lt * 3f));
                float appear = Mathf.Clamp01(lt / 0.08f);
                logo.color = new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, appear);
                glow.color = new Color(Ui.Accent.r, Ui.Accent.g, Ui.Accent.b, appear * 0.18f);
                Color32[] layer = Grab(logoCamera);
                for (int i = 0; i < output.Length; i++)
                {
                    Color32 l = layer[i];
                    if (l.a == 0)
                        continue;
                    float a = l.a / 255f;
                    Color32 o = output[i];
                    output[i] = new Color32((byte)Mathf.Min(255f, o.r * (1f - a) + l.r * a + o.r * 0f), (byte)Mathf.Min(255f, o.g * (1f - a) + l.g * a),
                        (byte)Mathf.Min(255f, o.b * (1f - a) + l.b * a), 255);
                }
            }
            if (t >= LogoAt)
                LogoLightning(output, t - LogoAt, f);
            Save(output, folder, f);
        }
        Debug.Log($"[Trailer] Intro: {frames} frames");
    }

    /// <summary>Front views of the round robots, to pick the one that matches the logo: Logs/trailer_robots.png.</summary>
    public static void RenderLineup()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        RenderSettings.ambientLight = new Color(0.5f, 0.5f, 0.55f);
        var key = new GameObject("Key", typeof(Light)).GetComponent<Light>();
        key.type = LightType.Directional;
        key.transform.rotation = Quaternion.Euler(30f, 160f, 0f);
        string[] models = { "EyeDrone", "QuadShell", "Mike", "Classic", "Trilobite" };
        for (int i = 0; i < models.Length; i++)
        {
            var holder = new GameObject(models[i]).transform;
            EnemyLook look = EnemyLook.Create(models[i], holder);
            holder.rotation = Quaternion.Euler(0f, 180f, 0f);
            look?.Pose(0f);
            var bounds = new Bounds(holder.position, Vector3.zero);
            foreach (Renderer part in holder.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(part.bounds);
            float scale = 1.2f / Mathf.Max(0.1f, Mathf.Max(bounds.size.x, bounds.size.y));
            holder.localScale = Vector3.one * scale;
            holder.position = new Vector3((i - 2) * 1.5f, -bounds.center.y * scale, 0f);
        }
        Camera camera = MakeCamera(false, new Color(0.3f, 0.29f, 0.27f, 1f));
        camera.fieldOfView = 40f;
        camera.transform.position = new Vector3(0f, 0f, -6.5f);
        Color32[] pixels = Grab(camera);
        var image = new Texture2D(W, H, TextureFormat.RGBA32, false);
        image.SetPixels32(pixels);
        image.Apply();
        File.WriteAllBytes("Logs/trailer_robots.png", image.EncodeToPNG());
    }

    // ------------------------------------------------------------------ lightning

    /// <summary>
    /// The card's slam throws lightning: bolts from the panel's edge out toward the screen's, re-forked every other
    /// frame, for half a second; then a couple of short crackles along the panel. A white core in a cyan glow.
    /// </summary>
    static void CardLightning(Color32[] pixels, float t, int frame, Vector2 centre, Vector2 panel)
    {
        var rng = new System.Random(frame / 2 * 7919 + 13);
        if (t >= 0.08f && t < 0.6f)
        {
            float strength = 1f - (t - 0.08f) / 0.52f;
            for (int i = 0; i < 6; i++)
            {
                Vector2 from = EdgePoint(centre, panel, rng);
                Vector2 outward = (from - centre).normalized;
                Vector2 jitter = new Vector2((float)rng.NextDouble() - 0.5f, (float)rng.NextDouble() - 0.5f) * 0.8f;
                Vector2 to = from + (outward + jitter).normalized * (float)(250 + rng.NextDouble() * 550) * Mathf.Lerp(0.6f, 1f, strength);
                Bolt(pixels, from, to, rng, strength, true);
            }
        }
        foreach (float at in new[] { 0.95f, 1.32f })
            if (t >= at && t < at + 0.07f)
                for (int i = 0; i < 2; i++)
                {
                    Vector2 from = EdgePoint(centre, panel, rng);
                    Bolt(pixels, from, EdgePoint(centre, panel, rng) * 0.3f + from * 0.7f, rng, 0.8f, false);
                }
    }

    /// <summary>The logo's slam throws bolts outward for half a second; then small arcs crackle round it now and then.</summary>
    static void LogoLightning(Color32[] pixels, float t, int frame)
    {
        var rng = new System.Random(frame / 2 * 104729 + 5);
        var centre = new Vector2(W * 0.5f, H * 0.5f + 60f);
        if (t >= 0.06f && t < 0.6f)
        {
            float strength = 1f - (t - 0.06f) / 0.54f;
            for (int i = 0; i < 7; i++)
            {
                float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                // From outside the logo (text included), outward, so no bolt crosses the name.
                Vector2 from = centre + new Vector2(dir.x * 560f, dir.y * 430f);
                Bolt(pixels, from, from + dir * (float)(200 + rng.NextDouble() * 500), rng, strength, true);
            }
        }
        // Every 0.7 s or so, an arc jumps across the edge of the logo for a few frames.
        float cycle = (t + 0.3f) % 0.7f;
        if (t > 0.7f && cycle < 0.1f)
        {
            float angle = (float)(rng.NextDouble() * Mathf.PI * 2.0);
            float span = (float)(0.4 + rng.NextDouble() * 0.6);
            Vector2 a = centre + new Vector2(Mathf.Cos(angle) * 360f, Mathf.Sin(angle) * 290f);
            Vector2 b = centre + new Vector2(Mathf.Cos(angle + span) * 360f, Mathf.Sin(angle + span) * 290f);
            Bolt(pixels, a, b, rng, 0.75f, false);
        }
    }

    static Vector2 EdgePoint(Vector2 centre, Vector2 size, System.Random rng)
    {
        float u = (float)rng.NextDouble();
        Vector2 half = size * 0.5f;
        switch (rng.Next(4))
        {
            case 0: return centre + new Vector2(Mathf.Lerp(-half.x, half.x, u), half.y);
            case 1: return centre + new Vector2(Mathf.Lerp(-half.x, half.x, u), -half.y);
            case 2: return centre + new Vector2(-half.x, Mathf.Lerp(-half.y, half.y, u));
            default: return centre + new Vector2(half.x, Mathf.Lerp(-half.y, half.y, u));
        }
    }

    /// <summary>A jagged bolt from a to b (midpoint displacement), with a fork or two; added onto the frame.</summary>
    static void Bolt(Color32[] pixels, Vector2 a, Vector2 b, System.Random rng, float strength, bool forks)
    {
        var points = new List<Vector2> { a, b };
        for (int pass = 0; pass < 6; pass++)
        {
            var next = new List<Vector2>(points.Count * 2);
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 p = points[i], q = points[i + 1];
                Vector2 d = q - p;
                var n = new Vector2(-d.y, d.x);
                next.Add(p);
                next.Add((p + q) * 0.5f + n * (float)(rng.NextDouble() - 0.5) * 0.45f);
            }
            next.Add(points[points.Count - 1]);
            points = next;
        }
        for (int i = 0; i < points.Count - 1; i++)
            Stroke(pixels, points[i], points[i + 1], strength);
        if (!forks)
            return;
        for (int k = 0; k < 2; k++)
        {
            Vector2 start = points[rng.Next(points.Count / 4, points.Count * 3 / 4)];
            Vector2 dir = (Vector2)(Quaternion.Euler(0f, 0f, (float)(rng.NextDouble() * 80 - 40)) * (Vector3)(b - a));
            Bolt(pixels, start, start + dir * (float)(0.25 + rng.NextDouble() * 0.25), rng, strength * 0.6f, false);
        }
    }

    static void Stroke(Color32[] pixels, Vector2 a, Vector2 b, float strength)
    {
        float length = Vector2.Distance(a, b);
        int steps = Mathf.Max(1, Mathf.CeilToInt(length / 1.5f));
        for (int s = 0; s <= steps; s++)
            Glow(pixels, Vector2.Lerp(a, b, s / (float)steps), strength);
    }

    static void Glow(Color32[] pixels, Vector2 at, float strength)
    {
        const int Radius = 11;
        int cx = Mathf.RoundToInt(at.x), cy = Mathf.RoundToInt(at.y);
        for (int y = cy - Radius; y <= cy + Radius; y++)
        {
            if (y < 0 || y >= H)
                continue;
            for (int x = cx - Radius; x <= cx + Radius; x++)
            {
                if (x < 0 || x >= W)
                    continue;
                float d2 = (x - at.x) * (x - at.x) + (y - at.y) * (y - at.y);
                float core = Mathf.Exp(-d2 / 3.2f) * 0.6f;
                float halo = Mathf.Exp(-d2 / 45f) * 0.11f;
                float k = strength * 255f;
                Color32 o = pixels[y * W + x];
                pixels[y * W + x] = new Color32(
                    (byte)Mathf.Min(255f, o.r + (core * 0.85f + halo * 0.3f) * k),
                    (byte)Mathf.Min(255f, o.g + (core * 0.95f + halo * 0.8f) * k),
                    (byte)Mathf.Min(255f, o.b + (core + halo) * k), o.a);
            }
        }
    }

    static float Pulse(float t, float at, float length) => t < at ? 0f : Mathf.Clamp01(1f - (t - at) / length);

    static bool Inside(Vector2 q) => q.x >= 0f && q.y >= 0f && q.x < W - 1 && q.y < H - 1;

    static void Dot(Color32[] pixels, Vector2 at, int radius, Color color)
    {
        int cx = Mathf.RoundToInt(at.x), cy = Mathf.RoundToInt(at.y);
        for (int y = cy - radius; y <= cy + radius; y++)
            for (int x = cx - radius; x <= cx + radius; x++)
            {
                if (x < 0 || y < 0 || x >= W || y >= H)
                    continue;
                float fall = 1f - Mathf.Clamp01(new Vector2(x - at.x, y - at.y).magnitude / (radius + 0.5f));
                Color32 o = pixels[y * W + x];
                pixels[y * W + x] = new Color32((byte)Mathf.Min(255f, o.r + color.r * fall * 255f), (byte)Mathf.Min(255f, o.g + color.g * fall * 255f),
                    (byte)Mathf.Min(255f, o.b + color.b * fall * 255f), 255);
            }
    }
}
