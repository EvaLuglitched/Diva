using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using DigiPhant;
using Diva;
using StudentStarter.Savannah;
using Kind = DivaTextures.Kind;

/// <summary>
/// Builds the Diva shooting-game layer around the instructor's savannah course: a detailed town
/// (Blender kit in Models/Town), plaza props, tech arches, targets, path lights, lighting and HUD.
/// Everything goes under one root, "Diva Game Layer", beside (never inside) the course root, so the
/// course, its start/finish/path and the four landmark tasks keep their positions. The course is only
/// recoloured. Rebuilding replaces only the root. Layout is deterministic.
///
/// Value layering, back to front: cool, darker town and skyline -> mid-value plaza -> bright warm
/// path with cyan edge lights -> saturated glowing targets and a light on the elephant.
/// </summary>
public static class DivaGameBuilder
{
    public const string RootName = "Diva Game Layer";
    public const string TeamScene = "Assets/DigiPhant/Scenes/DigiPhant.unity";
    const string Folder = "Assets/DivaGame";
    const string MaterialFolder = Folder + "/Materials";
    const string TownFolder = Folder + "/Models/Town";
    const string Kenney = Folder + "/ThirdParty/Kenney";
    const float PathHalfWidth = 2.1f;
    const float StageHalf = 30;
    const float StreetInner = 32.4f, StreetOuter = 38.6f, BuildingFront = 41;

    static readonly Dictionary<string, Material> town = new Dictionary<string, Material>();
    static Material beam, sky, pathLight, landingLight, cable, bulb, asphalt, sidewalk, curb, cloud, flameOuter, flameCore;
    static DivaModelSlots slots;

    // ---------- Menus ----------

    [MenuItem("Diva/Game/Build Game Layer in Open Scene")]
    public static void BuildInOpenScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Stop Play first.");
        var scene = EditorSceneManager.GetActiveScene();
        Build(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        // The project's GPU Resident Drawer keeps drawing objects a script destroyed until the scene reloads.
        EditorSceneManager.OpenScene(scene.path);
    }

    /// <summary>Command line: opens the team scene, sets the travel radius to 30, builds and saves.</summary>
    public static void BuildInTeamScene()
    {
        var scene = EditorSceneManager.OpenScene(TeamScene);
        var locomotion = UnityEngine.Object.FindAnyObjectByType<DigiPhantLocomotion>();
        if (locomotion && locomotion.stageRadius < SavannahCourseGenerator.RequiredTravelRadius)
        {
            Undo.RecordObject(locomotion, "Travel radius for the savannah course");
            locomotion.stageRadius = SavannahCourseGenerator.RequiredTravelRadius;
            Debug.Log("DIVA_STAGE_RADIUS: " + locomotion.stageRadius);
        }
        Build(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    public static GameObject Build(UnityEngine.SceneManagement.Scene scene)
    {
        var course = scene.GetRootGameObjects().FirstOrDefault(g => g.name == SavannahCourseGenerator.RootName);
        if (!course) throw new Exception("No '" + SavannahCourseGenerator.RootName + "' in this scene. Inject the savannah course first.");
        var controller = UnityEngine.Object.FindAnyObjectByType<DigiPhantController>();
        var locomotion = controller ? controller.GetComponent<DigiPhantLocomotion>() : null;
        if (!locomotion || !locomotion.travelRoot) throw new Exception("No DigiPhant locomotion with a travel root in this scene.");

        PrepareAssets();
        CaptureRocketPitch(scene);
        foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName)) UnityEngine.Object.DestroyImmediate(old);
        var root = new GameObject(RootName);
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        // Share the course's frame, so route coordinates from the generator apply directly.
        root.transform.SetPositionAndRotation(course.transform.position, course.transform.rotation);

        var layout = new Layout(course.transform);
        BuildStreets(Group(root.transform, "Streets"));
        BuildTown(Group(root.transform, "Town"));
        BuildStreetDressing(Group(root.transform, "Street dressing"));
        BuildPerimeter(Group(root.transform, "Course perimeter"));
        BuildPlaza(Group(root.transform, "Plaza props"), layout);
        var gates = BuildGates(Group(root.transform, "Arches over the path"), layout);
        var targets = BuildTargets(Group(root.transform, "Targets"), layout, gates);
        BuildPathLights(Group(root.transform, "Path lights"), layout);
        BuildSky(Group(root.transform, "Sky clouds"));
        BuildRocket(root.transform);
        RecolourCourse(course.transform);
        ReplaceCourseTrees(course.transform, Group(root.transform, "Course tree replacements"));
        BuildLook(root.transform);
        BuildHeroLight(root.transform, locomotion);
        BuildGameplay(root, controller, locomotion, course.transform);
        Debug.Log($"DIVA_GAME_LAYER_BUILT: {scene.path} | {targets} targets | {gates.Count} gates | " +
                  $"{root.GetComponentsInChildren<Renderer>().Length} renderers | path clearance {Layout.Clearance} units");
        return root;
    }

    // ---------- Layout helpers (course-local coordinates) ----------

    class Layout
    {
        public const float Clearance = PathHalfWidth + 2.4f; // nothing at ground level closer to the path centre line
        public readonly Vector3[] route = SavannahCourseGenerator.Route;
        public readonly List<Vector3> trees = new List<Vector3>();
        public readonly List<Vector3> landmarks = new List<Vector3>();
        public readonly float length;

        public Layout(Transform course)
        {
            foreach (Transform child in course)
            {
                if (child.name.StartsWith("Acacia trees")) foreach (Transform tree in child) trees.Add(tree.localPosition);
                if (child.name.StartsWith("Landmarks")) foreach (Transform mark in child) landmarks.Add(mark.localPosition);
            }
            for (int i = 1; i < route.Length; i++) length += Vector3.Distance(route[i - 1], route[i]);
        }

        public Vector3 Nearest(Vector3 p)
        {
            Vector3 best = route[0];
            for (int i = 1; i < route.Length; i++)
            {
                Vector3 a = route[i - 1], ab = route[i] - a;
                Vector3 q = a + ab * Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                if (Vector3.Distance(p, q) < Vector3.Distance(p, best)) best = q;
            }
            return best;
        }

        public float DistanceToRoute(Vector3 p) => Vector3.Distance(p, Nearest(p));

        /// <summary>Point on the route at a fraction of its length, its direction, and the left-hand normal.</summary>
        public (Vector3 point, Vector3 forward, Vector3 side) At(float fraction)
        {
            float distance = Mathf.Clamp01(fraction) * length;
            for (int i = 1; i < route.Length; i++)
            {
                float segment = Vector3.Distance(route[i - 1], route[i]);
                Vector3 forward = (route[i] - route[i - 1]).normalized;
                if (distance <= segment || i == route.Length - 1)
                    return (Vector3.Lerp(route[i - 1], route[i], Mathf.Clamp01(distance / segment)), forward, Vector3.Cross(Vector3.up, forward));
                distance -= segment;
            }
            throw new InvalidOperationException();
        }

        public bool Clear(Vector3 p, float pathClearance, float treeClearance, float landmarkClearance) =>
            DistanceToRoute(p) >= pathClearance &&
            trees.All(t => Vector3.Distance(Flat(t), Flat(p)) >= treeClearance) &&
            landmarks.All(l => Vector3.Distance(Flat(l), Flat(p)) >= landmarkClearance);
    }

