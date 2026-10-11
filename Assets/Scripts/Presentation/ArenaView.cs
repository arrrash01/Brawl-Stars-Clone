using UnityEngine;
using UnityEngine.Rendering;

namespace RiftRumble
{
    /// <summary>Decorates the same immutable layout used by server movement and client replay.</summary>
    public sealed class ArenaView : MonoBehaviour
    {
        Transform content;
        static readonly System.Collections.Generic.Dictionary<string, Material> Materials = new System.Collections.Generic.Dictionary<string, Material>();

        public void Build(GameMode mode)
        {
            if (content != null)
            {
                content.gameObject.SetActive(false);
                Destroy(content.gameObject);
            }
            var root = new GameObject("Arena · " + GameRules.ModeName(mode));
            content = root.transform;
            content.SetParent(transform, false);
            Box("Outer sand", new Vector3(0, -.50f, 0), new Vector3(78, .7f, 90), "SandDark");
            Box("Arena foundation", new Vector3(0, -.33f, 0), new Vector3(34, .6f, 44), "ArenaEdge");
            Box("Playing field", new Vector3(0, -.09f, 0), new Vector3(32, .18f, 42), "Sand", true);

            // Broad patches read as sand strata, without suggesting extra gameplay colliders.
            for (int z = -18; z <= 18; z += 6)
            for (int x = -12; x <= 12; x += 6)
            {
                int seed = (x + 16) * 23 + (z + 21) * 7;
                if (seed % 3 == 0) continue;
                var patch = Box("Sand patch", new Vector3(x + (seed % 3) * .4f, .006f, z), new Vector3(2.9f + seed % 2, .012f, 1.8f), seed % 2 == 0 ? "SandLight" : "SandDark");
                patch.transform.localEulerAngles = new Vector3(0, seed % 30 - 15, 0);
            }

            Boundary(new Vector3(-16.35f, .55f, 0), new Vector3(.7f, 1.1f, 43.4f));
            Boundary(new Vector3(16.35f, .55f, 0), new Vector3(.7f, 1.1f, 43.4f));
            Boundary(new Vector3(0, .55f, -21.35f), new Vector3(32, 1.1f, .7f));
            Boundary(new Vector3(0, .55f, 21.35f), new Vector3(32, 1.1f, .7f));

            var obstacles = ArenaLayout.Obstacles(mode);
            for (int i = 0; i < obstacles.Count; i++)
            {
                ArenaObstacle wall = obstacles[i];
                Box("Cover " + (i + 1), wall.Center, wall.Size, "Stone", true);
                Box("Cover cap", wall.Center + Vector3.up * (wall.Size.y * .5f + .055f), new Vector3(wall.Size.x + .10f, .11f, wall.Size.z + .10f), "StoneTop");
                Box("Cover footing", new Vector3(wall.Center.x, .14f, wall.Center.z), new Vector3(wall.Size.x + .12f, .28f, wall.Size.z + .12f), "StoneDark");
                bool wide = wall.Size.x > wall.Size.z;
                int segments = Mathf.Max(1, Mathf.FloorToInt((wide ? wall.Size.x : wall.Size.z) / 1.5f));
                for (int j = 0; j < segments; j++)
                {
                    float offset = (j - (segments - 1) * .5f) * 1.6f;
                    Vector3 trimPos = wall.Center + new Vector3(wide ? offset : wall.Size.x * .5f + .015f, .25f, wide ? -wall.Size.z * .5f - .015f : offset);
                    Box("Stone inset", trimPos, wide ? new Vector3(.85f, .12f, .035f) : new Vector3(.035f, .12f, .85f), "StoneDark");
                }
                Vector3 grassAt = wall.Center + new Vector3(-wall.Size.x * .5f - .35f, -wall.Center.y, -wall.Size.z * .5f);
                Prop("Grass", grassAt, 1.15f, i * 57);
            }
            if (mode == GameMode.GemRush)
            {
                Prop("GemWell", Vector3.zero, 1, 0);
                SpawnMark(-17, "BlueTeam");
                SpawnMark(17, "RedTeam");
            }
            else
            {
                // A cracked rift marks the central contest area in the survival layout.
                var p = Prop("GemWell", Vector3.zero, .85f, 30);
                p.name = "Central rift landmark";
            }

            for (int side = -1; side <= 1; side += 2)
            for (int i = 0; i < 8; i++)
            {
                float z = -20 + i * 5.7f;
                Prop("Rock", new Vector3(side * (19.8f + i % 2 * 1.3f), 0, z), 1.3f + i % 3 * .5f, i * 53f);
                if (i % 2 == 0) Prop("Cactus", new Vector3(side * (18.9f + i % 3), 0, z + 2.5f), 1.05f + i % 3 * .16f, side * 10f);
                if (i % 3 == 0) Prop("Pylon", new Vector3(side * 17.3f, 0, z), 1, 0);
                Prop("Grass", new Vector3(side * 17.8f, 0, z - 1.9f), 1.3f, i * 77f);
            }
            for (int i = -2; i <= 2; i++)
            {
                Prop("Rock", new Vector3(i * 7.6f, 0, -24.6f - i % 2), 1.8f, i * 31f);
                Prop("Rock", new Vector3(i * 7.1f + 2, 0, 24.8f + i % 2), 2.2f, i * 49f);
            }
        }

        void Boundary(Vector3 center, Vector3 size)
        {
            Box("Boundary wall", center, size, "ArenaEdge", true);
            Box("Boundary illuminated rail", center + Vector3.up * (size.y * .5f + .035f), new Vector3(size.x, .07f, size.z), "CyanDark");
        }

        void SpawnMark(float z, string material)
        {
            Box("Spawn lane", new Vector3(0, .022f, z), new Vector3(11, .018f, .12f), material);
            for (int i = -1; i <= 1; i++)
            {
                Box("Spawn marker", new Vector3(i * 4, .022f, z), new Vector3(.12f, .018f, 1.6f), material);
                Box("Spawn marker", new Vector3(i * 4, .022f, z + 1f), new Vector3(1.6f, .018f, .12f), material);
            }
        }

        GameObject Box(string objectName, Vector3 position, Vector3 size, string material, bool collider = false)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = objectName;
            box.transform.SetParent(content, false);
            box.transform.localPosition = position;
            box.transform.localScale = size;
            var col = box.GetComponent<Collider>();
            if (!collider) { col.enabled = false; Destroy(col); }
            var renderer = box.GetComponent<Renderer>();
            renderer.sharedMaterial = Material(material);
            if (size.y < .1f) renderer.shadowCastingMode = ShadowCastingMode.Off;
            return box;
        }

        GameObject Prop(string prefab, Vector3 position, float scale, float yaw)
        {
            var asset = Resources.Load<GameObject>("RiftRumble/Environment/" + prefab);
            if (asset == null) return new GameObject("Missing art: " + prefab);
            var go = Instantiate(asset, content);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one * scale;
            return go;
        }

        static Material Material(string name)
        {
            if (!Materials.TryGetValue(name, out var material) || material == null)
            {
                material = Resources.Load<Material>("RiftRumble/Materials/" + name);
                Materials[name] = material;
            }
            return material;
        }
    }
}
