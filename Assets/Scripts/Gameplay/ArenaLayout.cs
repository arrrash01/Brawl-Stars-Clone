using System.Collections.Generic;
using UnityEngine;

namespace RiftRumble
{
    public readonly struct ArenaObstacle
    {
        public readonly Vector3 Center, Size;
        public ArenaObstacle(float x, float z, float width, float depth, float height = 1.8f)
        {
            Center = new Vector3(x, height * .5f, z);
            Size = new Vector3(width, height, depth);
        }
    }

    // Both prediction and authority use these immutable colliders. No dynamic physics bodies drive movement.
    public static class ArenaLayout
    {
        public const float HalfWidth = 16f, HalfDepth = 21f;
        private static readonly ArenaObstacle[] GemWalls =
        {
            new ArenaObstacle(-9, -12, 5, 2), new ArenaObstacle(9, 12, 5, 2),
            new ArenaObstacle(9, -12, 5, 2), new ArenaObstacle(-9, 12, 5, 2),
            new ArenaObstacle(-5, -5, 2, 5), new ArenaObstacle(5, 5, 2, 5),
            new ArenaObstacle(7, -4, 5, 2), new ArenaObstacle(-7, 4, 5, 2),
            new ArenaObstacle(-12, 0, 2, 5), new ArenaObstacle(12, 0, 2, 5),
            new ArenaObstacle(0, -12, 3, 2), new ArenaObstacle(0, 12, 3, 2)
        };
        private static readonly ArenaObstacle[] SoloWalls =
        {
            new ArenaObstacle(-8, -12, 5, 2), new ArenaObstacle(8, 12, 5, 2),
            new ArenaObstacle(8, -12, 2, 5), new ArenaObstacle(-8, 12, 2, 5),
            new ArenaObstacle(-7, -4, 2, 5), new ArenaObstacle(7, 4, 2, 5),
            new ArenaObstacle(6, -4, 4, 2), new ArenaObstacle(-6, 4, 4, 2),
            new ArenaObstacle(-12, 2, 2, 4), new ArenaObstacle(12, -2, 2, 4)
        };
        private static readonly Vector3[] SoloSpawns =
        {
            new Vector3(0, 0, 18), new Vector3(10, 0, 17),
            new Vector3(14, 0, 7), new Vector3(14, 0, -8), new Vector3(7, 0, -18),
            new Vector3(0, 0, -18), new Vector3(-10, 0, -17),
            new Vector3(-14, 0, -7), new Vector3(-14, 0, 8), new Vector3(-7, 0, 18)
        };

        public static IReadOnlyList<ArenaObstacle> Obstacles(GameMode mode) => mode == GameMode.GemRush ? GemWalls : SoloWalls;

        public static Vector3 Move(Vector3 position, Vector2 input, float speed, float dt, GameMode mode)
        {
            if (!Finite(input.x) || !Finite(input.y)) return position;
            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 step = new Vector3(input.x, 0, input.y) * speed * dt;
            // Substeps make replay safe even if a caller uses a larger timestep.
            int count = Mathf.Max(1, Mathf.CeilToInt(step.magnitude / .2f));
            step /= count;
            for (int i = 0; i < count; i++)
            {
                Vector3 x = ClampPosition(position + new Vector3(step.x, 0, 0));
                if (!IsBlocked(x, mode)) position = x;
                Vector3 z = ClampPosition(position + new Vector3(0, 0, step.z));
                if (!IsBlocked(z, mode)) position = z;
            }
            return position;
        }

        public static Vector3 ClampPosition(Vector3 p) => new Vector3(
            Mathf.Clamp(p.x, -HalfWidth + GameRules.PlayerRadius, HalfWidth - GameRules.PlayerRadius), 0,
            Mathf.Clamp(p.z, -HalfDepth + GameRules.PlayerRadius, HalfDepth - GameRules.PlayerRadius));

        public static Vector3 ClampPosition(Vector3 p, GameMode mode) => ClampPosition(p);

        public static bool IsBlocked(Vector3 p, GameMode mode)
            => IsBlocked(p, mode, GameRules.PlayerRadius);

        private static bool IsBlocked(Vector3 p, GameMode mode, float radius)
        {
            foreach (ArenaObstacle wall in Obstacles(mode))
            {
                Vector3 half = wall.Size * .5f;
                float x = Mathf.Clamp(p.x, wall.Center.x - half.x, wall.Center.x + half.x);
                float z = Mathf.Clamp(p.z, wall.Center.z - half.z, wall.Center.z + half.z);
                if ((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z) < radius * radius) return true;
            }
            return false;
        }

        public static bool HasLineOfSight(Vector3 from, Vector3 to, GameMode mode)
        {
            Vector3 delta = to - from; delta.y = 0;
            float length = delta.magnitude;
            if (length < .01f) return true;
            Ray ray = new Ray(new Vector3(from.x, .8f, from.z), delta / length);
            foreach (ArenaObstacle wall in Obstacles(mode))
                if (new Bounds(wall.Center, wall.Size).IntersectRay(ray, out float distance) && distance < length) return false;
            return true;
        }

        public static Vector3 Spawn(GameMode mode, int slot)
        {
            if (mode == GameMode.GemRush)
                return new Vector3((slot / 2 - 1) * 4f, 0, slot % 2 == 0 ? -17f : 17f);
            Vector3 preferred = SoloSpawns[(slot % SoloSpawns.Length + SoloSpawns.Length) % SoloSpawns.Length];
            // Leave room for a first step in every direction, including after a layout adjustment.
            const float clearance = GameRules.PlayerRadius + .7f;
            if (!IsBlocked(preferred, mode, clearance)) return preferred;
            for (float radius = .5f; radius <= 4; radius += .5f)
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI * 2 / 16;
                Vector3 candidate = ClampPosition(preferred + new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle)) * radius);
                if (!IsBlocked(candidate, mode, clearance)) return candidate;
            }
            Debug.LogError("No clear Showdown spawn near slot " + slot);
            return preferred;
        }

        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