    static uint Hash(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h;
        }
    }

    /// <summary>Rotation that turns a kit model's front (+Z) towards a direction on the ground.</summary>
    static Quaternion Facing(Vector3 direction) => Quaternion.LookRotation(Flat(direction).normalized);

    // ---------- Town ----------

    static readonly (string name, float w, float d)[] Houses =
        { ("House_A", 7, 7), ("House_B", 6, 7), ("House_C", 8, 8), ("House_D", 6, 6), ("House_E", 7, 7), ("House_F", 6.5f, 6.5f) };

    static void BuildStreets(Transform parent)
    {
        // Dark asphalt beyond the course, kerbed light sidewalks either side of a ring street.
        WorldBox(parent, "Asphalt", asphalt, new Vector3(0, -.08f, 0), new Vector3(180, .1f, 180));
        for (int side = 0; side < 4; side++)
        {
            var q = Quaternion.Euler(0, side * 90, 0);
            float innerW = StreetInner - StageHalf, outerW = BuildingFront + 12 - StreetOuter;
            WorldBox(parent, "Inner sidewalk", sidewalk, q * new Vector3(0, .02f, StageHalf + innerW / 2), new Vector3(2 * StreetInner, .14f, innerW), q);
            WorldBox(parent, "Outer sidewalk", sidewalk, q * new Vector3(0, .02f, StreetOuter + outerW / 2), new Vector3(2 * (BuildingFront + 12), .14f, outerW), q);
            WorldBox(parent, "Kerb", curb, q * new Vector3(0, .05f, StreetInner), new Vector3(2 * StreetInner, .2f, .25f), q);
            WorldBox(parent, "Kerb", curb, q * new Vector3(0, .05f, StreetOuter), new Vector3(2 * StreetOuter, .2f, .25f), q);
            for (float x = -StreetOuter + 3; x < StreetOuter - 3; x += 6)  // dashed centre line
                WorldBox(parent, "Lane mark", sidewalk, q * new Vector3(x, -.025f, (StreetInner + StreetOuter) / 2), new Vector3(2.5f, .02f, .18f), q);
        }
    }

    static void BuildTown(Transform parent)
    {
        // Front row: continuous townhouses facing the course, with the odd alley.
        var row = Group(parent, "Townhouses");
        for (int side = 0; side < 4; side++)
        {
            var q = Quaternion.Euler(0, side * 90, 0);
            float x = -36;
            for (int i = 0; ; i++)
            {
                uint h = Hash(side * 31 + 3, i);
                var (name, w, d) = Houses[h % Houses.Length];
                if (x + w > 36) break;
                var pos = q * new Vector3(x + w / 2, 0, BuildingFront + d / 2);
                PlaceModel(row, name, HouseSlot((int)(h % 97)), pos, q * Quaternion.Euler(0, 180, 0), 12);
                x += w + ((h >> 8) % 6 == 0 ? 2.8f : .25f);
            }
        }
        // Back row: taller tech blocks for a layered skyline.
        var skyline = Group(parent, "Skyline");
        for (int side = 0; side < 4; side++)
        {
            var q = Quaternion.Euler(0, side * 90, 0);
            for (int i = 0; i < 6; i++)
            {
                uint h = Hash(side * 17 + 5, i);
                string name = (h % 3) switch { 0 => "Tech_A", 1 => "Tech_B", _ => "House_E" };
                float x = -36 + i * 14.4f + (h >> 4) % 3;
                var pos = q * new Vector3(x, 0, BuildingFront + 16);
                var model = PlaceModel(skyline, name, slots.techBuilding, pos, q * Quaternion.Euler(0, 180, 0), 18);
                model.transform.localScale *= 1.15f + (h >> 7) % 3 * .12f;
            }
        }
        // Corner landmarks facing the centre.
        var corners = Group(parent, "Corner landmarks");
        string[] cornerModels = { "Clock_Tower", "Tech_A", "Tech_B", "Clock_Tower" };
        for (int c = 0; c < 4; c++)
        {
            var pos = new Vector3(c % 2 == 0 ? -47 : 47, 0, c < 2 ? -47 : 47);
            var model = PlaceModel(corners, cornerModels[c], cornerModels[c] == "Clock_Tower" ? slots.landmark : slots.techBuilding,
                pos, Facing(-pos), 24);
            if (cornerModels[c] != "Clock_Tower") model.transform.localScale *= 1.4f;
        }
    }

    static GameObject HouseSlot(int i) => slots.houses != null && slots.houses.Length > 0 ? slots.houses[i % slots.houses.Length] : null;

    static void BuildStreetDressing(Transform parent)
    {
        for (int side = 0; side < 4; side++)
        {
            var q = Quaternion.Euler(0, side * 90, 0);
            // Street lamps on the inner sidewalk, heads over the street.
            for (float x = -28; x <= 28; x += 8)
                PlaceModel(parent, "Street_Lamp", null, q * new Vector3(x, .09f, StreetInner - .6f), q, 0);
            // Utility poles on the outer sidewalk with warm string lights between them.
            Vector3? previous = null;
            for (float x = -34; x <= 34; x += 11.33f)
            {
                var pos = q * new Vector3(x, .09f, StreetOuter + .7f);
                PlaceModel(parent, "Cable_Pole", null, pos, q, 0);
                var top = pos + Vector3.up * 6.75f;
                if (previous.HasValue) StringLights(parent, previous.Value, top);
                previous = top;
            }
            // A banner over the inner sidewalk, facing the course.
            PlaceModel(parent, side % 2 == 0 ? "Banner_Welcome" : "Banner_Safari", null, q * new Vector3(0, .09f, StreetInner - 1.4f), q * Quaternion.Euler(0, 180, 0), 0);
        }
    }

    static void StringLights(Transform parent, Vector3 a, Vector3 b)
    {
        Vector3 mid = (a + b) / 2 + Vector3.down * .9f;
        for (int seg = 0; seg < 2; seg++)
        {
            Vector3 p0 = seg == 0 ? a : mid, p1 = seg == 0 ? mid : b;
            var wire = Part(parent, "Cable", PrimitiveType.Cylinder, cable, (p0 + p1) / 2, new Vector3(.03f, Vector3.Distance(p0, p1) / 2, .03f));
            wire.transform.localRotation = Quaternion.FromToRotation(Vector3.up, p1 - p0);
            wire.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
        for (int k = 1; k < 10; k++)
        {
            float t = k / 10f;
            Vector3 p = t < .5f ? Vector3.Lerp(a, mid, t * 2) : Vector3.Lerp(mid, b, t * 2 - 1);
            var light = Part(parent, "Bulb", PrimitiveType.Sphere, bulb, p + Vector3.down * .12f, Vector3.one * .16f);
            light.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    static void BuildPerimeter(Transform parent)
    {
        for (int side = 0; side < 4; side++)
        {
            var q = Quaternion.Euler(0, side * 90, 0);
            for (float x = -28; x <= 28; x += 8)
                PlaceModel(parent, "Stone_Rail", null, q * new Vector3(x, .09f, StageHalf + .9f), q * Quaternion.Euler(0, 180, 0), 0);
        }
    }

    // ---------- Course area ----------

    static void BuildPlaza(Transform parent, Layout layout)
    {
        // Same grid and hash as earlier versions, so every prop keeps its spot.
        for (int gx = -5; gx <= 5; gx++)
        for (int gz = -5; gz <= 5; gz++)
        {
            var p = new Vector3(gx * 5.2f, 0, gz * 5.2f);
            uint h = Hash(gx, gz);
            if (h % 100 > 42 || !layout.Clear(p, Layout.Clearance + 1.6f, 3.2f, 6.5f) || p.magnitude < 7) continue;
            var face = Facing(layout.Nearest(p) - p);
            switch ((h >> 4) % 4)
            {
                case 0: PlaceModel(parent, (h >> 9) % 2 == 0 ? "Billboard_A" : "Billboard_B", slots.billboard, p, face, 7.6f); break;
                case 1: PlaceModel(parent, (h >> 9) % 2 == 0 ? "Crates" : "Barrier", slots.crates, p, face * Quaternion.Euler(0, (h >> 12) % 40, 0), 2); break;
                case 2:
                    PlaceModel(parent, (h >> 9) % 2 == 0 ? "Kiosk_A" : "Kiosk_B", slots.kiosk, p, face, 2.7f);
                    PlaceModel(parent, "Bench", null, p + face * new Vector3(2.4f, 0, .6f), face, 0);
                    break;
                default: PlaceModel(parent, "Planter_Tree", slots.planter, p, face, 4.5f); break;
            }
        }
    }

    static List<float> BuildGates(Transform parent, Layout layout)
    {
        var placed = new List<float>();
        foreach (float wanted in new[] { .08f, .38f, .60f, .90f })
        {
            // Same placement as before: nudge forward until both pillars clear trees and landmarks.
            for (float f = wanted; f < wanted + .06f; f += .01f)
            {
                var (point, forward, side) = layout.At(f);
                Vector3 left = point + side * 3.6f, right = point - side * 3.6f;
                if (!layout.Clear(left, 0, 1.8f, 4.5f) || !layout.Clear(right, 0, 1.8f, 4.5f)) continue;
                PlaceModel(parent, "Neon_Gate", slots.gate, point, Quaternion.LookRotation(-forward), 8);
                placed.Add(f);
                break;
            }
        }
        return placed;
    }

    static int BuildTargets(Transform parent, Layout layout, List<float> gates)
    {
        var plan = new (float f, TargetMotion motion)[]
        {
            (.05f, TargetMotion.Static), (.13f, TargetMotion.Swing), (.18f, TargetMotion.Slide), (.27f, TargetMotion.Hover),
            (.31f, TargetMotion.Static), (.35f, TargetMotion.Swing), (.43f, TargetMotion.Slide), (.53f, TargetMotion.Hover),
            (.56f, TargetMotion.Static), (.65f, TargetMotion.Swing), (.69f, TargetMotion.Slide), (.78f, TargetMotion.Hover),
            (.82f, TargetMotion.Static), (.86f, TargetMotion.Swing), (.94f, TargetMotion.Hover), (.97f, TargetMotion.Slide),
        };
        float[] landmarkFractions = { .23f, .49f, .74f };
        int[] landmarkSides = { -1, 1, -1 }; // the generator's side offsets (-4, +4, -4)
        int built = 0;
        foreach (var (f, motion) in plan)
        {
            if (gates.Any(g => Mathf.Abs(g - f) < .015f)) continue;
            var (point, forward, side) = layout.At(f);
            int preferred = built % 2 == 0 ? 1 : -1;
            for (int i = 0; i < landmarkFractions.Length; i++)
                if (Mathf.Abs(landmarkFractions[i] - f) < .07f) preferred = -landmarkSides[i];
            Transform stand = null;
            foreach (int s in new[] { preferred, -preferred })
            {
                Vector3 p = point + side * s * 5.4f;
                if (!layout.Clear(p, PathHalfWidth + 2.5f, 2.2f, 4.5f)) continue;
                stand = Group(parent, $"Target {built + 1:00} · {(motion == TargetMotion.Hover ? "Drone" : motion.ToString())}");
                stand.localPosition = p;
                stand.localRotation = Facing(point - p);
                break;
            }
            if (!stand) continue;
            var target = stand.gameObject.AddComponent<DivaTarget>();
            target.motion = motion;
            if (motion == TargetMotion.Hover)
            {
                var visual = Group(stand, "Drone");
                visual.localPosition = new Vector3(0, 3.6f, 0);
                var drone = PlaceModel(visual, "Target_Drone", slots.targetDrone, Vector3.zero, Quaternion.identity, 1.6f, grounded: false);
                if (!slots.targetDrone) drone.transform.localScale *= 1.4f;
                PlaceModel(stand, "Target_Stand", null, Vector3.zero, Quaternion.identity, 0).transform.localScale = new Vector3(1.4f, .3f, 1.4f);
                target.visual = visual; target.amplitude = .45f; target.speed = 1.2f; target.points = 30;
                target.glow = TargetLight(visual, new Vector3(0, -.6f, -.4f));
            }
            else
            {
                PlaceModel(stand, "Target_Stand", null, Vector3.zero, Quaternion.identity, 0);
                var hinge = Group(stand, "Board hinge");
                hinge.localPosition = new Vector3(0, 1.15f, 0);
                PlaceModel(hinge, "Target_Board", slots.targetBoard, Vector3.zero, Quaternion.identity, 2);
                target.visual = hinge;
                target.glow = TargetLight(hinge, new Vector3(0, .25f, -.6f));   // behind and low: glows on the ground, keeps the red/white face readable
                target.amplitude = motion == TargetMotion.Swing ? 22 : 1.3f;
                target.speed = motion == TargetMotion.Slide ? 1.1f : 1.6f;
                target.points = motion == TargetMotion.Static ? 10 : motion == TargetMotion.Swing ? 15 : 20;
            }
            built++;
        }
        return built;
    }

    /// <summary>Warm neon glow around a target, so it lights its surroundings and reads from a distance.</summary>
    static Light TargetLight(Transform parent, Vector3 position)
    {
        var go = new GameObject("Neon glow");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1, .38f, .16f);
        light.range = 5.5f;
        light.intensity = 3.5f;
        light.shadows = LightShadows.None;
        return light;
    }

    static void BuildPathLights(Transform parent, Layout layout)
    {
        // Cyan edge strips and runway dots make the route the brightest line in the scene.
        var r = layout.route;
        for (int i = 1; i < r.Length; i++)
        {
            Vector3 a = r[i - 1], b = r[i], dir = (b - a).normalized, side = Vector3.Cross(Vector3.up, dir);
            float len = Vector3.Distance(a, b);
            for (int s = -1; s <= 1; s += 2)
            {
                var strip = Part(parent, "Edge light", PrimitiveType.Cube, pathLight, (a + b) / 2 + side * s * (PathHalfWidth + .12f) + Vector3.up * .05f,
                    new Vector3(.14f, .05f, Mathf.Max(.1f, len - 1.2f)));
                strip.transform.localRotation = Quaternion.LookRotation(dir);
                strip.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
                for (float t = 1.5f; t < len - 1; t += 3)
                    Part(parent, "Runway light", PrimitiveType.Cylinder, landingLight, a + dir * t + side * s * (PathHalfWidth + .45f) + Vector3.up * .06f,
                        new Vector3(.22f, .04f, .22f)).GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }
        }
    }

    static void BuildSky(Transform parent)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Kenney + "/Models/cloud.fbx");
        for (int i = 0; i < 10; i++)
        {
            uint h = Hash(i, 7);
            // A ring just above the rooftops around the course: in the game camera's sky band, in front of the rocket's orbit.
            float angle = i * 36 + h % 20, radius = 48 + (h >> 5) % 28;
            var anchor = Group(parent, "Cloud " + (i + 1));
            anchor.localPosition = Quaternion.Euler(0, angle, 0) * new Vector3(0, 30 + (h >> 9) % 9, radius);
            anchor.localRotation = Quaternion.Euler(0, (h >> 3) % 360, 0);
            anchor.localScale = new Vector3(14 + (h >> 11) % 6, 1.6f, 9 + (h >> 15) % 4);
            if (slots.clouds != null && slots.clouds.Length > 0)
            {
                // Downloaded clouds keep their own materials; size them by width rather than the stand-in's scale.
                var prefab = slots.clouds[(int)((h >> 17) % slots.clouds.Length)];
                anchor.localScale = Vector3.one;
                if (prefab) FitWidth(prefab, anchor, 14 + (h >> 11) % 7, i);
            }
            else if (model)
            {
                var c = (GameObject)PrefabUtility.InstantiatePrefab(model, anchor);
                foreach (var rr in c.GetComponentsInChildren<Renderer>())
                {
                    rr.sharedMaterials = rr.sharedMaterials.Select(_ => cloud).ToArray();
                    rr.shadowCastingMode = ShadowCastingMode.Off;
                }
            }
            var motion = anchor.gameObject.AddComponent<DivaFloat>();
            motion.bobHeight = 1.2f + (h >> 19) % 3 * .4f; motion.bobSpeed = .25f + (h >> 21) % 3 * .05f;
            motion.driftRadius = 4 + (h >> 23) % 4; motion.driftSpeed = .04f + (h >> 25) % 3 * .015f;
        }
    }

    /// <summary>Instantiates a prefab scaled to a width; for a pack of several meshes, keeps only mesh number `pick`.</summary>
    static void FitWidth(GameObject prefab, Transform anchor, float width, int pick = 0)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
        var meshes = instance.GetComponentsInChildren<MeshRenderer>(true);
        if (meshes.Length > 1)
        {
            var keep = meshes[pick % meshes.Length];
            foreach (var m in meshes) if (m != keep) m.gameObject.SetActive(false);
            keep.transform.localPosition = Vector3.zero;
        }
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        instance.transform.localScale *= width / Mathf.Max(.01f, Mathf.Max(b.size.x, b.size.z));
        b = renderers[0].bounds;
        foreach (var r in renderers) b.Encapsulate(r.bounds);
        instance.transform.position += anchor.position - b.center;
        foreach (var r in renderers) r.shadowCastingMode = ShadowCastingMode.Off;
    }

    /// <summary>A giant rocket circling high above the centre of the map, nose at the kept pitch, exhaust trailing behind.</summary>
    static void BuildRocket(Transform root)
    {
        float length = slots.rocketLength, k = length / 22;   // effects were tuned for a 22 m rocket
        var hover = Group(root, "Giant rocket");
        var orbit = hover.gameObject.AddComponent<DivaOrbit>();
        orbit.centre = root.position;
        orbit.radius = slots.rocketOrbitRadius;
        orbit.speed = slots.rocketOrbitSpeed;
        orbit.height = slots.rocketOrbitHeight;   // with the wide orbit: above the skyline, in the game camera's view
        orbit.startAngle = 70;   // starts ahead of the elephant at the start line, then flies across
        var body = Group(hover, "Rocket body");
        GameObject model;
        if (slots.rocket)
        {
            model = (GameObject)PrefabUtility.InstantiatePrefab(slots.rocket, body);
            model.transform.localRotation = Quaternion.FromToRotation(slots.rocketNoseAxis.normalized, Vector3.forward);
        }
        else
        {
            model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(TownFolder + "/Rocket.fbx"), body);
            model.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.forward);   // kit rocket: nose up
        }
        // Measure with the body axis-aligned in world space (bounds are world AABBs), then tilt.
        body.rotation = Quaternion.identity;
        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds Measure() { var b = renderers[0].bounds; foreach (var r in renderers) b.Encapsulate(r.bounds); return b; }
            model.transform.localScale *= length / Mathf.Max(.01f, Measure().size.z);
            model.transform.position += body.position - Measure().center;
            foreach (var r in renderers) r.shadowCastingMode = ShadowCastingMode.Off;
        }
        body.localRotation = Quaternion.Euler(-slots.rocketPitch, 0, 0);   // nose (+Z) tipped up by the pitch
        float tail = -length / 2;
        Part(body, "Exhaust flame", PrimitiveType.Sphere, flameOuter, new Vector3(0, 0, tail - 2.2f * k), new Vector3(2.2f, 2.2f, 5.5f) * k)
            .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        Part(body, "Exhaust core", PrimitiveType.Sphere, flameCore, new Vector3(0, 0, tail - 1.2f * k), new Vector3(1.2f, 1.2f, 3f) * k)
            .GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        var glow = new GameObject("Exhaust light").AddComponent<Light>();
        glow.transform.SetParent(body, false);
        glow.transform.localPosition = new Vector3(0, 0, tail - 3 * k);
        glow.type = LightType.Point; glow.color = new Color(1, .55f, .35f); glow.range = 25 * k; glow.intensity = 6; glow.shadows = LightShadows.None;
        orbit.Place(0);
    }

    /// <summary>
    /// Reads the nose angle of the rocket currently in the scene (including any hand rotation of the
    /// model or body) into the slots, so a rebuild keeps it.
    /// </summary>
    static void CaptureRocketPitch(UnityEngine.SceneManagement.Scene scene)
    {
        var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RootName);
        var hover = old ? old.transform.Find("Giant rocket") : null;
        if (hover && !hover.GetComponent<DivaOrbit>()) return;
        var body = hover ? hover.Find("Rocket body") : null;
        var model = body ? body.Cast<Transform>().FirstOrDefault(t => t.GetComponentInChildren<Renderer>() && !t.name.StartsWith("Exhaust")) : null;
        if (!model) return;
        bool fab = slots.rocket && PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject) == slots.rocket;
        Vector3 noseAxis = fab ? slots.rocketNoseAxis.normalized : Vector3.up;
        Vector3 nose = Quaternion.Inverse(hover.rotation) * (model.rotation * noseAxis);
        float pitch = Mathf.Round(Mathf.Asin(Mathf.Clamp(nose.normalized.y, -1, 1)) * Mathf.Rad2Deg * 10) / 10;
        if (Mathf.Abs(pitch - slots.rocketPitch) < .05f) return;
        slots.rocketPitch = pitch;
        EditorUtility.SetDirty(slots);
        AssetDatabase.SaveAssets();
        Debug.Log("DIVA_ROCKET_PITCH kept from scene: " + pitch);
    }

    const string FabRocket = Folder + "/ThirdParty/Fab/StylizedRocket/Stylized Rocket.fbx";
    const string FabClouds = Folder + "/ThirdParty/Fab/StylizedClouds07/stylized_clouds_pack_vol_07.fbx";

    /// <summary>Puts the downloaded Fab rocket and clouds into the model slots (when present) and rebuilds.</summary>
    [MenuItem("Diva/Game/Use Fab Rocket and Clouds")]
    public static void UseFabModels()
    {
        PrepareAssets();
        var rocket = AssetDatabase.LoadAssetAtPath<GameObject>(FabRocket);
        var clouds = AssetDatabase.LoadAssetAtPath<GameObject>(FabClouds);
        if (!rocket && !clouds) throw new Exception("No Fab files found; see Assets/DivaGame/ThirdParty/Fab/README.md.");
        if (rocket) { slots.rocket = rocket; slots.rocketNoseAxis = Vector3.up; }
        if (clouds) slots.clouds = new[] { clouds };
        EditorUtility.SetDirty(slots);
        AssetDatabase.SaveAssets();
        BuildInOpenScene();
    }

    /// <summary>Candy recolour of the Fab rocket (our albedo/emission from its texture) and candy clouds.</summary>
    static void PrepareFabMaterials()
    {
        string dir = Folder + "/ThirdParty/Fab/StylizedRocket/";
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "rocket_candy_albedo.png");
        if (albedo)
        {
            var normalImporter = (TextureImporter)AssetImporter.GetAtPath(dir + "rocket_norm.png");
            if (normalImporter && normalImporter.textureType != TextureImporterType.NormalMap)
            {
                normalImporter.textureType = TextureImporterType.NormalMap;
                normalImporter.SaveAndReimport();
            }
            var rocket = Lit("Diva Rocket Candy", Color.white, .55f, .1f, Color.white * 2.5f);
            rocket.SetTexture("_BaseMap", albedo);
            rocket.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "rocket_candy_emission.png"));
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(dir + "rocket_norm.png");
            if (normal) { rocket.SetTexture("_BumpMap", normal); rocket.EnableKeyword("_NORMALMAP"); }
            EditorUtility.SetDirty(rocket);
            RemapAll(FabRocket, rocket);
        }
        RemapAll(FabClouds, cloud);
    }

    /// <summary>Points every embedded material of a model at one material.</summary>
    static void RemapAll(string path, Material material)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (!importer || !material) return;
        var names = new HashSet<string>(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name));
        foreach (var key in importer.GetExternalObjectMap().Keys) if (key.type == typeof(Material)) names.Add(key.name);
        bool changed = false;
        foreach (var name in names)
        {
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
            if (importer.GetExternalObjectMap().TryGetValue(id, out var current) && current == material) continue;
            importer.AddRemap(id, material);
            changed = true;
        }
        if (changed) importer.SaveAndReimport();
    }

    // ---------- Course recolour ----------

    const string CoursePrefix = "Diva Course ";

    /// <summary>
    /// Restyles the instructor's course by swapping materials on its renderers. Geometry, positions and
    /// the route are untouched; "Diva > Game > Restore Course Colours" undoes it.
    /// </summary>
    static void RecolourCourse(Transform course)
    {
        var looks = new Dictionary<string, Func<Material>>
        {
            // Light grey blue-violet plaza, so the bright cream path and the targets read clearly on top of it.
            // The course meshes have no UVs, so they get flat colours (a texture would sample one pixel).
            ["Ochre ground"] = () => Lit(CoursePrefix + "Ochre ground", new Color(.56f, .58f, .73f), .25f),   // light grey blue-violet (lit by bright pastel ambient)
            ["Pale sand path"] = () => Lit(CoursePrefix + "Pale sand path", new Color(1, .95f, .8f), .3f),
            ["Olive foliage"] = () => Lit(CoursePrefix + "Olive foliage", new Color(1, .64f, .82f), .3f),
            ["Sage foliage"] = () => Lit(CoursePrefix + "Sage foliage", new Color(.84f, .72f, 1), .3f),
            ["Bark"] = () => Lit(CoursePrefix + "Bark", new Color(.68f, .48f, .6f), .25f),
            ["Basin stone"] = () => Lit(CoursePrefix + "Basin stone", new Color(.86f, .78f, .98f), .3f),
            ["Still blue water"] = () => Lit(CoursePrefix + "Still blue water", new Color(.5f, .95f, .92f), .9f, emission: new Color(.12f, .45f, .45f)),
            ["Terracotta markers"] = () => Lit(CoursePrefix + "Terracotta markers", new Color(1, .35f, .75f), .4f, emission: new Color(1, .25f, .7f) * 2),
            ["Ivory markers"] = () => Lit(CoursePrefix + "Ivory markers", new Color(1, .98f, .99f), .4f),
        };
        var made = new Dictionary<string, Material>();
        var originals = OriginalCourseMaterials();
        foreach (var renderer in course.GetComponentsInChildren<MeshRenderer>(true))
        {
            var materials = renderer.sharedMaterials;
            // Start from the instructor's materials, so the result never depends on what the scene holds now.
            if (originals.TryGetValue(CoursePath(course, renderer.transform), out var source) && source.Length == materials.Length)
                materials = (Material[])source.Clone();
            for (int i = 0; i < materials.Length; i++)
            {
                if (!materials[i]) continue;
                string original = materials[i].name.StartsWith(CoursePrefix) ? materials[i].name.Substring(CoursePrefix.Length) : materials[i].name;
                if (!looks.TryGetValue(original, out var make)) continue;
                if (!made.TryGetValue(original, out var material)) made[original] = material = make();
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
        }
    }

    /// <summary>The instructor's materials per course renderer, keyed by hierarchy path, from the shipped course prefab.</summary>
    static Dictionary<string, Material[]> OriginalCourseMaterials()
    {
        var map = new Dictionary<string, Material[]>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SavannahCourse/Prefabs/SavannahCourse.prefab");
        if (prefab)
            foreach (var r in prefab.GetComponentsInChildren<MeshRenderer>(true))
                map[CoursePath(prefab.transform, r.transform)] = r.sharedMaterials;
        return map;
    }

    /// <summary>Path below the course root with sibling indices, since siblings often share a name.</summary>
    static string CoursePath(Transform root, Transform t)
    {
        var parts = new List<string>();
        for (; t && t != root; t = t.parent) parts.Insert(0, t.name + "#" + t.GetSiblingIndex());
        return string.Join("/", parts);
    }

    [MenuItem("Diva/Game/Restore Course Colours")]
    public static void RestoreCourseColours()
    {
        var course = EditorSceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g => g.name == SavannahCourseGenerator.RootName);
        if (!course) return;
        var originals = OriginalCourseMaterials();
        foreach (var renderer in course.GetComponentsInChildren<MeshRenderer>(true))
            if (originals.TryGetValue(CoursePath(course.transform, renderer.transform), out var source))
                renderer.sharedMaterials = source;
        foreach (var renderer in course.GetComponentsInChildren<Renderer>(true)) renderer.enabled = true;
        EditorSceneManager.MarkSceneDirty(course.scene);
    }

    /// <summary>Course trees: hide each original (positions untouched) and place the slot prefab at the same spot.</summary>
    static void ReplaceCourseTrees(Transform course, Transform parent)
    {
        var grove = course.Cast<Transform>().FirstOrDefault(t => t.name.StartsWith("Acacia trees"));
        if (!grove) return;
        foreach (Transform tree in grove)
        {
            bool replace = slots.courseTree;
            foreach (var r in tree.GetComponentsInChildren<Renderer>(true)) r.enabled = !replace;
            if (!replace) continue;
            var anchor = Group(parent, tree.name);
            anchor.position = tree.position;
            anchor.rotation = tree.rotation;
            Slot(slots.courseTree, anchor, 4.5f);
        }
    }

    // ---------- Look ----------

    static void BuildLook(Transform root)
    {
        // Candy fantasy town: pink-lavender sky, pastel ambient, soft pink haze for depth.
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(1, .82f, .96f);
        RenderSettings.ambientEquatorColor = new Color(.96f, .8f, .92f);
        RenderSettings.ambientGroundColor = new Color(.74f, .6f, .8f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.98f, .82f, .95f);
        RenderSettings.fogStartDistance = 85;
        RenderSettings.fogEndDistance = 300;
        DynamicGI.UpdateEnvironment();
        var sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        if (sun)
        {
            Undo.RecordObject(sun, "Diva sunlight");
            sun.color = new Color(1, .93f, .96f);
            sun.intensity = 1.3f;
            sun.shadows = LightShadows.Soft;
            EditorUtility.SetDirty(sun);
        }

        string profilePath = Folder + "/Settings/Diva Post FX.asset";
        Directory.CreateDirectory(Folder + "/Settings");
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
        if (!profile)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
        }
        T Effect<T>() where T : VolumeComponent
        {
            if (!profile.TryGet(out T effect)) { effect = profile.Add<T>(true); AssetDatabase.AddObjectToAsset(effect, profile); }
            effect.active = true;
            return effect;
        }
        var bloom = Effect<Bloom>();
        bloom.threshold.Override(.95f); bloom.intensity.Override(1.2f); bloom.scatter.Override(.72f); bloom.tint.Override(new Color(1, .88f, .96f));
        Effect<Tonemapping>().mode.Override(TonemappingMode.Neutral); // ACES muddies pastels
        var colour = Effect<ColorAdjustments>();
        colour.postExposure.Override(0); colour.contrast.Override(14); colour.saturation.Override(18);
        colour.colorFilter.Override(Color.white);
        var vignette = Effect<Vignette>();
        vignette.intensity.Override(.18f); vignette.color.Override(new Color(.45f, .2f, .45f));
        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        var volume = new GameObject("Post FX volume").AddComponent<Volume>();
        volume.transform.SetParent(root, false);
        volume.isGlobal = true;
        volume.priority = 1;
        volume.sharedProfile = profile;
        foreach (var camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (camera.CompareTag("MainCamera"))
            {
                var data = camera.GetUniversalAdditionalCameraData();
                data.renderPostProcessing = true;
                EditorUtility.SetDirty(data);
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, 400);
            }
    }

    static void BuildHeroLight(Transform root, DigiPhantLocomotion locomotion)
    {
        // A soft warm key above the elephant keeps the hero readable against the busy town.
        var go = new GameObject("Elephant light");
        go.transform.SetParent(root, false);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1, .9f, .8f);
        light.range = 10;
        light.intensity = 2.5f;
        light.shadows = LightShadows.None;
        var follow = go.AddComponent<DivaFollow>();
        follow.target = locomotion.travelRoot;
        follow.offset = new Vector3(0, 6, -2.5f);
        go.transform.position = locomotion.travelRoot.position + locomotion.travelRoot.rotation * follow.offset;
    }

    static void BuildGameplay(GameObject root, DigiPhantController controller, DigiPhantLocomotion locomotion, Transform course)
    {
        var blaster = root.AddComponent<DivaLaserBlaster>();
        blaster.elephant = locomotion.travelRoot;
        blaster.muzzle = locomotion.elephantAnimator
            ? locomotion.elephantAnimator.GetComponentsInChildren<Transform>().FirstOrDefault(t => t.name == "elephant_Trunk7_bone")
            : null;
        blaster.beamMaterial = beam;
        blaster.burstSprite = AssetDatabase.LoadAssetAtPath<Sprite>(Kenney + "/Sprites/hit.png");
        blaster.shotSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Kenney + "/Sounds/blaster.ogg");
        blaster.hitSound = AssetDatabase.LoadAssetAtPath<AudioClip>(Kenney + "/Sounds/enemy_destroy.ogg");

        var sky = root.AddComponent<DivaSkyCamera>();
        sky.gameCamera = Camera.main;
        var game = root.AddComponent<DivaGameManager>();
        game.controller = controller;
        game.locomotion = locomotion;
        game.blaster = blaster;
        Transform Landmark(string prefix) => course.GetComponentsInChildren<Transform>().First(t => t.name.StartsWith(prefix));
        game.tasks = new[]
        {
            new CourseTask { label = "Eat at the feeding bush", landmark = Landmark("01 "), radius = 5.5f, check = TaskCheck.CurlTrunk, action = "eat" },
            new CourseTask { label = "Drink at the water basin", landmark = Landmark("02 "), radius = 5.5f, check = TaskCheck.CurlTrunk, action = "drink" },
            new CourseTask { label = "Step over the fallen log", landmark = Landmark("03 "), radius = 5, check = TaskCheck.LiftLegs },
            new CourseTask { label = "Reach the finish arch", landmark = Landmark("04 "), radius = 4, check = TaskCheck.Reach },
        };

        var intro = root.AddComponent<DivaIntro>();
        intro.gameCamera = Camera.main;
        intro.controller = controller;
        intro.locomotion = locomotion;
        intro.rocket = root.GetComponentInChildren<DivaOrbit>();
        intro.skyCamera = sky;
        intro.game = game;
    }

    // ---------- Assets ----------

    /// <summary>Material look for every kit material name: texture, colour, smoothness, metallic, emission.
    /// Candy pastel palette with neon accents; value still steps up towards the path and targets.</summary>
    static readonly Dictionary<string, (Kind? kind, Color colour, float smooth, float metal, Color? glow)> Looks =
        new Dictionary<string, (Kind?, Color, float, float, Color?)>
    {
        ["Plaster_Teal"] = (Kind.Plaster, new Color(.6f, .92f, .84f), .3f, 0, null),        // mint
        ["Plaster_Orange"] = (Kind.Plaster, new Color(1, .78f, .66f), .3f, 0, null),        // peach
        ["Plaster_Mauve"] = (Kind.Plaster, new Color(.8f, .68f, 1), .3f, 0, null),          // lavender
        ["Plaster_Cream"] = (Kind.Plaster, new Color(1, .93f, .95f), .3f, 0, null),         // strawberry milk
        ["Plaster_Blue"] = (Kind.Plaster, new Color(.66f, .84f, 1), .3f, 0, null),          // baby blue
        ["Plaster_Stone"] = (Kind.Concrete, new Color(.9f, .84f, .94f), .3f, 0, null),      // lilac stone
        ["Brick_Red"] = (Kind.Brick, new Color(1, .6f, .72f), .25f, 0, null),               // strawberry brick
        ["Brick_Dark"] = (Kind.Brick, new Color(.82f, .62f, .82f), .25f, 0, null),          // mauve brick
        ["Panel_Navy"] = (Kind.MetalPanel, new Color(.74f, .64f, .96f), .5f, .3f, null),    // lilac panels
        ["Panel_Grey"] = (Kind.MetalPanel, new Color(.93f, .9f, .98f), .5f, .3f, null),     // pearl panels
        ["Trim_Stone"] = (Kind.Concrete, new Color(1, .97f, .98f), .35f, 0, null),          // icing white
        ["Roof_Slate"] = (Kind.RoofTile, new Color(1, .5f, .72f), .4f, 0, null),            // bubblegum
        ["Roof_Terracotta"] = (Kind.RoofTile, new Color(1, .66f, .6f), .4f, 0, null),       // coral
        ["Roof_Teal"] = (Kind.RoofTile, new Color(.55f, .88f, .82f), .4f, 0, null),         // mint
        ["Roof_Ridge"] = (Kind.Concrete, new Color(1, .9f, .95f), .35f, 0, null),
        ["Wood_Dark"] = (Kind.Wood, new Color(.86f, .48f, .66f), .3f, 0, null),            // raspberry beams
        ["Wood_Door"] = (Kind.Wood, new Color(1, .45f, .62f), .35f, 0, null),               // cherry
        ["Wood_Green"] = (Kind.Wood, new Color(.5f, .85f, .76f), .35f, 0, null),
        ["Wood_Blue"] = (Kind.Wood, new Color(.62f, .7f, 1), .35f, 0, null),
        ["Crate"] = (Kind.Wood, new Color(1, .88f, .62f), .3f, 0, null),                    // butter
        ["Glass"] = (null, new Color(.62f, .66f, .96f), .9f, .2f, null),
        ["Glass_Lit"] = (null, new Color(1, .78f, .9f), .9f, 0, new Color(1, .55f, .82f) * 1.4f),
        ["Metal_Dark"] = (Kind.MetalPanel, new Color(.64f, .56f, .82f), .5f, .4f, null),    // lilac metal
        ["Metal_Light"] = (Kind.MetalPanel, new Color(.94f, .92f, .99f), .55f, .4f, null),
        ["Concrete"] = (Kind.Concrete, new Color(.94f, .86f, .92f), .25f, 0, null),
        ["Awning_Red"] = (Kind.Plaster, new Color(1, .48f, .68f), .25f, 0, null),
        ["Awning_Teal"] = (Kind.Plaster, new Color(.46f, .88f, .8f), .25f, 0, null),
        ["Awning_Yellow"] = (Kind.Plaster, new Color(1, .9f, .52f), .25f, 0, null),
        ["Sign_Board"] = (null, new Color(.58f, .38f, .68f), .4f, .1f, null),                // plum
        ["Neon_Cyan"] = (null, new Color(.4f, .95f, 1), .5f, 0, new Color(.25f, .9f, 1) * 4),
        ["Neon_Magenta"] = (null, new Color(1, .35f, .8f), .5f, 0, new Color(1, .25f, .78f) * 5),
        ["Neon_Orange"] = (null, new Color(1, .65f, .5f), .5f, 0, new Color(1, .5f, .4f) * 4),   // peach neon
        ["Neon_Yellow"] = (null, new Color(1, .92f, .45f), .5f, 0, new Color(1, .85f, .35f) * 4),
        ["Screen"] = (null, new Color(.85f, .45f, .78f), .15f, 0, new Color(1, .35f, .78f) * 1.2f),
        ["Letter_Glow"] = (null, Color.white, .3f, 0, new Color(1, .96f, .98f) * 2f),
        ["Letter_Cream"] = (null, new Color(1, .97f, .98f), .3f, 0, null),
        ["Cloth_Purple"] = (Kind.Plaster, new Color(.76f, .55f, 1), .25f, 0, null),
        ["Cloth_Teal"] = (Kind.Plaster, new Color(.46f, .86f, .78f), .25f, 0, null),
        ["Lamp_Glow"] = (null, new Color(1, .9f, .95f), .5f, 0, new Color(1, .75f, .9f) * 5),
        ["Foliage"] = (null, new Color(1, .64f, .82f), .3f, 0, null),                       // cherry blossom
        ["Bark"] = (Kind.Wood, new Color(.72f, .52f, .64f), .25f, 0, null),
        ["Soil"] = (null, new Color(.74f, .55f, .66f), .1f, 0, null),
        ["Hazard"] = (Kind.Hazard, Color.white, .35f, 0, null),                              // candy-cane stripes
        ["Clock_Face"] = (null, new Color(1, .96f, .98f), .4f, 0, new Color(.35f, .3f, .34f)),
        // Targets keep the classic red/white/orange of the darker version, so they pop against the pastels.
        ["Target_Face"] = (null, new Color(.97f, .97f, .97f), .5f, 0, new Color(.25f, .25f, .25f)),
        ["Target_Ring"] = (null, new Color(.9f, .08f, .12f), .5f, 0, new Color(.6f, .02f, .05f)),
        ["Target_Rim"] = (null, new Color(1, .42f, .08f), .5f, 0, new Color(1, .35f, .04f) * 4),
        ["Target_Bull"] = (null, new Color(1, .12f, .16f), .5f, 0, new Color(1, .08f, .12f) * 2.5f),
        ["Neon_Target"] = (null, new Color(1, .4f, .2f), .5f, 0, new Color(1, .28f, .08f) * 3.5f),   // neon halo rings; brighter blooms over the red/white face
        ["Drone_Shell"] = (null, new Color(.5f, .56f, .7f), .6f, .3f, null),
        // Stand-in rocket.
        ["Rocket_Body"] = (Kind.MetalPanel, new Color(1, .95f, .97f), .6f, .2f, null),
        ["Rocket_Nose"] = (null, new Color(1, .4f, .68f), .6f, .1f, null),
        ["Rocket_Stripe"] = (null, new Color(.5f, .9f, .85f), .5f, .1f, null),
    };

    static void PrepareAssets()
    {
        Directory.CreateDirectory(MaterialFolder);
        foreach (var name in new[] { "hit", "burst", "crosshair" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(Kenney + "/Sprites/" + name + ".png");
            if (importer && importer.textureType != TextureImporterType.Sprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }
        }
        string slotPath = Folder + "/Settings/Diva Model Slots.asset";
        Directory.CreateDirectory(Folder + "/Settings");
        slots = AssetDatabase.LoadAssetAtPath<DivaModelSlots>(slotPath);
        if (!slots) { slots = ScriptableObject.CreateInstance<DivaModelSlots>(); AssetDatabase.CreateAsset(slots, slotPath); }

        town.Clear();
        foreach (var pair in Looks)
        {
            var (kind, colour, smooth, metal, glow) = pair.Value;
            town[pair.Key] = kind.HasValue
                ? Textured("Town " + pair.Key, kind.Value, colour, smooth, metal, glow)
                : Lit("Town " + pair.Key, colour, smooth, metal, glow);
        }
        asphalt = Textured("Diva Asphalt", Kind.Asphalt, new Color(.76f, .68f, .94f), .35f, 0);   // lavender street
        sidewalk = Textured("Diva Sidewalk", Kind.Pavers, new Color(1, .88f, .94f), .25f, 0);     // pink paving
        curb = Textured("Diva Kerb", Kind.Concrete, new Color(1, .97f, .99f), .3f, 0);
        pathLight = Lit("Diva Path Edge Light", new Color(.3f, .95f, 1), .5f, 0, new Color(.15f, .85f, 1) * 4);
        landingLight = Lit("Diva Runway Light", new Color(1, .5f, .82f), .5f, 0, new Color(1, .35f, .8f) * 3.5f);
        cable = Lit("Diva Cable", new Color(.62f, .46f, .68f), .3f);
        bulb = Lit("Diva Bulb", new Color(1, .85f, .92f), .5f, 0, new Color(1, .7f, .88f) * 4);
        flameOuter = Lit("Diva Rocket Flame", new Color(1, .5f, .4f), .3f, 0, new Color(1, .35f, .3f) * 5);
        flameCore = Lit("Diva Rocket Flame Core", new Color(1, .95f, .8f), .3f, 0, new Color(1, .9f, .7f) * 8);
        cloud = Lit("Diva Cloud Soft", new Color(1, .92f, .97f), 0, 0, new Color(.45f, .32f, .42f));
        beam = MaterialAsset("Diva Laser Beam", "Universal Render Pipeline/Unlit");
        beam.SetColor("_BaseColor", new Color(1, .3f, .8f) * 6);   // pink laser
        EditorUtility.SetDirty(beam);
        sky = MaterialAsset("Diva Sky", "Skybox/Procedural");
        sky.SetColor("_SkyTint", new Color(1, .55f, .85f));
        sky.SetColor("_GroundColor", new Color(.9f, .7f, .92f));
        sky.SetFloat("_AtmosphereThickness", .6f); // thicker turns the horizon sunset-yellow
        sky.SetFloat("_Exposure", 1.2f);
        // No sun disc: its HDR highlight reflects off every surface when seen from above and blows out bloom.
        sky.SetFloat("_SunDisk", 0);
        EditorUtility.SetDirty(sky);
        RemapKitMaterials();
        PrepareFabMaterials();
        AssetDatabase.SaveAssets();
    }

    /// <summary>Points each kit FBX's embedded materials at the shared Town materials (by name), once.</summary>
    static void RemapKitMaterials()
    {
        foreach (var path in Directory.GetFiles(TownFolder, "*.fbx"))
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            if (!importer) continue;
            var names = new HashSet<string>(AssetDatabase.LoadAllAssetsAtPath(path).OfType<Material>().Select(m => m.name));
            foreach (var key in importer.GetExternalObjectMap().Keys) if (key.type == typeof(Material)) names.Add(key.name);
            bool changed = false;
            if (importer.animationType != ModelImporterAnimationType.None) { importer.animationType = ModelImporterAnimationType.None; changed = true; }
            if (importer.importCameras || importer.importLights) { importer.importCameras = importer.importLights = false; changed = true; }
            foreach (var name in names)
            {
                if (!town.TryGetValue(name, out var material)) { Debug.LogWarning($"No Diva look for kit material '{name}' in {path}"); continue; }
                var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), name);
                if (importer.GetExternalObjectMap().TryGetValue(id, out var current) && current == material) continue;
                importer.AddRemap(id, material);
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
        }
    }

    static Material MaterialAsset(string name, string shader)
    {
        string path = MaterialFolder + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!material)
        {
            material = new Material(Shader.Find(shader)) { name = name };
            AssetDatabase.CreateAsset(material, path);
        }
        return material;
    }

    static Material Lit(string name, Color color, float smoothness = .4f, float metallic = 0, Color? emission = null)
    {
        var material = MaterialAsset(name, "Universal Render Pipeline/Lit");
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        material.SetTexture("_BaseMap", null);
        material.SetTexture("_BumpMap", null);
        material.DisableKeyword("_NORMALMAP");
        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else
        {
            material.DisableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    /// <summary>Lit material with a procedural detail texture and normal map; tiling defaults to real-world scale (kit UVs are metres).</summary>
    static Material Textured(string name, Kind kind, Color color, float smoothness, float metallic, Color? emission = null, float tiling = 0)
    {
        var material = Lit(name, color, smoothness, metallic, emission);
        var (albedo, normal) = DivaTextures.Get(kind);
        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", .8f);
        material.EnableKeyword("_NORMALMAP");
        float scale = tiling > 0 ? tiling : 1 / DivaTextures.TileMetres(kind);
        material.SetTextureScale("_BaseMap", Vector2.one * scale);
        EditorUtility.SetDirty(material);
        return material;
    }

    // ---------- Scene helpers ----------

    static Transform Group(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject Part(Transform parent, string name, PrimitiveType type, Material material, Vector3 position, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>()); // visuals only; targets are found by the blaster, not physics
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>A box whose UVs are in metres, so tiled textures keep their real-world scale at any size.</summary>
    static GameObject WorldBox(Transform parent, string name, Material material, Vector3 centre, Vector3 size, Quaternion? rotation = null)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = centre;
        go.transform.localRotation = rotation ?? Quaternion.identity;
        go.AddComponent<MeshFilter>().sharedMesh = DivaMeshes.MetricBox(size);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    /// <summary>
    /// Instantiates a kit model (Models/Town/&lt;name&gt;.fbx), or the replacement prefab when a slot is set
    /// (scaled to fitHeight). Kit models face +Z; rotation turns that front towards where it should look.
    /// </summary>
    static GameObject PlaceModel(Transform parent, string kitName, GameObject replacement, Vector3 position, Quaternion rotation, float fitHeight, bool grounded = true)
    {
        var anchor = Group(parent, kitName);
        anchor.localPosition = position;
        anchor.localRotation = rotation;
        if (replacement && fitHeight > 0 && Slot(replacement, anchor, fitHeight, grounded))
        {
            anchor.name = replacement.name;
            return anchor.GetChild(0).gameObject;
        }
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(TownFolder + "/" + kitName + ".fbx");
        if (!model) { Debug.LogWarning("Missing kit model " + kitName); return anchor.gameObject; }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, anchor);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        return instance;
    }

    /// <summary>
    /// If a replacement prefab is set, instantiates it under the anchor, scaled so its height matches,
    /// centred on the anchor and (when grounded) standing on it. Returns false when the slot is empty.
    /// </summary>
    static bool Slot(GameObject prefab, Transform anchor, float height, bool grounded = true)
    {
        if (!prefab) return false;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, anchor);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return true;
        Bounds Measure()
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            return b;
        }
        instance.transform.localScale *= height / Mathf.Max(.01f, Measure().size.y);
        var bounds = Measure();
        Vector3 pivot = new Vector3(bounds.center.x, grounded ? bounds.min.y : bounds.center.y, bounds.center.z);
        instance.transform.position += anchor.position - pivot;
        return true;
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }

    // ---------- Preview renders ----------

    /// <summary>Renders overview and chase-camera stills of the open scene to Recordings/preview.</summary>
    [MenuItem("Diva/Game/Render Preview Shots")]
    public static void RenderPreviewShots()
    {
        var root = GameObject.Find(RootName);
        var locomotion = UnityEngine.Object.FindAnyObjectByType<DigiPhantLocomotion>();
        if (!root || !locomotion) throw new Exception("Build the game layer first.");
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Recordings/preview"));
        Directory.CreateDirectory(folder);
        var frame = root.transform;
        var elephant = locomotion.travelRoot;
        // Keep the sun behind the overview camera; facing into it shows only shaded sides and glare.
        var sun = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None).FirstOrDefault(l => l.type == LightType.Directional);
        Vector3 sunward = sun ? Flat(sun.transform.forward).normalized : frame.forward;
        Vector3 centre = frame.TransformPoint(Vector3.zero);
        var shots = new (string name, Vector3 position, Vector3 lookAt, float fov)[]
        {
            ("overview", centre - sunward * 75 + Vector3.up * 62, centre, 50),
            ("game-camera", elephant.position + elephant.rotation * new Vector3(0, 4.5f, -8), elephant.position + Vector3.up * 1.5f, 0),
            ("start-chase", elephant.position - elephant.forward * 7 + Vector3.up * 6.5f, elephant.position + elephant.forward * 14 + Vector3.up * 1.5f, 60),
            ("course-low", frame.TransformPoint(new Vector3(-17, 3.2f, -16)), frame.TransformPoint(new Vector3(8, 1.5f, -17)), 60),
            ("finish-side", frame.TransformPoint(new Vector3(10, 4.5f, 12)), frame.TransformPoint(new Vector3(-12, 1.5f, 18)), 60),
            ("street", frame.TransformPoint(new Vector3(-20, 2, 35.5f)), frame.TransformPoint(new Vector3(20, 4, 36)), 65),
            ("rocket", frame.TransformPoint(new Vector3(-125, 24, -20)), root.transform.Find("Giant rocket").position + Vector3.down * 25, 55),   // side-on view of the rocket on its orbit
        };
        var go = new GameObject("Preview camera");
        try
        {
            var camera = go.AddComponent<Camera>();
            camera.allowHDR = true;
            camera.farClipPlane = 600;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var rt = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            // A new camera's first frame renders with uninitialised lighting in batch mode; discard it.
            camera.transform.SetPositionAndRotation(shots[0].position, Quaternion.LookRotation(shots[0].lookAt - shots[0].position));
            camera.Render();
            foreach (var shot in shots)
            {
                camera.fieldOfView = shot.fov;
                camera.transform.position = shot.position;
                camera.transform.LookAt(shot.lookAt);
                if (shot.fov == 0)
                {
                    // The in-game view: DivaDemo's camera, lifted by DivaSkyCamera.
                    var sky = root.GetComponent<DivaSkyCamera>();
                    camera.fieldOfView = sky ? sky.fieldOfView : 55;
                    if (sky) camera.transform.rotation = Quaternion.AngleAxis(-sky.lookUp, camera.transform.right) * camera.transform.rotation;
                }
                camera.Render();
                RenderTexture.active = rt;
                var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(folder, shot.name + ".png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            RenderTexture.active = null;
            camera.targetTexture = null;
            rt.Release();
            Debug.Log("DIVA_PREVIEW_SHOTS: " + folder);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    /// <summary>Renders keyframes of the race intro (camera, rocket position, mech power-up) to Recordings/preview/intro-*.png.</summary>
    [MenuItem("Diva/Game/Render Intro Frames")]
    public static void RenderIntroFrames()
    {
        var root = GameObject.Find(RootName);
        var intro = root ? root.GetComponent<DivaIntro>() : null;
        if (!intro) throw new Exception("Build the game layer first.");
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Recordings/preview"));
        Directory.CreateDirectory(folder);
        intro.Prepare();
        var orbit = intro.rocket;
        float a0 = orbit ? orbit.startAngle * Mathf.Deg2Rad : 0;
        var go = new GameObject("Intro preview camera");
        try
        {
            var camera = go.AddComponent<Camera>();
            camera.allowHDR = true;
            camera.farClipPlane = 800;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.Render();   // warm-up frame
            foreach (float t in new[] { 1.5f, 4.2f, 4.9f, 7.0f, 8.4f, 9.6f, 10.6f, 11.9f, 13.6f })
            {
                if (orbit) orbit.SetAngle(a0 + orbit.AngularSpeed * t, 0);
                intro.ApplyBoot(t);
                intro.ApplyFog(t);
                intro.PoseAt(t, out var pos, out var rot, out float fov);
                camera.transform.SetPositionAndRotation(pos, rot);
                camera.fieldOfView = fov;
                camera.Render();
                RenderTexture.active = rt;
                var image = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                image.Apply();
                File.WriteAllBytes(Path.Combine(folder, $"intro-{t:00.0}.png"), image.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(image);
            }
            RenderTexture.active = null;
            camera.targetTexture = null;
            rt.Release();
            Debug.Log("DIVA_INTRO_FRAMES: " + folder);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            intro.ApplyBoot(float.MaxValue);
            intro.ApplyFog(float.MaxValue);
            if (orbit) orbit.Place(0);
        }
    }

    /// <summary>Command line (interactive editor): open the team scene and select the game layer so it is easy to inspect.</summary>
    public static void OpenAndFocus()
    {
        EditorSceneManager.OpenScene(TeamScene);
        EditorApplication.delayCall += () =>
        {
            var root = GameObject.Find(RootName);
            if (!root) return;
            Selection.activeGameObject = root;
            EditorGUIUtility.PingObject(root);
            SceneView.lastActiveSceneView?.FrameSelected();
        };
    }

    /// <summary>Command line: build in the team scene, then render previews.</summary>
    public static void BuildAndRenderTeamScene()
    {
        BuildInTeamScene();
        // Render what Unity shows after opening the saved scene, not the in-memory state left by building.
        EditorSceneManager.OpenScene(TeamScene);
        RenderPreviewShots();
    }
}
