using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Repulsor
{
    /// <summary>
    /// SCAN ROOM (from the title): look around and walk about while the depth scan (<see cref="RoomMap"/>) maps the
    /// floor; the floor it has found shows as dots, and blocked spots as raised ones. Enemies then appear anywhere
    /// in the mapped space, as far from you as possible. The scan also runs through every match, and Space Setup's
    /// floor (when the headset has one) is used first.
    /// </summary>
    public sealed partial class Arena
    {
        bool scanning;
        bool bossFight;
        Text scanText;
        MeshFilter scanDots;
        float scanRedraw;
        readonly List<Vector3> scanPoints = new List<Vector3>();

        void ShowScan()
        {
            phase = Phase.Title;
            scanning = true;
            RectTransform s = OpenScreen(new Vector2(420f, 240f), "SCAN ROOM");
            Ui.Label(s, "Look slowly all around the room, floor included, and walk about a little.\n"
                + "Dots appear on the floor the game has found. Enemies will come from anywhere in it.",
                new Vector2(0f, 45f), new Vector2(390f, 60f), 13).color = Ui.Muted;
            scanText = Ui.Label(s, "", new Vector2(0f, -10f), new Vector2(390f, 24f), 16, TextAnchor.MiddleCenter, FontStyle.Bold);
            Ui.Button(s, "CLEAR", new Vector2(-95f, -80f), new Vector2(150f, 44f), () =>
            {
                roomMap.Clear();
                Log("Room map cleared");
            }, 16);
            Ui.Button(s, "DONE", new Vector2(95f, -80f), new Vector2(150f, 44f), () =>
            {
                Log($"Room scan done: {roomMap.ScannedArea:0.0} m2 of floor{(roomMap.HasFloorOutline ? ", Space Setup floor found too" : "")}");
                scanning = false;
                ShowScanDots(false);
                ShowTitle();
            }, 16);
            Log("Room scan started");
        }

        /// <summary>While the scan screen is up: the live count and the dots.</summary>
        void TickScan()
        {
            if (!scanning)
                return;
            if (phase != Phase.Title || !menu.gameObject.activeSelf)
            {
                scanning = false;
                ShowScanDots(false);
                return;
            }
            if (scanText != null)
                scanText.text = $"Floor found: {roomMap.ScannedArea:0.0} m²" + (roomMap.HasFloorOutline ? "   (Space Setup floor: yes)" : "");
            if (Time.unscaledTime < scanRedraw)
                return;
            scanRedraw = Time.unscaledTime + 0.5f;
            ShowScanDots(true);
        }

        void ShowScanDots(bool on)
        {
            if (scanDots == null)
            {
                if (!on)
                    return;
                var go = new GameObject("Scan Dots", typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(transform, false);
                scanDots = go.GetComponent<MeshFilter>();
                scanDots.sharedMesh = new Mesh { name = "Scan Dots" };
                var renderer = go.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = Resources.Load<Material>("Fx/FxGlow");
                var block = new MaterialPropertyBlock();
                block.SetColor("_Color", new Color(0.3f, 0.9f, 1f, 1f));
                renderer.SetPropertyBlock(block);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            scanDots.gameObject.SetActive(on);
            if (!on)
                return;
            roomMap.OpenPoints(scanPoints, FloorY);
            // A flat glowing square per open cell.
            int count = Mathf.Min(scanPoints.Count, 4000);
            var vertices = new Vector3[count * 4];
            var uvs = new Vector2[count * 4];
            var triangles = new int[count * 6];
            const float half = 0.06f;
            for (int i = 0; i < count; i++)
            {
                Vector3 p = scanPoints[i];
                vertices[i * 4] = p + new Vector3(-half, 0f, -half);
                vertices[i * 4 + 1] = p + new Vector3(-half, 0f, half);
                vertices[i * 4 + 2] = p + new Vector3(half, 0f, half);
                vertices[i * 4 + 3] = p + new Vector3(half, 0f, -half);
                uvs[i * 4] = new Vector2(0f, 0f);
                uvs[i * 4 + 1] = new Vector2(0f, 1f);
                uvs[i * 4 + 2] = new Vector2(1f, 1f);
                uvs[i * 4 + 3] = new Vector2(1f, 0f);
                triangles[i * 6] = i * 4;
                triangles[i * 6 + 1] = i * 4 + 1;
                triangles[i * 6 + 2] = i * 4 + 2;
                triangles[i * 6 + 3] = i * 4;
                triangles[i * 6 + 4] = i * 4 + 2;
                triangles[i * 6 + 5] = i * 4 + 3;
            }
            Mesh mesh = scanDots.sharedMesh;
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
        }
    }
}
