using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Repulsor
{
    /// <summary>
    /// What the game knows about the room's floor, for picking where enemies appear. Two sources, best first:
    /// 1. Space Setup's floor outline (AR planes), minus furniture boxes: exact, when the headset has a room set up.
    /// 2. A depth scan: each frame a few depth rays from your head mark the floor cells they cross as open, and cells
    ///    where they hit something standing (a wall, a sofa) as blocked. It runs all through a match and on the
    ///    SCAN ROOM screen, so the map fills in as you look around. It needs no Space Setup.
    /// Spawn points are open floor with room around them, as far from the players as possible.
    /// Lines tagged [Room] go to the headset log.
    /// </summary>
    public sealed class RoomMap
    {
        public const float Cell = 0.25f;
        const int Size = 72;               // 18 m across, centred on where the game started
        const int RaysPerFrame = 6;
        const float MaxRay = 7f;

        readonly byte[] open = new byte[Size * Size];
        readonly byte[] blocked = new byte[Size * Size];
        readonly List<Vector2> floorOutline = new List<Vector2>();
        readonly List<Vector3> candidates = new List<Vector3>();
        int nextRay;
        int openCells;
        float outlineCheck;
        float floorOutlineY;

        public int OpenCells => openCells;
        /// <summary>Square metres of floor the scan has seen open.</summary>
        public float ScannedArea => openCells * Cell * Cell;
        /// <summary>Space Setup gave a floor outline.</summary>
        public bool HasFloorOutline => floorOutline.Count >= 3;

        static int Index(int x, int z) => z * Size + x;

        static bool ToCell(Vector3 point, out int x, out int z)
        {
            x = Mathf.FloorToInt(point.x / Cell) + Size / 2;
            z = Mathf.FloorToInt(point.z / Cell) + Size / 2;
            return x >= 0 && z >= 0 && x < Size && z < Size;
        }

        static Vector3 CellCentre(int x, int z, float floorY) =>
            new Vector3((x - Size / 2 + 0.5f) * Cell, floorY, (z - Size / 2 + 0.5f) * Cell);

        public void Clear()
        {
            System.Array.Clear(open, 0, open.Length);
            System.Array.Clear(blocked, 0, blocked.Length);
            openCells = 0;
        }

        /// <summary>
        /// Casts this frame's few depth rays from the head: a fan sweeping left to right and from the floor near
        /// you out to the horizon, round-robin, so in a second or two everything in view has been sampled.
        /// </summary>
        public void Scan(RoomSense room, Vector3 head, Vector3 forward, float floorY)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f || room == null)
                return;
            flat.Normalize();
            for (int i = 0; i < RaysPerFrame; i++)
            {
                int ray = nextRay++ % 48;
                float yaw = Mathf.Lerp(-45f, 45f, (ray % 8) / 7f);
                float pitch = Mathf.Lerp(8f, 60f, (ray / 8) / 5f);
                Vector3 direction = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Vector3.Cross(Vector3.up, flat)) * flat;
                // Start a little out so your own hands and arms don't count as walls.
                Vector3 start = head + direction * 0.4f;
                if (!room.TryDistance(start, direction, out float distance) || distance > MaxRay)
                    continue;
                Vector3 hit = start + direction * distance;
                bool floorHit = hit.y < floorY + 0.12f;
                // Everything the ray passed over before the hit was open floor, up to a margin before solid things.
                float clearTo = floorHit ? distance : distance - 0.3f;
                for (float along = 0.3f; along < clearTo; along += Cell * 0.7f)
                    MarkOpen(start + direction * along);
                if (floorHit)
                    MarkOpen(hit);
                else if (hit.y < floorY + 2.2f)
                    MarkBlocked(hit);
            }
        }

        void MarkOpen(Vector3 point)
        {
            if (!ToCell(point, out int x, out int z))
                return;
            int i = Index(x, z);
            if (open[i] == 0)
                openCells++;
            if (open[i] < 250)
                open[i]++;
        }

        void MarkBlocked(Vector3 point)
        {
            if (!ToCell(point, out int x, out int z))
                return;
            int i = Index(x, z);
            if (blocked[i] < 250)
                blocked[i] += 2;
        }

        /// <summary>Reads Space Setup's floor outline when there is one (checked every couple of seconds).</summary>
        public void ReadPlanes(ARPlaneManager planes, float now)
        {
            if (planes == null || now < outlineCheck)
                return;
            outlineCheck = now + 2f;
            ARPlane best = null;
            float bestArea = 0f;
            foreach (ARPlane plane in planes.trackables)
            {
                if ((plane.classifications & PlaneClassifications.Floor) == 0 && plane.alignment != PlaneAlignment.HorizontalUp)
                    continue;
                float area = plane.size.x * plane.size.y;
                // The floor is the biggest upward plane; tables are small.
                if ((plane.classifications & PlaneClassifications.Floor) != 0)
                    area *= 10f;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = plane;
                }
            }
            int before = floorOutline.Count;
            floorOutline.Clear();
            if (best == null)
                return;
            foreach (Vector2 corner in best.boundary)
            {
                Vector3 world = best.transform.TransformPoint(new Vector3(corner.x, 0f, corner.y));
                floorOutline.Add(new Vector2(world.x, world.z));
            }
            floorOutlineY = best.transform.position.y;
            if (before < 3 && floorOutline.Count >= 3)
                Debug.Log($"[Room] Space Setup floor: {floorOutline.Count} corners, {best.size.x:0.0} x {best.size.y:0.0} m");
        }

        /// <summary>
        /// The best place for an enemy to appear: open floor with clearance, outside furniture, far from every player
        /// and from other enemies, between <paramref name="minDistance"/> and <paramref name="maxDistance"/> away.
        /// False if the map knows too little yet.
        /// </summary>
        public bool TryPick(IReadOnlyList<Vector3> players, IReadOnlyList<Vector3> others, float minDistance, float maxDistance,
            System.Func<Vector3, bool> insideFurniture, out Vector3 point, out string source)
        {
            candidates.Clear();
            float floorY = players.Count > 0 ? 0f : 0f;
            if (HasFloorOutline)
            {
                source = "Space Setup floor";
                // Random points inside the floor outline, kept 0.4 m off its edges.
                Bounds bounds = OutlineBounds();
                for (int i = 0; i < 160 && candidates.Count < 60; i++)
                {
                    var p = new Vector2(Random.Range(bounds.min.x, bounds.max.x), Random.Range(bounds.min.z, bounds.max.z));
                    if (Inside(p) && EdgeDistance(p) > 0.4f)
                        candidates.Add(new Vector3(p.x, floorOutlineY, p.y));
                }
            }
            else
            {
                source = "depth scan";
                for (int z = 1; z < Size - 1; z++)
                    for (int x = 1; x < Size - 1; x++)
                        if (Clear(x, z))
                            candidates.Add(CellCentre(x, z, 0f) + new Vector3(Random.Range(-0.08f, 0.08f), 0f, Random.Range(-0.08f, 0.08f)));
            }

            point = Vector3.zero;
            float best = float.MinValue;
            foreach (Vector3 candidate in candidates)
            {
                if (insideFurniture != null && insideFurniture(candidate))
                    continue;
                float nearest = float.MaxValue;
                foreach (Vector3 player in players)
                    nearest = Mathf.Min(nearest, Flat(candidate - player));
                if (nearest < minDistance || nearest > maxDistance)
                    continue;
                float crowd = float.MaxValue;
                foreach (Vector3 other in others)
                    crowd = Mathf.Min(crowd, Flat(candidate - other));
                if (crowd < 0.8f)
                    continue;
                // As far as possible, with a little randomness so they don't all come from one corner.
                float score = nearest + Mathf.Min(crowd, 2f) * 0.3f + Random.Range(0f, 0.9f);
                if (score > best)
                {
                    best = score;
                    point = candidate;
                }
            }
            return best > float.MinValue;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// <summary>Seen open at least twice, nothing solid in it or next to it.</summary>
        bool Clear(int x, int z)
        {
            if (open[Index(x, z)] < 2)
                return false;
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (blocked[Index(x + dx, z + dz)] > open[Index(x + dx, z + dz)])
                        return false;
            return true;
        }

        Bounds OutlineBounds()
        {
            var bounds = new Bounds(new Vector3(floorOutline[0].x, 0f, floorOutline[0].y), Vector3.zero);
            foreach (Vector2 p in floorOutline)
                bounds.Encapsulate(new Vector3(p.x, 0f, p.y));
            return bounds;
        }

        bool Inside(Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = floorOutline.Count - 1; i < floorOutline.Count; j = i++)
            {
                Vector2 a = floorOutline[i];
                Vector2 b = floorOutline[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        float EdgeDistance(Vector2 p)
        {
            float best = float.MaxValue;
            for (int i = 0, j = floorOutline.Count - 1; i < floorOutline.Count; j = i++)
            {
                Vector2 a = floorOutline[j];
                Vector2 ab = floorOutline[i] - a;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                best = Mathf.Min(best, (a + ab * t - p).magnitude);
            }
            return best;
        }

        /// <summary>The floor cells seen open, as points (for showing the scan).</summary>
        public void OpenPoints(List<Vector3> into, float floorY)
        {
            into.Clear();
            for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                    if (open[Index(x, z)] >= 2)
                        into.Add(CellCentre(x, z, floorY) + Vector3.up * (blocked[Index(x, z)] > open[Index(x, z)] ? 0.3f : 0.01f));
        }

        /// <summary>The floor outline from Space Setup, for showing it.</summary>
        public IReadOnlyList<Vector2> FloorOutline => floorOutline;
    }
}
