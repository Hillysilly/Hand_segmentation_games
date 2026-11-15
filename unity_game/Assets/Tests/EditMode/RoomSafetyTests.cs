using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Repulsor.Tests
{
    /// <summary>
    /// Enemies stay out of walls: where the room map lets them appear, the relaxed rules for small rooms, and the
    /// fail-safe that lets your attacks through a wall to an enemy stuck in or behind it. Also the room light estimate.
    /// </summary>
    public class RoomSafetyTests
    {
        static readonly List<Vector3> NoOthers = new List<Vector3>();

        /// <summary>A stand-in enemy for line-of-fire tests.</summary>
        sealed class Dummy : IHittable
        {
            public Vector3 Center { get; set; }
            public float Radius => 0.25f;
            public bool Alive => true;
            public Team Team => Team.Enemies;
            public float Hp => 10f;
            public float MaxHp => 10f;
            public float TakeHit(float damage, Vector3 from) => damage;
            public Vector3 BarPoint => Center;
        }

        /// <summary>A room of open floor 6 m square round the origin, with a wall of solid cells along x = 3.</summary>
        static RoomMap Room(float half = 3f)
        {
            var map = new RoomMap();
            map.TestMarkFloor(Vector3.zero, half);
            for (float z = -half; z <= half; z += RoomMap.Cell * 0.5f)
                map.TestMarkSolid(new Vector3(half + RoomMap.Cell * 0.5f, 0f, z));
            return map;
        }

        [Test]
        public void SpawnsKeepTheirBodyClearOfWalls()
        {
            RoomMap map = Room();
            var players = new List<Vector3> { new Vector3(-2f, 1.6f, 0f) };
            for (int i = 0; i < 200; i++)
            {
                Assert.That(map.TryPick(players, NoOthers, 2f, 8f, null, 0.4f, out Vector3 point, out string source), Is.True);
                Assert.That(source, Is.EqualTo("depth scan"), "a 6 m room needs no relaxing");
                // The tank's 0.4 m radius plus the margin: its edge is never at the wall, or past the scanned floor.
                Assert.That(point.x, Is.LessThan(3f - 0.4f - 0.1f), $"too near the wall at {point}");
                Assert.That(Mathf.Abs(point.z), Is.LessThan(3f - 0.4f), $"at the edge of the known floor at {point}");
                Assert.That(Vector3.Distance(new Vector3(point.x, 0f, point.z), new Vector3(-2f, 0f, 0f)), Is.GreaterThanOrEqualTo(2f));
            }
        }

        [Test]
        public void NothingAppearsBeyondAWallOrOnFloorSeenThroughIt()
        {
            RoomMap map = Room();
            // A patch of floor seen beyond the wall (through a window, or depth noise): open, but cut off.
            map.TestMarkFloor(new Vector3(5f, 0f, 0f), 1f);
            var players = new List<Vector3> { new Vector3(-2f, 1.6f, 0f) };
            for (int i = 0; i < 200; i++)
            {
                Assert.That(map.TryPick(players, NoOthers, 2f, 9f, null, 0.24f, out Vector3 point, out _), Is.True);
                Assert.That(point.x, Is.LessThan(3f), $"spawned beyond the wall at {point}");
            }
        }

        [Test]
        public void ASmallRoomRelaxesTheRulesRatherThanGivingUp()
        {
            // 2.5 m square: nowhere is 2 m from a player in the middle with full clearance.
            RoomMap map = Room(1.25f);
            var players = new List<Vector3> { new Vector3(0f, 1.6f, 0f) };
            Assert.That(map.TryPick(players, NoOthers, 2f, 8f, null, 0.24f, out Vector3 point, out string source), Is.True);
            Assert.That(source, Does.Contain("tight room"));
            Assert.That(point.x, Is.LessThan(1.25f), "not in the wall");
            Assert.That(Mathf.Abs(point.z), Is.LessThan(1.5f), "on floor that was seen");
            Assert.That(new Vector2(point.x, point.z).magnitude, Is.GreaterThanOrEqualTo(1.2f), "still not on top of you");
        }

        [Test]
        public void AnEmptyMapPicksNothing()
        {
            var map = new RoomMap();
            var players = new List<Vector3> { Vector3.up * 1.6f };
            Assert.That(map.TryPick(players, NoOthers, 2f, 8f, null, 0.24f, out _, out _), Is.False);
        }

        [Test]
        public void SolidCellsCountAsWallsButUnknownOnesDoNot()
        {
            RoomMap map = Room();
            Assert.That(map.Walled(new Vector3(3.1f, 0f, 0f)), Is.True, "the wall");
            Assert.That(map.Walled(new Vector3(1f, 0f, 0f)), Is.False, "open floor");
            Assert.That(map.Walled(new Vector3(7f, 0f, 7f)), Is.False, "never seen: not a wall, so nobody freezes");
            // The floor seen there later (someone had walked past): open again.
            Assert.That(map.Walled(new Vector3(3.1f, 0f, 0.1f)), Is.True);
            for (int i = 0; i < 4; i++)
                map.TestMarkFloor(new Vector3(3.1f, 0f, 0.1f), 0.01f);
            Assert.That(map.Walled(new Vector3(3.1f, 0f, 0.1f)), Is.False);
        }

        [Test]
        public void AttacksReachAnEnemyInOrBehindAWallButWallsStillStopTheRest()
        {
            Hittables.All.Clear();
            var enemy = new Dummy { Center = new Vector3(0f, 1f, 4.2f) };
            Hittables.Add(enemy);
            try
            {
                // The wall is 4 m away; the enemy's middle is 20 cm inside it.
                float reach = RepulsorGame.ReachPast(new Vector3(0f, 1f, 0f), Vector3.forward, 4f);
                Assert.That(reach, Is.GreaterThan(4.2f + enemy.Radius), "carries on through the wall to the enemy");
                // Right behind the wall (the next room): still reached.
                enemy.Center = new Vector3(0f, 1f, 5.5f);
                Assert.That(RepulsorGame.ReachPast(new Vector3(0f, 1f, 0f), Vector3.forward, 4f), Is.GreaterThan(5.5f));
                // In front of the wall: the wall is where the shot stops, as before.
                enemy.Center = new Vector3(0f, 1f, 2f);
                Assert.That(RepulsorGame.ReachPast(new Vector3(0f, 1f, 0f), Vector3.forward, 4f), Is.EqualTo(4f));
                // Off the line of fire: no effect.
                enemy.Center = new Vector3(2f, 1f, 4.5f);
                Assert.That(RepulsorGame.ReachPast(new Vector3(0f, 1f, 0f), Vector3.forward, 4f), Is.EqualTo(4f));
            }
            finally
            {
                Hittables.Remove(enemy);
            }
        }

        [Test]
        public void TheRoomLightFollowsBrightnessAndColour()
        {
            RoomLight.Estimate(new Color(0.2f, 0.2f, 0.2f), out Color tint, out float level);
            Assert.That(level, Is.EqualTo(1f).Within(0.05f), "a normally lit room");
            Assert.That(tint.r, Is.EqualTo(1f).Within(0.02f));
            Assert.That(tint.b, Is.EqualTo(1f).Within(0.02f));

            RoomLight.Estimate(new Color(0.01f, 0.01f, 0.01f), out _, out float dark);
            Assert.That(dark, Is.LessThan(0.45f), "a dark room dims them");
            Assert.That(dark, Is.GreaterThanOrEqualTo(0.3f), "but never to black");

            RoomLight.Estimate(new Color(0.3f, 0.2f, 0.1f), out Color warm, out _);
            Assert.That(warm.r, Is.GreaterThan(warm.g));
            Assert.That(warm.g, Is.GreaterThan(warm.b), "warm lamps warm them");
            Assert.That(warm.r, Is.LessThanOrEqualTo(1.4f));
            Assert.That(warm.b, Is.GreaterThanOrEqualTo(0.6f));
        }
    }
}
