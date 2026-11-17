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
    ///
    /// Keeping enemies out of walls (11/15). A spawn spot has to pass, in order of strictness:
    /// - strict: the floor itself was seen there (a depth ray landed on it), every cell within the enemy's radius
    ///   plus 25 cm was seen open and nothing solid was seen in them, the spot connects to a player across open
    ///   floor, and the straight line to the nearest player crosses nothing solid (so it can walk in, and be shot);
    /// - relaxed (small rooms, when strict finds nothing): clearance of just the enemy's radius, no floor-seen or
    ///   line rule, and it may appear closer.
    /// Enemies also can't walk, dash or be pushed into cells seen solid, or off Space Setup's floor (see Walled).
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
        /// <summary>Cells where a depth ray landed on the floor itself: the strongest sign a robot can stand there.</summary>
        readonly byte[] floorSeen = new byte[Size * Size];
        readonly byte[] reach = new byte[Size * Size];
        readonly Queue<int> frontier = new Queue<int>();
        readonly List<Vector2> floorOutline = new List<Vector2>();
        readonly List<Vector3> candidates = new List<Vector3>();
        int nextRay;
        int openCells;
        float outlineCheck;
        float floorOutlineY;

        // Space Setup's outline, checked against what the depth scan sees (11/16: in a playtest the robots appeared
        // off from the real hallway and were called out of walls 721 times in 632 spawns, which points at the
        // outline being read flipped or turned). The plane's corners are tried as read and in the seven other
        // flips and quarter turns about the plane's centre; the one that best matches floor seen by the scan wins.
        // Bit 1 flips x, bit 2 flips y, bit 4 swaps x and y (in the plane's own space).
        static readonly string[] OrientationNames =
            { "as read", "x flipped", "y flipped", "turned 180", "x and y swapped", "turned 90", "turned -90", "swapped and turned 180" };
        const int MinEvidenceCells = 30;
        readonly List<Vector2> rawCorners = new List<Vector2>();
        readonly List<Vector2> trial = new List<Vector2>();
        int orientation;
        bool outlineChecked;

        // Paths (11/16 playtest: robots walked through walls, straight at you). A breadth-first search from the
        // players over floor a robot can stand on: with Space Setup, everywhere inside the outline that isn't seen
        // solid; without, only floor the scan has seen open. A robot steps to the neighbouring cell nearer you.
        readonly short[] pathSteps = new short[Size * Size];
        readonly byte[] onOutline = new byte[Size * Size];
        readonly Queue<int> pathQueue = new Queue<int>();
        bool hasPaths;

        /// <summary>The outline was matched against the scan (so it can be trusted over the headset's live depth).</summary>
        public bool OutlineConfirmed => HasFloorOutline && outlineChecked;

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
            System.Array.Clear(floorSeen, 0, floorSeen.Length);
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
                // Each ray is jittered inside its slot of the fan, so over time the floor is sampled everywhere
                // rather than along fixed rings.
                int ray = nextRay++ % 48;
                float yaw = Mathf.Lerp(-45f, 45f, ((ray % 8) + Random.value - 0.5f) / 7f);
                float pitch = Mathf.Lerp(8f, 60f, Mathf.Clamp01(((ray / 8) + Random.value - 0.5f) / 5f));
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
                    MarkFloor(hit);
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

        /// <summary>The floor was seen here: open, and it outweighs an earlier solid sighting (someone walked by).</summary>
        void MarkFloor(Vector3 point)
        {
            MarkOpen(point);
            if (!ToCell(point, out int x, out int z))
                return;
            int i = Index(x, z);
            if (floorSeen[i] < 250)
                floorSeen[i]++;
            if (blocked[i] >= 2)
                blocked[i] -= 2;
        }

        /// <summary>For tests: marks a square of floor as seen (open, floor seen) or a cell as solid.</summary>
        public void TestMarkFloor(Vector3 centre, float half)
        {
            for (float x = -half; x <= half; x += Cell * 0.5f)
                for (float z = -half; z <= half; z += Cell * 0.5f)
                {
                    MarkFloor(centre + new Vector3(x, 0f, z));
                    MarkFloor(centre + new Vector3(x, 0f, z));
                }
        }

        public void TestMarkSolid(Vector3 point)
        {
            for (int i = 0; i < 4; i++)
                MarkBlocked(point);
        }

        /// <summary>Something solid was seen in this cell more than open floor was: a wall, a sofa.</summary>
        bool Solid(int i) => blocked[i] >= 4 && blocked[i] > open[i];

        /// <summary>
        /// A robot shouldn't be here: the scan saw something solid in this cell, or (with Space Setup) it's off the
        /// floor outline. Unknown cells count as fine, so a thin map never freezes anyone.
        /// </summary>
        public bool Walled(Vector3 point, float margin = 0.1f)
        {
            if (HasFloorOutline)
            {
                var p = new Vector2(point.x, point.z);
                if (!Inside(p) || EdgeDistance(p) < margin)
                    return true;
            }
            return ToCell(point, out int x, out int z) && Solid(Index(x, z));
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
            rawCorners.Clear();
            foreach (Vector2 corner in best.boundary)
                rawCorners.Add(corner);
            floorOutlineY = best.transform.position.y;
            ChooseOrientation(best.transform);
            Outline(best.transform, orientation, floorOutline);
            for (int z = 0; z < Size; z++)
                for (int x = 0; x < Size; x++)
                {
                    Vector3 c = CellCentre(x, z, 0f);
                    var cell = new Vector2(c.x, c.z);
                    onOutline[Index(x, z)] = (byte)(Inside(cell) && EdgeDistance(cell) > 0.12f ? 1 : 0);
                }
            if (before < 3 && floorOutline.Count >= 3)
            {
                Vector3 c = best.transform.position;
                Debug.Log($"[Room] Space Setup floor: {floorOutline.Count} corners, {best.size.x:0.0} x {best.size.y:0.0} m, "
                    + $"centre ({c.x:0.00}, {c.y:0.00}, {c.z:0.00}), facing {best.transform.forward.x:0.00},{best.transform.forward.z:0.00}, "
                    + $"up {best.transform.up.y:0.00}, first corner ({rawCorners[0].x:0.00}, {rawCorners[0].y:0.00})");
            }
        }

        /// <summary>The outline in world floor coordinates, with the corners flipped or turned by one orientation.</summary>
        void Outline(Transform plane, int which, List<Vector2> into)
        {
            into.Clear();
            foreach (Vector2 corner in rawCorners)
            {
                Vector2 c = (which & 4) != 0 ? new Vector2(corner.y, corner.x) : corner;
                if ((which & 1) != 0)
                    c.x = -c.x;
                if ((which & 2) != 0)
                    c.y = -c.y;
                Vector3 world = plane.TransformPoint(new Vector3(c.x, 0f, c.y));
                into.Add(new Vector2(world.x, world.z));
            }
        }

        /// <summary>
        /// Scores each orientation by the floor cells the scan saw: inside the outline counts for it, outside counts
        /// twice against it (floor really is there). Once there's enough evidence the best one is used; it only
        /// changes again if another beats it clearly. Logged, so the headset log says which it was.
        /// </summary>
        void ChooseOrientation(Transform plane)
        {
            int seen = 0;
            for (int i = 0; i < floorSeen.Length; i++)
                if (floorSeen[i] > 0)
                    seen++;
            if (seen < MinEvidenceCells || rawCorners.Count < 3)
                return;
            int bestWhich = orientation;
            float bestScore = float.MinValue, current = 0f;
            var scores = new System.Text.StringBuilder();
            for (int which = 0; which < OrientationNames.Length; which++)
            {
                Outline(plane, which, trial);
                float score = 0f;
                for (int z = 0; z < Size; z++)
                    for (int x = 0; x < Size; x++)
                    {
                        if (floorSeen[Index(x, z)] == 0)
                            continue;
                        Vector3 centre = CellCentre(x, z, 0f);
                        score += InsidePolygon(trial, new Vector2(centre.x, centre.z)) ? 1f : -2f;
                    }
                scores.Append($"{OrientationNames[which]} {score / seen:0.00}  ");
                if (which == orientation)
                    current = score;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestWhich = which;
                }
            }
            bool change = bestWhich != orientation && bestScore > current + 0.15f * seen;
            if (change || !outlineChecked)
            {
                if (change)
                    orientation = bestWhich;
                Debug.Log($"[Room] Space Setup floor checked against {seen} floor cells seen: using {OrientationNames[orientation]} "
                    + $"(scores: {scores})");
            }
            outlineChecked = true;
        }

        /// <summary>
        /// The best place for an enemy of this <paramref name="radius"/> to appear: open floor with clearance, outside
        /// furniture, far from every player and from other enemies, between <paramref name="minDistance"/> and
        /// <paramref name="maxDistance"/> away. Strict rules first, relaxed ones if they find nothing (small rooms).
        /// False if the map knows too little yet.
        /// </summary>
        public bool TryPick(IReadOnlyList<Vector3> players, IReadOnlyList<Vector3> others, float minDistance, float maxDistance,
            System.Func<Vector3, bool> insideFurniture, float radius, out Vector3 point, out string source)
        {
            for (int tier = 0; tier < 2; tier++)
            {
                bool strict = tier == 0;
                Gather(players, radius, strict);
                float min = strict ? minDistance : Mathf.Max(1.2f, minDistance * 0.6f);
                if (Best(players, others, min, maxDistance, insideFurniture, strict, out point))
                {
                    source = (HasFloorOutline ? "Space Setup floor" : "depth scan") + (strict ? "" : ", tight room");
                    return true;
                }
            }
            point = Vector3.zero;
            source = "none";
            return false;
        }

        void Gather(IReadOnlyList<Vector3> players, float radius, bool strict)
        {
            candidates.Clear();
            if (HasFloorOutline)
            {
                // Random points inside the floor outline, kept off its edges by the enemy's size.
                float margin = strict ? radius + 0.25f : Mathf.Max(0.2f, radius * 0.6f);
                Bounds bounds = OutlineBounds();
                for (int i = 0; i < 200 && candidates.Count < 70; i++)
                {
                    var p = new Vector2(Random.Range(bounds.min.x, bounds.max.x), Random.Range(bounds.min.z, bounds.max.z));
                    if (Inside(p) && EdgeDistance(p) > margin)
                        candidates.Add(new Vector3(p.x, floorOutlineY, p.y));
                }
                return;
            }
            float clearance = strict ? radius + 0.25f : radius;
            int cells = Mathf.Max(1, Mathf.CeilToInt(clearance / Cell));
            if (strict)
                FloodFrom(players);
            for (int z = cells; z < Size - cells; z++)
                for (int x = cells; x < Size - cells; x++)
                {
                    if (!Clear(x, z, cells, strict))
                        continue;
                    if (strict && reach[Index(x, z)] == 0)
                        continue;
                    candidates.Add(CellCentre(x, z, 0f) + new Vector3(Random.Range(-0.08f, 0.08f), 0f, Random.Range(-0.08f, 0.08f)));
                }
        }

        bool Best(IReadOnlyList<Vector3> players, IReadOnlyList<Vector3> others, float minDistance, float maxDistance,
            System.Func<Vector3, bool> insideFurniture, bool strict, out Vector3 point)
        {
            point = Vector3.zero;
            float best = float.MinValue;
            foreach (Vector3 candidate in candidates)
            {
                if (insideFurniture != null && insideFurniture(candidate))
                    continue;
                float nearest = float.MaxValue;
                Vector3 nearestPlayer = candidate;
                foreach (Vector3 player in players)
                {
                    float d = Flat(candidate - player);
                    if (d < nearest)
                    {
                        nearest = d;
                        nearestPlayer = player;
                    }
                }
                if (nearest < minDistance || nearest > maxDistance)
                    continue;
                float crowd = float.MaxValue;
                foreach (Vector3 other in others)
                    crowd = Mathf.Min(crowd, Flat(candidate - other));
                if (crowd < 0.8f)
                    continue;
                // As far as possible, with a little randomness so they don't all come from one corner.
                float score = nearest + Mathf.Min(crowd, 2f) * 0.3f + Random.Range(0f, 0.9f);
                if (score <= best)
                    continue;
                // Nothing solid between it and you: it can walk in, and your shots can reach it.
                if (!HasFloorOutline && !LineClear(candidate, nearestPlayer))
                    continue;
                best = score;
                point = candidate;
            }
            return best > float.MinValue;
        }

        /// <summary>Marks every cell that connects to a player across floor seen open (and not solid).</summary>
        void FloodFrom(IReadOnlyList<Vector3> players)
        {
            System.Array.Clear(reach, 0, reach.Length);
            frontier.Clear();
            foreach (Vector3 player in players)
            {
                if (!ToCell(player, out int px, out int pz))
                    continue;
                // You stand on floor the rays never look at (they start ahead of you): seed with what's around you.
                for (int dz = -4; dz <= 4; dz++)
                    for (int dx = -4; dx <= 4; dx++)
                    {
                        int x = px + dx, z = pz + dz;
                        if (x < 0 || z < 0 || x >= Size || z >= Size)
                            continue;
                        int i = Index(x, z);
                        if (reach[i] == 0 && !Solid(i))
                        {
                            reach[i] = 1;
                            frontier.Enqueue(i);
                        }
                    }
            }
            while (frontier.Count > 0)
            {
                int i = frontier.Dequeue();
                int x = i % Size, z = i / Size;
                for (int k = 0; k < 4; k++)
                {
                    int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), nz = z + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || nz < 0 || nx >= Size || nz >= Size)
                        continue;
                    int n = Index(nx, nz);
                    if (reach[n] != 0 || open[n] == 0 || Solid(n))
                        continue;
                    reach[n] = 1;
                    frontier.Enqueue(n);
                }
            }
        }

        /// <summary>No solid cell on the straight line between two points.</summary>
        /// <summary>
        /// Nothing in the way between two points on the map: no cell seen solid, and (with Space Setup) the line
        /// stays on the floor outline, so it doesn't cut through a wall between rooms.
        /// </summary>
        public bool LineClear(Vector3 from, Vector3 to)
        {
            float length = Flat(to - from);
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / (Cell * 0.25f)));
            for (int i = 0; i <= steps; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / (float)steps);
                if (!ToCell(p, out int x, out int z))
                    continue;
                int at = Index(x, z);
                if (Solid(at))
                    return false;
                if (HasFloorOutline && !Inside(new Vector2(p.x, p.z)))
                    return false;
            }
            return true;
        }

        bool Walkable(int x, int z)
        {
            int i = Index(x, z);
            if (Solid(i))
                return false;
            return HasFloorOutline ? onOutline[i] != 0 : open[i] > 0;
        }

        /// <summary>Recomputes the paths to the players (a few times a second is plenty).</summary>
        public void UpdatePaths(IReadOnlyList<Vector3> players)
        {
            for (int i = 0; i < pathSteps.Length; i++)
                pathSteps[i] = -1;
            pathQueue.Clear();
            foreach (Vector3 player in players)
            {
                if (!ToCell(player, out int px, out int pz))
                    continue;
                // You stand where the rays don't look: start from your cell and the ones around it.
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int x = px + dx, z = pz + dz;
                        if (x < 0 || z < 0 || x >= Size || z >= Size || pathSteps[Index(x, z)] >= 0)
                            continue;
                        pathSteps[Index(x, z)] = 0;
                        pathQueue.Enqueue(Index(x, z));
                    }
            }
            hasPaths = pathQueue.Count > 0;
            while (pathQueue.Count > 0)
            {
                int i = pathQueue.Dequeue();
                int x = i % Size, z = i / Size;
                short next = (short)(pathSteps[i] + 1);
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dz == 0)
                            continue;
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= Size || nz >= Size || pathSteps[Index(nx, nz)] >= 0 || !Walkable(nx, nz))
                            continue;
                        // No cutting a wall's corner diagonally.
                        if (dx != 0 && dz != 0 && (!Walkable(x + dx, z) || !Walkable(x, z + dz)))
                            continue;
                        pathSteps[Index(nx, nz)] = next;
                        pathQueue.Enqueue(Index(nx, nz));
                    }
            }
        }

        /// <summary>
        /// Which way to walk from here to reach the nearest player round the walls: toward the furthest cell ahead
        /// on the path that's in a straight clear line (so it walks smoothly, not cell by cell). False if this spot
        /// has no path (unknown floor, or cut off).
        /// </summary>
        public bool PathToward(Vector3 from, out Vector3 direction)
        {
            direction = Vector3.zero;
            if (!hasPaths || !ToCell(from, out int x, out int z))
                return false;
            int here = Index(x, z);
            if (pathSteps[here] < 0)
            {
                // Just off the known floor (pushed, or standing at the edge): the best neighbour still counts.
                int best = -1;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= Size || nz >= Size)
                            continue;
                        int n = Index(nx, nz);
                        if (pathSteps[n] >= 0 && (best < 0 || pathSteps[n] < pathSteps[best]))
                            best = n;
                    }
                if (best < 0)
                    return false;
                here = best;
            }
            // Follow the path down a few steps, keeping the furthest cell still in a clear straight line.
            int cell = here, aim = here;
            for (int step = 0; step < 8 && pathSteps[cell] > 0; step++)
            {
                int cx = cell % Size, cz = cell / Size, nextCell = cell;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = cx + dx, nz = cz + dz;
                        if (nx < 0 || nz < 0 || nx >= Size || nz >= Size)
                            continue;
                        int n = Index(nx, nz);
                        if (pathSteps[n] >= 0 && pathSteps[n] < pathSteps[nextCell])
                            nextCell = n;
                    }
                if (nextCell == cell)
                    break;
                cell = nextCell;
                if (CorridorClear(from, CellCentre(cell % Size, cell / Size, from.y), PathHalfWidth))
                    aim = cell;
                else
                    break;
            }
            if (aim == here && pathSteps[here] > 0)
            {
                // Even the next cell isn't in a clear line (pressed against a wall): step to a neighbour nearer
                // you, never diagonally across a wall's corner.
                int hx = here % Size, hz = here / Size;
                for (int dz = -1; dz <= 1; dz++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = hx + dx, nz = hz + dz;
                        if (nx < 0 || nz < 0 || nx >= Size || nz >= Size)
                            continue;
                        if (dx != 0 && dz != 0 && (!Walkable(hx + dx, hz) || !Walkable(hx, hz + dz)))
                            continue;
                        int n = Index(nx, nz);
                        if (pathSteps[n] >= 0 && pathSteps[n] < pathSteps[aim])
                            aim = n;
                    }
            }
            Vector3 target = CellCentre(aim % Size, aim / Size, from.y);
            Vector3 flat = Vector3.ProjectOnPlane(target - from, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                return false;
            direction = flat.normalized;
            return true;
        }

        /// <summary>Half the width kept clear along a path, so a robot rounding a corner doesn't clip the wall.</summary>
        const float PathHalfWidth = 0.18f;

        /// <summary>A straight line with this much room either side of it crosses nothing solid.</summary>
        bool CorridorClear(Vector3 from, Vector3 to, float halfWidth)
        {
            Vector3 along = Vector3.ProjectOnPlane(to - from, Vector3.up);
            if (along.sqrMagnitude < 1e-6f)
                return true;
            Vector3 side = Vector3.Cross(Vector3.up, along.normalized) * halfWidth;
            return LineClear(from, to) && LineClear(from + side, to + side) && LineClear(from - side, to - side);
        }

        /// <summary>This spot has a path to a player.</summary>
        public bool HasPath(Vector3 from) => hasPaths && ToCell(from, out int x, out int z) && pathSteps[Index(x, z)] >= 0;

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        /// <summary>
        /// Seen open at least twice, and nothing solid within <paramref name="cells"/> of it. Strict: the floor was
        /// seen in it or right beside it, and every cell in that clearance was itself seen open (no unknown floor).
        /// </summary>
        bool Clear(int x, int z, int cells, bool strict)
        {
            if (open[Index(x, z)] < 2)
                return false;
            bool floor = false;
            for (int dz = -cells; dz <= cells; dz++)
                for (int dx = -cells; dx <= cells; dx++)
                {
                    int i = Index(x + dx, z + dz);
                    if (blocked[i] > open[i])
                        return false;
                    if (!strict)
                        continue;
                    if (open[i] == 0)
                        return false;
                    if (floorSeen[i] > 0 && Mathf.Abs(dx) <= 1 && Mathf.Abs(dz) <= 1)
                        floor = true;
                }
            return floor || !strict;
        }

        Bounds OutlineBounds()
        {
            var bounds = new Bounds(new Vector3(floorOutline[0].x, 0f, floorOutline[0].y), Vector3.zero);
            foreach (Vector2 p in floorOutline)
                bounds.Encapsulate(new Vector3(p.x, 0f, p.y));
            return bounds;
        }

        bool Inside(Vector2 p) => InsidePolygon(floorOutline, p);

        static bool InsidePolygon(List<Vector2> outline, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = outline.Count - 1; i < outline.Count; j = i++)
            {
                Vector2 a = outline[i];
                Vector2 b = outline[j];
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
