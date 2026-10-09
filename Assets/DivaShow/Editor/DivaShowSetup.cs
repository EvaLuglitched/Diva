using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Diva.Show.EditorTools
{
    /// <summary>
    /// 把整局流程装进当前场景：一个 "Diva Show" 物体（DivaShowDirector），再填好字体、TMP 着色器、粒子材质和长鸣动画。
    /// 需要 TextMeshPro 基础资源（Assets/TextMesh Pro），没有时先导入。
    /// </summary>
    public static class DivaShowSetup
    {
        const string Root = "Assets/DivaShow";
        const string ObjectName = "Diva Show";

        [MenuItem("Diva/Show/Install Show in Open Scene")]
        public static void InstallMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play first.");
            if (!ImportTmpIfMissing()) { Debug.Log("DIVA_SHOW TMP resources imported; run Install again."); return; }
            Install(SceneManager.GetActiveScene());
        }

        [MenuItem("Diva/Show/Import TextMeshPro Resources")]
        public static void ImportTmpMenu() => ImportTmpIfMissing();

        /// <summary>Returns true when the TMP essentials are already there.</summary>
        public static bool ImportTmpIfMissing()
        {
            if (AssetDatabase.IsValidFolder("Assets/TextMesh Pro") && Shader.Find("TextMeshPro/Distance Field")) return true;
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            string package = info != null ? Path.Combine(info.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage") : null;
            if (package == null || !File.Exists(package)) throw new FileNotFoundException("TMP Essential Resources.unitypackage not found in com.unity.ugui.");
            AssetDatabase.ImportPackage(package, false);
            AssetDatabase.Refresh();
            return false;
        }

        public static DivaShowDirector Install(Scene scene)
        {
            var director = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<DivaShowDirector>(true)).FirstOrDefault();
            if (!director)
            {
                var go = new GameObject(ObjectName);
                SceneManager.MoveGameObjectToScene(go, scene);
                Undo.RegisterCreatedObjectUndo(go, "Diva Show");
                director = go.AddComponent<DivaShowDirector>();
            }
            Undo.RecordObject(director, "Diva Show");
            T Find<T>() where T : Component => scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<T>(true)).FirstOrDefault();
            director.demo = Find<DigiPhant.DivaDemo>();
            director.controller = director.demo ? director.demo.GetComponent<DigiPhant.DigiPhantController>() : Find<DigiPhant.DigiPhantController>();
            director.locomotion = director.demo ? director.demo.GetComponent<DigiPhant.DigiPhantLocomotion>() : Find<DigiPhant.DigiPhantLocomotion>();
            director.game = Find<DivaGameManager>();
            director.laser = Find<DivaLaserBlaster>();
            director.skins = Find<DivaMechSkins>();
            director.boosters = Find<DivaBoosters>();
            director.bubbles = Find<DivaBubbleCannons>();
            director.waterGun = Find<DivaTrunkBlaster>();
            director.mainCamera = director.demo && director.demo.thirdPersonCamera ? director.demo.thirdPersonCamera
                : scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>(true)).FirstOrDefault(c => c.CompareTag("MainCamera"));
            director.latinFont = AssetDatabase.LoadAssetAtPath<Font>(Root + "/Fonts/SairaExtraCondensed-Bold.ttf");
            director.sdfShader = Shader.Find("TextMeshPro/Distance Field");
            director.glowMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/eva大象/Generated/Mech Flame.mat");
            director.bubbleMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/eva大象/Generated/Mech Bubble.mat");
            director.trumpetClip = AssetDatabase.LoadAllAssetsAtPath("Assets/Elephant/Animations/elephant@trumpet.fbx")
                .OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview", StringComparison.Ordinal));
            director.entranceMusic = AssetDatabase.LoadAssetAtPath<AudioClip>(Root + "/Audio/diva_entrance.wav");
            director.raceIntro = Find<DivaIntro>();
            var look = director.GetComponent<DivaLook>();
            if (!look) look = Undo.AddComponent<DivaLook>(director.gameObject);
            look.targetCamera = director.mainCamera;
            look.hero = director.locomotion ? director.locomotion.travelRoot : null;
            director.look = look;
            EditorUtility.SetDirty(look);
            EditorUtility.SetDirty(director);
            EditorSceneManager.MarkSceneDirty(scene);
            string missing = string.Join(", ", new (string, UnityEngine.Object)[]
            {
                ("DivaDemo", director.demo), ("game layer", director.game), ("mech skins", director.skins), ("font", director.latinFont),
                ("TMP shader", director.sdfShader), ("glow material", director.glowMaterial), ("trumpet clip", director.trumpetClip),
                ("entrance music", director.entranceMusic), ("Eva's race intro", director.raceIntro),
            }.Where(x => !x.Item2).Select(x => x.Item1));
            Debug.Log("DIVA_SHOW_INSTALLED " + scene.path + (missing.Length > 0 ? " (missing: " + missing + ")" : ""));
            return director;
        }

        /// <summary>
        /// Batch: Unity -batchmode -projectPath P -executeMethod Diva.Show.EditorTools.DivaShowSetup.InstallFromCommandLine
        ///   [-divaScene Assets/DigiPhant/Scenes/Diva.unity] -quit
        /// (run twice on a project without TextMesh Pro resources: the first run imports them)
        /// </summary>
        public static void InstallFromCommandLine()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-divaScene");
            string path = i >= 0 && i + 1 < args.Length ? args[i + 1] : "Assets/DigiPhant/Scenes/Diva.unity";
            if (!ImportTmpIfMissing()) { Debug.Log("DIVA_SHOW_TMP_IMPORTED run again to install"); return; }
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            Install(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }
}
