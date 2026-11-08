using System.IO;
using Repulsor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Renders the menus, HUD and shield looks to Logs/preview_*.png, to judge them without the headset. One eye,
/// no passthrough (a flat room colour instead), no live depth.
/// Batch: -executeMethod GamePreview.RenderAll (without -nographics)
/// </summary>
public static class GamePreview
{
    static readonly Color RoomColor = new Color(0.46f, 0.42f, 0.37f);

    [MenuItem("Repulsor/Preview Menus and Shields")]
    public static void RenderAll()
    {
        RenderScreens();
        RenderShields();
        RenderTinted();
    }

    /// <summary>The three energy ball looks side by side: crackle, glow shell, fire shell (left to right).</summary>
    public static void RenderBalls()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<Blast>("Assets/Prefabs/Blast.prefab");
        var yellow = new Color(1f, 0.75f, 0.1f);
        for (int i = 0; i < 3; i++)
        {
            Blast ball = Object.Instantiate(prefab);
            ball.Launch(null, new Vector3(-0.9f + i * 0.9f, 1.5f, 2.2f), Vector3.back, 1f, 3f, 0.6f);
            ball.ShowTail(0.01f);
            Tint.Apply(ball.gameObject, yellow);
            if (i > 0)
            {
                Bubble shell = Bubble.Create("Ball Shell", ball.Head);
                shell.transform.localScale = Vector3.one * 1.35f;
                shell.Show(yellow, i == 1 ? BubbleStyle.Energy : BubbleStyle.Flame, 0.9f);
            }
        }
        foreach (ElectricBall crackle in Object.FindObjectsByType<ElectricBall>())
            crackle.Crackle(0.1f);
        Save(new Vector3(0f, 1.5f, 0f), Quaternion.identity, 60f, "Logs/preview_energy_balls.png");
    }

    /// <summary>The smoke bomb's cloud in front of a stand-in person, as seven overlapping smoke spheres.</summary>
    public static void RenderSmoke()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        person.transform.position = new Vector3(0.4f, 0.87f, 3.2f);
        person.transform.localScale = new Vector3(0.5f, 0.87f, 0.3f);
        var random = new System.Random(3);
        for (int i = 0; i < 7; i++)
        {
            Bubble puff = Bubble.Create("Puff", null);
            Vector3 offset = i == 0 ? Vector3.zero : new Vector3((float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f, (float)random.NextDouble() - 0.5f) * 1.2f;
            puff.transform.position = new Vector3(0f, 0.9f, 2.4f) + offset;
            puff.transform.localScale = Vector3.one * 1.25f;
            puff.Show(new Color(0.55f, 0.56f, 0.6f), BubbleStyle.Smoke, 0.75f);
        }
        Save(new Vector3(0f, 1.5f, 0f), Quaternion.LookRotation(new Vector3(0f, -0.25f, 1f)), 70f, "Logs/preview_smoke.png");
    }

    /// <summary>A Ki-yellow energy ball and comet, to check player colours reach the energy shaders.</summary>
    public static void RenderTinted()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var prefab = AssetDatabase.LoadAssetAtPath<Blast>("Assets/Prefabs/Blast.prefab");
        Blast small = Object.Instantiate(prefab);
        small.Launch(null, new Vector3(-0.5f, 1.5f, 2f), Vector3.right, 1f);
        small.ShowTail(1.2f);
        Tint.Apply(small.gameObject, new Color(1f, 0.75f, 0.1f));
        Blast big = Object.Instantiate(prefab);
        big.Launch(null, new Vector3(0.6f, 1.3f, 2.5f), new Vector3(1f, 0f, -0.3f), 1f, 3.5f, 0.6f);
        big.ShowTail(1.5f);
        Tint.Apply(big.gameObject, new Color(0.25f, 0.6f, 1f));
        foreach (ElectricBall ball in Object.FindObjectsByType<ElectricBall>())
            ball.Crackle(0.1f);
        Save(new Vector3(0f, 1.5f, 0f), Quaternion.identity, 70f, "Logs/preview_tinted_blasts.png");
    }

    public static void RenderScreens()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var head = new GameObject("Head").transform;
        head.position = new Vector3(0f, 1.6f, 0f);
        var arena = new GameObject("Arena").AddComponent<Arena>();
        arena.InitPreview(head);

        foreach (string screen in new[] { "title", "modes", "characters", "pause", "debug", "results" })
        {
            arena.PreviewScreen(screen);
            Canvas.ForceUpdateCanvases();
            Vector3 menuCentre = head.position + Vector3.forward * 0.42f + Vector3.down * 0.2f;
            Save(head.position, Quaternion.LookRotation(menuCentre - head.position), 75f, $"Logs/preview_menu_{screen}.png");
        }

        // The HUD mid-fight: hurt, meter part full, shield half used.
        Object.DestroyImmediate(GameObject.Find("Menu"));
        Fighter player = arena.PreviewPlayer;
        player.DealtDamage(Fighter.MeterPerLevel);
        player.TryTransform();
        player.TakeDamage(70f);
        player.DealtDamage(Fighter.MeterPerLevel * 0.65f);
        player.ApplyPowerup(PowerupKind.DamageBoost);
        arena.PreviewHud.Visible = true;
        arena.PreviewHud.ShowFps = true;
        arena.PreviewHud.Message("WAVE 2", "of 5", 0f);
        for (int i = 0; i < 3; i++)
            arena.PreviewHud.Tick(player, CharacterDef.Get(CharacterId.Technoman), 0.016f);
        Canvas.ForceUpdateCanvases();
        Save(head.position, Quaternion.identity, 90f, "Logs/preview_hud.png");

        player.FillMeter();
        arena.PreviewHud.Message("", "", 0f);
        arena.PreviewHud.Tick(player, CharacterDef.Get(CharacterId.Technoman), 0.016f);
        Canvas.ForceUpdateCanvases();
        Save(head.position, Quaternion.identity, 90f, "Logs/preview_hud_ready.png");
    }

    public static void RenderShields()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var person = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        person.transform.position = new Vector3(0f, 0.87f, 2.2f);
        person.transform.localScale = new Vector3(0.5f, 0.87f, 0.3f);

        var colors = new[] { "#4FC3FF", "#FFD23F", "#3F8CFF", "#FF6A20", "#E02030" };
        var styles = new[] { BubbleStyle.Hex, BubbleStyle.Flame, BubbleStyle.Glass, BubbleStyle.Thin, BubbleStyle.Web };
        for (int i = 0; i < styles.Length; i++)
        {
            Bubble bubble = Bubble.Create("Shield", null);
            bubble.transform.position = new Vector3(0f, 1.25f, 2.2f);
            bubble.transform.localScale = Vector3.one * 1.24f;
            bubble.Show(Arena.Hex(colors[i]), styles[i], 1f);
            Save(new Vector3(0f, 1.5f, 0f), Quaternion.LookRotation(new Vector3(0f, 1.2f, 2.2f) - new Vector3(0f, 1.5f, 0f)), 60f,
                $"Logs/preview_shield_{i}_{styles[i]}.png");
            // From inside, as the player holding it sees it.
            bubble.transform.position = new Vector3(0f, 1.25f, 0f);
            Save(new Vector3(0f, 1.6f, 0f), Quaternion.LookRotation(new Vector3(0f, -0.25f, 1f)), 90f,
                $"Logs/preview_shield_{i}_{styles[i]}_inside.png");
            Object.DestroyImmediate(bubble.gameObject);
        }
    }

    static void Save(Vector3 position, Quaternion rotation, float fov, string outputPath)
    {
        var cameraObject = new GameObject("Preview Camera", typeof(Camera));
        cameraObject.transform.SetPositionAndRotation(position, rotation);
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = RoomColor;
        camera.nearClipPlane = 0.05f;
        camera.fieldOfView = fov;

        var texture = new RenderTexture(1280, 960, 24) { antiAliasing = 4 };
        camera.targetTexture = texture;
        camera.Render();
        RenderTexture.active = texture;
        var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        Object.DestroyImmediate(cameraObject);
        File.WriteAllBytes(outputPath, image.EncodeToPNG());
        Debug.Log($"[Preview] Wrote {outputPath}");
    }
}
