using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RiftRumble.Editor
{
    /// <summary>Authored geometry for Rift Rumble. Rebuilding preserves existing asset GUIDs.</summary>
    public static class ArenaArtBuilder
    {
        const string Art = "Assets/Art/RiftRumble";
        const string Resources = "Assets/Resources/RiftRumble";
        static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        static readonly Dictionary<string, Mesh> Meshes = new Dictionary<string, Mesh>();

        [MenuItem("Rift Rumble/Build Art Assets")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(Art + "/Meshes");
            Directory.CreateDirectory(Resources + "/Materials");
            Directory.CreateDirectory(Resources + "/Models");
            Directory.CreateDirectory(Resources + "/Environment");
            AssetDatabase.Refresh();
            Materials.Clear();
            Meshes.Clear();
            Material("Ink", "182C48");
            Material("DeepInk", "0C1C32");
            Material("Steel", "809CAC", .35f);
            Material("Ivory", "FFF4CB");
            Material("Coral", "F86563");
            Material("CoralDark", "A8354D");
            Material("CoralLight", "FFAD7E");
            Material("Cyan", "32D9E6", .18f);
            Material("CyanDark", "177C9C");
            Material("CyanGlow", "8EFEF0", .1f, .8f);
            Material("Yellow", "FFC847");
            Material("Gold", "B57936", .35f);
            Material("Plum", "714596");
            Material("PlumDark", "392953");
            Material("Gem", "C570FF", .1f, .6f);
            Material("GemLight", "F4BDFF", .1f, 1.2f);
            Material("Sand", "CF8D6F");
            Material("SandLight", "E4AD80");
            Material("SandDark", "B97466");
            Material("Stone", "8C607B");
            Material("StoneTop", "C18896");
            Material("StoneDark", "694D71");
            Material("Grass", "49B69E");
            Material("GrassLight", "92D19B");
            Material("ArenaEdge", "293E5A");
            Material("BlueTeam", "44BDE7", 0, .4f);
            Material("RedTeam", "FF7278", 0, .4f);
            ShieldMaterial();
            Mesh("Bevel", BevelBox(.14f));
            Mesh("BevelWide", BevelBox(.07f));
            Mesh("Orb", Ellipsoid(12, 7));
            Mesh("Hex", Cylinder(6, 1, 1));
            Mesh("Cylinder", Cylinder(16, 1, 1));
            Mesh("Taper", Cylinder(8, 1, .65f));
            Mesh("Cone", Cylinder(7, 1, .01f));
            Mesh("Gem", Crystal());
            Mesh("Ring", Ring(40, .79f));
            BuildBront();
            BuildVex();
            BuildPip();
            BuildEnvironment();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Rift Rumble: original models, articulated rigs, materials and arena props generated.");
        }

        static void BuildBront()
        {
            var root = new GameObject("Bront");
            var body = Pivot(root.transform, "Torso", new Vector3(0, 1.12f, 0));
            Part(body, "Heavy jacket", "Bevel", "Coral", new Vector3(0, .25f, 0), new Vector3(1.0f, .88f, .62f));
            Part(body, "Dark undershirt", "Bevel", "Ink", new Vector3(0, -.18f, 0), new Vector3(.72f, .32f, .52f));
            Part(body, "Chest guard", "Bevel", "CoralDark", new Vector3(0, .22f, .34f), new Vector3(.73f, .40f, .12f));
            Part(body, "Chest sigil", "Gem", "Ivory", new Vector3(0, .23f, .425f), new Vector3(.17f, .25f, .07f));
            Part(body, "Collar", "Bevel", "Ink", new Vector3(0, .64f, 0), new Vector3(.68f, .19f, .58f));
            Part(body, "Belt", "Bevel", "Gold", new Vector3(0, -.30f, .06f), new Vector3(.83f, .16f, .59f));
            Part(body, "Belt buckle", "Bevel", "Yellow", new Vector3(0, -.30f, .38f), new Vector3(.25f, .22f, .12f));
            var head = Pivot(body, "Head", new Vector3(0, .65f, .02f));
            Part(head, "Helmet", "Bevel", "CoralDark", new Vector3(0, .31f, 0), new Vector3(.66f, .63f, .58f));
            Part(head, "Faceplate", "Bevel", "Ink", new Vector3(0, .27f, .285f), new Vector3(.57f, .35f, .11f));
            Part(head, "Visor glow", "Bevel", "CyanGlow", new Vector3(0, .32f, .355f), new Vector3(.44f, .10f, .05f));
            Part(head, "Brow", "Bevel", "Coral", new Vector3(0, .44f, .31f), new Vector3(.64f, .14f, .18f));
            Part(head, "Jaw guard", "Bevel", "Steel", new Vector3(0, .08f, .30f), new Vector3(.46f, .16f, .19f));
            Part(head, "Mohawk front", "Bevel", "CoralLight", new Vector3(0, .67f, .12f), new Vector3(.19f, .27f, .21f));
            Part(head, "Mohawk rear", "Bevel", "Coral", new Vector3(0, .65f, -.12f), new Vector3(.18f, .24f, .19f));
            for (int side = -1; side <= 1; side += 2)
            {
                string suffix = side < 0 ? "L" : "R";
                var arm = Pivot(body, "Arm" + suffix, new Vector3(side * .57f, .42f, 0));
                Part(arm, "Shoulder", "Bevel", "CoralLight", Vector3.zero, new Vector3(.57f, .44f, .69f));
                Part(arm, "Arm joint", "Orb", "Ink", new Vector3(side * .035f, -.28f, 0), new Vector3(.34f, .42f, .36f));
                Part(arm, "Power gauntlet", "Bevel", "Coral", new Vector3(side * .09f, -.59f, .18f), new Vector3(.62f, .61f, .72f));
                Part(arm, "Fist armor", "Bevel", "CoralDark", new Vector3(side * .09f, -.59f, .54f), new Vector3(.54f, .43f, .13f));
                Part(arm, "Power band", "Bevel", "Yellow", new Vector3(side * .09f, -.36f, .18f), new Vector3(.66f, .13f, .68f));
                Part(arm, "Power core", "Hex", "CyanGlow", new Vector3(side * .415f, -.55f, .20f), new Vector3(.30f, .07f, .30f), new Vector3(0, 0, 90));
                for (int k = -1; k <= 1; k++)
                    Part(arm, "Knuckle", "Bevel", "Ivory", new Vector3(side * .09f + k * .155f, -.48f, .63f), new Vector3(.12f, .21f, .12f));
                var leg = Pivot(root.transform, "Leg" + suffix, new Vector3(side * .25f, .79f, 0));
                Part(leg, "Trouser", "Bevel", "Ink", new Vector3(0, -.15f, 0), new Vector3(.39f, .45f, .41f));
                Part(leg, "Knee guard", "Bevel", "Coral", new Vector3(0, -.27f, .21f), new Vector3(.34f, .28f, .12f));
                Part(leg, "Boot", "Bevel", "CoralDark", new Vector3(0, -.58f, .12f), new Vector3(.46f, .35f, .69f));
                Part(leg, "Toe cap", "Bevel", "Steel", new Vector3(0, -.56f, .42f), new Vector3(.44f, .20f, .13f));
                Part(leg, "Sole", "Bevel", "Ink", new Vector3(0, -.74f, .13f), new Vector3(.47f, .11f, .71f));
            }
            Save(root, "Models/Bront");
        }

        static void BuildVex()
        {
            var root = new GameObject("Vex");
            var body = Pivot(root.transform, "Torso", new Vector3(0, 1.08f, 0));
            Part(body, "Tactical jacket", "Taper", "Cyan", new Vector3(0, .23f, 0), new Vector3(.85f, .77f, .56f), new Vector3(0, 22.5f, 0));
            Part(body, "Body armor", "Bevel", "Ink", new Vector3(0, .20f, .28f), new Vector3(.48f, .55f, .13f));
            Part(body, "Energy center", "Gem", "CyanGlow", new Vector3(0, .25f, .38f), new Vector3(.17f, .30f, .07f));
            Part(body, "Belt", "Bevel", "Ivory", new Vector3(0, -.22f, .02f), new Vector3(.66f, .12f, .53f));
            Part(body, "Scarf collar", "Bevel", "Yellow", new Vector3(0, .61f, .015f), new Vector3(.68f, .20f, .61f));
            Part(body, "Scarf tail", "Bevel", "Yellow", new Vector3(-.21f, .11f, -.39f), new Vector3(.29f, .72f, .10f), new Vector3(-15, -15, -12));
            Part(body, "Scarf tip", "Gem", "Gold", new Vector3(-.30f, -.23f, -.49f), new Vector3(.28f, .31f, .08f), new Vector3(0, 0, -15));
            var head = Pivot(body, "Head", new Vector3(0, .63f, 0));
            Part(head, "Mask shell", "Bevel", "Ink", new Vector3(0, .31f, 0), new Vector3(.62f, .65f, .59f));
            Part(head, "Mask front", "Bevel", "Cyan", new Vector3(0, .22f, .29f), new Vector3(.64f, .39f, .12f));
            Part(head, "Visor", "Bevel", "DeepInk", new Vector3(0, .39f, .34f), new Vector3(.55f, .19f, .07f));
            for (int side = -1; side <= 1; side += 2)
            {
                Part(head, "Sharp eye", "Bevel", "CyanGlow", new Vector3(side * .145f, .38f, .385f), new Vector3(.18f, .06f, .05f), new Vector3(0, 0, side * 12));
                Part(head, "Ear fin", "Cone", "Cyan", new Vector3(side * .25f, .75f, -.07f), new Vector3(.24f, .42f, .26f), new Vector3(-10, 0, side * -13));
                Part(head, "Ear insert", "Cone", "Ivory", new Vector3(side * .25f, .74f, .047f), new Vector3(.115f, .25f, .03f), new Vector3(0, 0, side * -13));
                string suffix = side < 0 ? "L" : "R";
                var arm = Pivot(body, "Arm" + suffix, new Vector3(side * .47f, .42f, 0));
                Part(arm, "Shoulder pad", "Bevel", "Ivory", Vector3.zero, new Vector3(.36f, .29f, .40f), new Vector3(0, 0, side * -15));
                Part(arm, "Sleeve", "Bevel", "CyanDark", new Vector3(side * .035f, -.27f, .025f), new Vector3(.26f, .45f, .28f));
                Part(arm, "Glove", "Bevel", "Ink", new Vector3(side * .035f, -.51f, .10f), new Vector3(.30f, .25f, .33f));
                if (side > 0)
                {
                    var gun = Pivot(arm, "Weapon", new Vector3(.04f, -.46f, .16f));
                    Part(gun, "Pulse rifle", "Bevel", "Ivory", new Vector3(0, .08f, .33f), new Vector3(.32f, .29f, .83f));
                    Part(gun, "Upper housing", "Bevel", "CyanDark", new Vector3(0, .23f, .31f), new Vector3(.26f, .13f, .57f));
                    Part(gun, "Energy cartridge", "Bevel", "CyanGlow", new Vector3(.18f, .09f, .22f), new Vector3(.07f, .17f, .29f));
                    Part(gun, "Muzzle", "Hex", "Ink", new Vector3(0, .08f, .80f), new Vector3(.33f, .20f, .33f), new Vector3(90, 0, 0));
                    Part(gun, "Muzzle light", "Hex", "CyanGlow", new Vector3(0, .08f, .91f), new Vector3(.19f, .03f, .19f), new Vector3(90, 0, 0));
                    Part(gun, "Sight", "Bevel", "Yellow", new Vector3(0, .34f, .46f), new Vector3(.07f, .11f, .10f));
                }
                var leg = Pivot(root.transform, "Leg" + suffix, new Vector3(side * .20f, .78f, 0));
                Part(leg, "Trouser", "Bevel", "Ink", new Vector3(0, -.24f, 0), new Vector3(.28f, .58f, .33f));
                Part(leg, "Shin plate", "Bevel", "Cyan", new Vector3(0, -.45f, .14f), new Vector3(.26f, .32f, .11f));
                Part(leg, "Boot", "Bevel", "CyanDark", new Vector3(0, -.64f, .13f), new Vector3(.35f, .25f, .61f));
                Part(leg, "Sole", "Bevel", "Ivory", new Vector3(0, -.75f, .14f), new Vector3(.36f, .09f, .62f));
            }
            Save(root, "Models/Vex");
        }

        static void BuildPip()
        {
            var root = new GameObject("Pip");
            var body = Pivot(root.transform, "Torso", new Vector3(0, .96f, 0));
            Part(body, "Boiler suit", "Orb", "Yellow", new Vector3(0, .18f, 0), new Vector3(1.00f, .98f, .78f));
            Part(body, "Belly plate", "Bevel", "Gold", new Vector3(0, .15f, .35f), new Vector3(.61f, .52f, .15f));
            Part(body, "Panel", "Bevel", "Ink", new Vector3(0, .18f, .45f), new Vector3(.45f, .26f, .07f));
            for (int k = -1; k <= 1; k++)
                Part(body, "Indicator", "Bevel", k == 0 ? "CyanGlow" : "Ivory", new Vector3(k * .12f, .18f, .50f), new Vector3(.07f, .13f, .04f));
            Part(body, "Utility belt", "Bevel", "Plum", new Vector3(0, -.22f, 0), new Vector3(.88f, .18f, .70f));
            Part(body, "Backpack", "Bevel", "PlumDark", new Vector3(0, .21f, -.49f), new Vector3(.85f, .92f, .39f));
            for (int side = -1; side <= 1; side += 2)
            {
                Part(body, "Canister", "Cylinder", "Plum", new Vector3(side * .27f, .34f, -.68f), new Vector3(.33f, .99f, .33f));
                Part(body, "Canister cap", "Hex", "Ivory", new Vector3(side * .27f, .84f, -.68f), new Vector3(.34f, .10f, .34f));
                Part(body, "Canister band", "Cylinder", "Yellow", new Vector3(side * .27f, .34f, -.68f), new Vector3(.35f, .15f, .35f));
            }
            var head = Pivot(body, "Head", new Vector3(0, .59f, .04f));
            Part(head, "Pilot helmet", "Orb", "Plum", new Vector3(0, .32f, 0), new Vector3(.91f, .81f, .75f));
            Part(head, "Goggle strap", "Bevel", "Ink", new Vector3(0, .33f, .025f), new Vector3(.97f, .20f, .65f));
            Part(head, "Helmet ridge", "Bevel", "Yellow", new Vector3(0, .70f, -.03f), new Vector3(.22f, .13f, .47f));
            Part(head, "Mouth grille", "Bevel", "Ivory", new Vector3(0, .05f, .33f), new Vector3(.36f, .12f, .08f));
            Part(head, "Smile line", "Bevel", "Ink", new Vector3(0, .055f, .38f), new Vector3(.25f, .035f, .03f));
            Part(head, "Antenna", "Cylinder", "Ink", new Vector3(.35f, .81f, -.10f), new Vector3(.045f, .33f, .045f), new Vector3(0, 0, -17));
            Part(head, "Antenna lamp", "Orb", "CyanGlow", new Vector3(.398f, .97f, -.10f), new Vector3(.15f, .15f, .15f));
            for (int side = -1; side <= 1; side += 2)
            {
                Part(head, "Goggle rim", "Cylinder", "Gold", new Vector3(side * .23f, .34f, .37f), new Vector3(.39f, .16f, .39f), new Vector3(90, 0, 0));
                Part(head, "Goggle lens", "Cylinder", "CyanGlow", new Vector3(side * .23f, .34f, .46f), new Vector3(.29f, .035f, .29f), new Vector3(90, 0, 0));
                Part(head, "Lens highlight", "Bevel", "Ivory", new Vector3(side * .23f - .045f, .395f, .49f), new Vector3(.10f, .045f, .025f), new Vector3(0, 0, 25));
                string suffix = side < 0 ? "L" : "R";
                var arm = Pivot(body, "Arm" + suffix, new Vector3(side * .51f, .38f, .02f));
                Part(arm, "Shoulder joint", "Orb", "Ink", Vector3.zero, new Vector3(.34f, .35f, .34f));
                Part(arm, "Sleeve", "Bevel", "Yellow", new Vector3(side * .06f, -.22f, .03f), new Vector3(.31f, .37f, .34f));
                Part(arm, "Glove", "Bevel", "Plum", new Vector3(side * .07f, -.43f, .15f), new Vector3(.35f, .29f, .39f));
                if (side > 0)
                {
                    var gun = Pivot(arm, "Weapon", new Vector3(.06f, -.35f, .38f));
                    Part(gun, "Lobber housing", "Cylinder", "PlumDark", new Vector3(0, .08f, .22f), new Vector3(.50f, .62f, .50f), new Vector3(72, 0, 0));
                    Part(gun, "Launcher lip", "Cylinder", "Yellow", new Vector3(0, .19f, .53f), new Vector3(.59f, .16f, .59f), new Vector3(72, 0, 0));
                    Part(gun, "Barrel bore", "Cylinder", "Ink", new Vector3(0, .22f, .61f), new Vector3(.42f, .018f, .42f), new Vector3(72, 0, 0));
                    Part(gun, "Loaded orb", "Orb", "Gem", new Vector3(0, .23f, .65f), new Vector3(.22f, .22f, .22f));
                }
                var leg = Pivot(root.transform, "Leg" + suffix, new Vector3(side * .25f, .62f, 0));
                Part(leg, "Leg joint", "Cylinder", "Ink", new Vector3(0, -.14f, 0), new Vector3(.24f, .30f, .24f));
                Part(leg, "Boot", "Bevel", "Plum", new Vector3(0, -.42f, .12f), new Vector3(.43f, .35f, .62f));
                Part(leg, "Toe cap", "Bevel", "Yellow", new Vector3(0, -.41f, .40f), new Vector3(.39f, .20f, .13f));
                Part(leg, "Sole", "Bevel", "Ink", new Vector3(0, -.58f, .11f), new Vector3(.45f, .09f, .65f));
            }
            Save(root, "Models/Pip");
        }

        static void BuildEnvironment()
        {
            var pickupGem = new GameObject("PickupGem");
            Part(pickupGem.transform, "Rift crystal", "Gem", "Gem", Vector3.zero, new Vector3(.58f, .85f, .58f));
            Part(pickupGem.transform, "Crystal glint", "Gem", "GemLight", new Vector3(-.09f, .19f, .14f), new Vector3(.13f, .27f, .08f), new Vector3(0, -32f, 0));
            Save(pickupGem, "Environment/PickupGem");
            var powerCell = new GameObject("PowerCell");
            Part(powerCell.transform, "Power cube housing", "Bevel", "Yellow", Vector3.zero, new Vector3(.65f, .65f, .65f));
            Part(powerCell.transform, "Power cube base", "Bevel", "Ink", new Vector3(0, -.28f, 0), new Vector3(.67f, .13f, .67f));
            Part(powerCell.transform, "Power cube lid", "Bevel", "Ivory", new Vector3(0, .28f, 0), new Vector3(.67f, .13f, .67f));
            Part(powerCell.transform, "Front panel", "Bevel", "Ink", new Vector3(0, 0, .33f), new Vector3(.45f, .41f, .045f));
            Part(powerCell.transform, "Energy face", "Gem", "CyanGlow", new Vector3(0, 0, .365f), new Vector3(.24f, .31f, .06f));
            Part(powerCell.transform, "Back panel", "Bevel", "Ink", new Vector3(0, 0, -.33f), new Vector3(.45f, .41f, .045f));
            Part(powerCell.transform, "Rear energy face", "Gem", "CyanGlow", new Vector3(0, 0, -.365f), new Vector3(.24f, .31f, .06f));
            Save(powerCell, "Environment/PowerCell");
            var shield = new GameObject("Shield");
            Part(shield.transform, "Protection bubble", "Orb", "Shield", new Vector3(0, 1.0f, 0), new Vector3(1.8f, 2.2f, 1.8f));
            var shieldRenderer = shield.GetComponentInChildren<MeshRenderer>();
            shieldRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shieldRenderer.receiveShadows = false;
            Save(shield, "Environment/Shield");
            var root = new GameObject("Podium");
            Part(root.transform, "Base", "Cylinder", "DeepInk", new Vector3(0, -.16f, 0), new Vector3(3.5f, .36f, 3.5f));
            Part(root.transform, "Trim", "Cylinder", "CyanGlow", new Vector3(0, -.055f, 0), new Vector3(3.43f, .08f, 3.43f));
            Part(root.transform, "Top", "Cylinder", "Ink", new Vector3(0, -.005f, 0), new Vector3(3.3f, .08f, 3.3f));
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60 * Mathf.Deg2Rad;
                Part(root.transform, "Deck light", "Bevel", "CyanGlow", new Vector3(Mathf.Sin(a) * 1.38f, .043f, Mathf.Cos(a) * 1.38f), new Vector3(.34f, .025f, .10f), new Vector3(0, i * 60, 0));
            }
            Save(root, "Environment/Podium");
            root = new GameObject("GemWell");
            Part(root.transform, "Foundation", "Hex", "StoneDark", new Vector3(0, .10f, 0), new Vector3(3.0f, .22f, 3.0f));
            Part(root.transform, "Metal deck", "Hex", "Ink", new Vector3(0, .21f, 0), new Vector3(2.5f, .13f, 2.5f));
            Part(root.transform, "Outer portal", "Ring", "Plum", new Vector3(0, .29f, 0), new Vector3(2.06f, 1, 2.06f));
            Part(root.transform, "Portal light", "Ring", "GemLight", new Vector3(0, .31f, 0), new Vector3(1.66f, 1, 1.66f));
            Part(root.transform, "Crystal", "Gem", "Gem", new Vector3(0, .75f, 0), new Vector3(.56f, .91f, .56f), new Vector3(0, 25, 13));
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120 * Mathf.Deg2Rad;
                Part(root.transform, "Crystal shard", "Gem", "GemLight", new Vector3(Mathf.Sin(a) * .56f, .47f, Mathf.Cos(a) * .56f), new Vector3(.27f, .58f, .27f), new Vector3(15, i * 120, -15));
            }
            Save(root, "Environment/GemWell");
            root = new GameObject("Rock");
            Part(root.transform, "Rock core", "Orb", "Stone", new Vector3(0, .38f, 0), new Vector3(1.3f, .95f, 1), new Vector3(13, 18, 11));
            Part(root.transform, "Rock chip", "Orb", "StoneTop", new Vector3(.40f, .22f, .22f), new Vector3(.55f, .51f, .57f), new Vector3(10, 20, 25));
            Save(root, "Environment/Rock");
            root = new GameObject("Cactus");
            Part(root.transform, "Trunk", "Cylinder", "Grass", new Vector3(0, .78f, 0), new Vector3(.43f, 1.50f, .43f));
            Part(root.transform, "Crown", "Orb", "GrassLight", new Vector3(0, 1.51f, 0), new Vector3(.43f, .35f, .43f));
            for (int i = -1; i <= 1; i += 2)
            {
                Part(root.transform, "Arm", "Cylinder", "Grass", new Vector3(i * .26f, .74f + i * .12f, 0), new Vector3(.27f, .61f, .27f), new Vector3(0, 0, 90));
                Part(root.transform, "Finger", "Cylinder", "Grass", new Vector3(i * .53f, .96f + i * .12f, 0), new Vector3(.27f, .49f, .27f));
                Part(root.transform, "Finger crown", "Orb", "GrassLight", new Vector3(i * .53f, 1.20f + i * .12f, 0), new Vector3(.27f, .22f, .27f));
            }
            Part(root.transform, "Flower", "Gem", "Coral", new Vector3(.03f, 1.78f, 0), new Vector3(.26f, .31f, .24f));
            Save(root, "Environment/Cactus");
            root = new GameObject("Grass");
            for (int i = 0; i < 7; i++)
            {
                float a = i * 137.5f * Mathf.Deg2Rad;
                Part(root.transform, "Blade", "Cone", i % 2 == 0 ? "Grass" : "GrassLight", new Vector3(Mathf.Sin(a) * .19f, .19f, Mathf.Cos(a) * .19f), new Vector3(.13f, .37f + i % 3 * .09f, .10f), new Vector3(Mathf.Sin(a) * 22, i * 137.5f, Mathf.Cos(a) * 22));
            }
            Save(root, "Environment/Grass");
            root = new GameObject("Pylon");
            Part(root.transform, "Foot", "Bevel", "StoneDark", new Vector3(0, .16f, 0), new Vector3(.90f, .34f, .90f));
            Part(root.transform, "Post", "Bevel", "Ink", new Vector3(0, .95f, 0), new Vector3(.43f, 1.4f, .43f));
            Part(root.transform, "Lamp base", "Hex", "Gold", new Vector3(0, 1.64f, 0), new Vector3(.69f, .22f, .69f));
            Part(root.transform, "Lamp", "Gem", "CyanGlow", new Vector3(0, 1.98f, 0), new Vector3(.41f, .59f, .41f));
            Save(root, "Environment/Pylon");
        }

        static Transform Pivot(Transform parent, string name, Vector3 position)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            return go.transform;
        }

        static void Part(Transform parent, string name, string mesh, string material, Vector3 position, Vector3 scale, Vector3 rotation = default)
        {
            var part = Pivot(parent, name, position);
            part.localScale = scale;
            part.localEulerAngles = rotation;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = Meshes[mesh];
            var renderer = part.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Materials[material];
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        static void Save(GameObject root, string relativePath)
        {
            PrefabUtility.SaveAsPrefabAsset(root, Resources + "/" + relativePath + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
        }

        static void Material(string name, string hex, float metallic = 0, float emission = 0)
        {
            string path = Resources + "/Materials/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("Universal Render Pipeline/Lit shader is required.");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else material.shader = shader;
            ColorUtility.TryParseHtmlString("#" + hex, out Color color);
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .24f);
            material.SetFloat("_Metallic", metallic);
            if (emission > 0)
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * emission);
            }
            else
            {
                material.DisableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", Color.black);
            }
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            Materials[name] = material;
        }

        static void ShieldMaterial()
        {
            Material("Shield", "8EFEF0", 0, .7f);
            var material = Materials["Shield"];
            Color color = material.GetColor("_BaseColor");
            color.a = .13f;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
            EditorUtility.SetDirty(material);
        }

        static void Mesh(string name, Mesh generated)
        {
            generated.name = name;
            string path = Art + "/Meshes/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                Meshes[name] = generated;
            }
            else
            {
                EditorUtility.CopySerialized(generated, existing);
                UnityEngine.Object.DestroyImmediate(generated);
                EditorUtility.SetDirty(existing);
                Meshes[name] = existing;
            }
        }

        // Beveled cubes use independent flat-shaded polygons, including triangular corner facets.
        static Mesh BevelBox(float bevel)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            float c = .5f - bevel;
            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                var polygon = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    var v = Vector3.zero;
                    v[axis] = sign * .5f;
                    v[(axis + 1) % 3] = (i == 0 || i == 3) ? -c : c;
                    v[(axis + 2) % 3] = i < 2 ? -c : c;
                    polygon[i] = v;
                }
                Face(vertices, triangles, polygon);
            }
            for (int edgeAxis = 0; edgeAxis < 3; edgeAxis++)
            for (int a = -1; a <= 1; a += 2)
            for (int b = -1; b <= 1; b += 2)
            {
                int ax = (edgeAxis + 1) % 3, bx = (edgeAxis + 2) % 3;
                var p = new Vector3[4];
                for (int k = 0; k < 4; k++)
                {
                    p[k][edgeAxis] = k < 2 ? -c : c;
                    p[k][ax] = a * ((k == 0 || k == 3) ? .5f : c);
                    p[k][bx] = b * ((k == 0 || k == 3) ? c : .5f);
                }
                Face(vertices, triangles, p);
            }
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
                Face(vertices, triangles, new[] { new Vector3(x * .5f, y * c, z * c), new Vector3(x * c, y * .5f, z * c), new Vector3(x * c, y * c, z * .5f) });
            return Finish(vertices, triangles);
        }

        static Mesh Cylinder(int sides, float bottomRadius, float topRadius)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var bottom = new Vector3[sides];
            var top = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides;
                bottom[i] = new Vector3(Mathf.Cos(a) * .5f * bottomRadius, -.5f, Mathf.Sin(a) * .5f * bottomRadius);
                top[i] = new Vector3(Mathf.Cos(a) * .5f * topRadius, .5f, Mathf.Sin(a) * .5f * topRadius);
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                Face(vertices, triangles, new[] { bottom[i], bottom[j], top[j], top[i] });
            }
            Face(vertices, triangles, bottom);
            Face(vertices, triangles, top);
            return Finish(vertices, triangles);
        }

        static Mesh Ellipsoid(int sides, int bands)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int band = 0; band < bands; band++)
            for (int i = 0; i < sides; i++)
            {
                float lat1 = Mathf.PI * band / bands, lat2 = Mathf.PI * (band + 1) / bands;
                float a1 = i * Mathf.PI * 2 / sides, a2 = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 Point(float lat, float a) => new Vector3(Mathf.Sin(lat) * Mathf.Cos(a), Mathf.Cos(lat), Mathf.Sin(lat) * Mathf.Sin(a)) * .5f;
                var p1 = Point(lat1, a1); var p2 = Point(lat1, a2); var p3 = Point(lat2, a2); var p4 = Point(lat2, a1);
                if (band == 0) Face(vertices, triangles, new[] { p1, p3, p4 });
                else if (band == bands - 1) Face(vertices, triangles, new[] { p1, p2, p3 });
                else Face(vertices, triangles, new[] { p1, p2, p3, p4 });
            }
            return Finish(vertices, triangles);
        }

        static Mesh Crystal()
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3, b = (i + 1) * Mathf.PI / 3;
                Vector3 p = new Vector3(Mathf.Cos(a) * .5f, .07f, Mathf.Sin(a) * .5f);
                Vector3 q = new Vector3(Mathf.Cos(b) * .5f, .07f, Mathf.Sin(b) * .5f);
                Face(vertices, triangles, new[] { p, q, Vector3.up * .5f });
                Face(vertices, triangles, new[] { q, p, Vector3.down * .5f });
            }
            return Finish(vertices, triangles);
        }

        static Mesh Ring(int sides, float inner)
        {
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            for (int i = 0; i < sides; i++)
            {
                float a = i * Mathf.PI * 2 / sides, b = (i + 1) * Mathf.PI * 2 / sides;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .5f;
                Vector3 q = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b)) * .5f;
                int start = vertices.Count;
                vertices.Add(p); vertices.Add(p * inner); vertices.Add(q * inner); vertices.Add(q);
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            return Finish(vertices, triangles);
        }

        static void Face(List<Vector3> vertices, List<int> triangles, Vector3[] polygon)
        {
            Vector3 center = Vector3.zero;
            foreach (var v in polygon) center += v;
            center /= polygon.Length;
            bool reverse = Vector3.Dot(Vector3.Cross(polygon[1] - polygon[0], polygon[2] - polygon[0]), center) < 0;
            int offset = vertices.Count;
            vertices.AddRange(polygon);
            for (int i = 1; i < polygon.Length - 1; i++)
            {
                triangles.Add(offset);
                triangles.Add(offset + (reverse ? i + 1 : i));
                triangles.Add(offset + (reverse ? i : i + 1));
            }
        }

        static Mesh Finish(List<Vector3> vertices, List<int> triangles)
        {
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
