using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.VisionOS;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;

namespace Diva.VR.EditorTools
{
    /// <summary>
    /// 一键打包到 Vision Pro：Metal 渲染、完全沉浸，
    /// 手部追踪，可以看到自己的真手，生成 Xcode 工程到 Builds/visionOS。
    /// 只在装了 Apple visionOS XR Plugin 的电脑上编译（需要 Pro / 学生授权；Personal 授权的电脑上这个插件会被卸载）。
    /// 装机：在 Xcode 里打开 Builds/visionOS 按运行。主屏幕上的名字要在 Unity 外面改（Unity 进程占着 Info.plist，在里面写会把它清空）：
    /// plutil -replace CFBundleDisplayName -string "Diva VR" Builds/visionOS/Info.plist
    /// </summary>
    public static class DivaVRBuild
    {
        const string Scene = "Assets/DigiPhant/Scenes/Diva.unity";
        const string Output = "Builds/visionOS";

        [MenuItem("Diva/VR/Build for Vision Pro", false, 210)]
        public static void BuildMenu()
        {
            var result = Build();
            EditorUtility.DisplayDialog("Diva VR", result == BuildResult.Succeeded
                ? "Xcode project ready in " + Output + ". Open it in Xcode and press Run with your Vision Pro selected."
                : "Build failed (" + result + "). See the Console.", "OK");
        }

        /// <summary>命令行：Unity -batchmode -buildTarget VisionOS -executeMethod Diva.VR.EditorTools.DivaVRBuild.BuildFromCommandLine</summary>
        public static void BuildFromCommandLine() => EditorApplication.Exit(Build() == BuildResult.Succeeded ? 0 : 1);

        [MenuItem("Diva/VR/Build for Vision Pro Simulator", false, 211)]
        public static void BuildSimulatorMenu()
        {
            var result = Build(true);
            EditorUtility.DisplayDialog("Diva VR", result == BuildResult.Succeeded
                ? "Xcode project ready in " + Output + "-sim. Run it on the Apple Vision Pro simulator (add -divaAutoplay to watch a full run)."
                : "Build failed (" + result + "). See the Console.", "OK");
        }

        /// <summary>命令行（模拟器）：… -executeMethod Diva.VR.EditorTools.DivaVRBuild.BuildSimulatorFromCommandLine</summary>
        public static void BuildSimulatorFromCommandLine() => EditorApplication.Exit(Build(true) == BuildResult.Succeeded ? 0 : 1);

        static bool Mixed => System.Environment.GetCommandLineArgs().Contains("-divaMixed");

        public static BuildResult Build(bool simulator = false)
        {
            Configure();
            SetSdk(simulator);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = (simulator ? Output + "-sim" : Output) + (Mixed ? "-mixed" : ""),
                target = BuildTarget.VisionOS,
                options = BuildOptions.None
            });
            Debug.Log($"DIVA_VR build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalTime}");
            return report.summary.result;
        }

        /// <summary>真机还是模拟器（PlayerSettings.VisionOS.sdkVersion；用反射，接口名在不同 Unity 版本里可能不同）。</summary>
        static void SetSdk(bool simulator)
        {
            var nested = typeof(PlayerSettings).GetNestedType("VisionOS");
            var prop = nested?.GetProperty("sdkVersion");
            if (prop == null) { Debug.LogWarning("DIVA_VR: PlayerSettings.VisionOS.sdkVersion not found; leaving the SDK as is"); return; }
            prop.SetValue(null, System.Enum.Parse(prop.PropertyType, simulator ? "Simulator" : "Device"));
            Debug.Log("DIVA_VR sdk " + prop.GetValue(null));
        }

        public static void Configure()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.VisionOS)
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.VisionOS, BuildTarget.VisionOS);

            // XR：visionOS 平台用 Apple 的 XR loader，开机就启动
            EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget);
            if (!perTarget)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            var group = BuildTargetGroup.VisionOS;
            if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
            if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            var general = perTarget.SettingsForBuildTarget(group);
            general.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(general.Manager, "UnityEngine.XR.VisionOS.VisionOSLoader", group);

            // 插件的设置资源：用工程里已有的；没有就新建一个存到 Assets/XR/Settings，并登记到 EditorBuildSettings
            //（GetOrCreateSettings 只在内存里建、不登记，打包后处理那一步会因为找不到它而出错，Info.plist 不完整）
            var vos = VisionOSSettings.currentSettings;
            if (!vos)
            {
                var guid = AssetDatabase.FindAssets("t:VisionOSSettings").FirstOrDefault();
                vos = guid != null ? AssetDatabase.LoadAssetAtPath<VisionOSSettings>(AssetDatabase.GUIDToAssetPath(guid)) : null;
                if (!vos)
                {
                    Directory.CreateDirectory("Assets/XR/Settings");
                    vos = ScriptableObject.CreateInstance<VisionOSSettings>();
                    AssetDatabase.CreateAsset(vos, "Assets/XR/Settings/VisionOSSettings.asset");
                }
                VisionOSSettings.currentSettings = vos;
                vos.GetOrCreateRuntimeSettings();
                Debug.Log("DIVA_VR visionOS settings registered: " + AssetDatabase.GetAssetPath(vos));
            }
            vos.appMode = VisionOSSettings.AppMode.Metal;
            // 完全沉浸：一睁眼就在游戏里（Carl 要的）。命令行 -divaMixed 打透视版（画面透明的地方露出真实房间）
            vos.metalImmersionStyle = Mixed ? VisionOSSettings.ImmersionStyle.Mixed : VisionOSSettings.ImmersionStyle.Full;
            vos.upperLimbVisibility = VisionOSSettings.Visibility.Visible;
            vos.foveatedRendering = true;
            vos.handsTrackingUsageDescription = "Diva uses your hands to pilot the mech elephant: pinch to spray, hands up for the ultimate.";
            vos.worldSensingUsageDescription = "Diva opens a portal in your room and keeps the cockpit steady.";
            EditorUtility.SetDirty(vos);

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.VisionOS, "com.jchen60.divavr");

            // Apple visionOS 插件自带的项目检查规则（菜单里才会跑，命令行打包不会）要求：
            //   每个摄像机都要生成深度纹理（合成器每帧要拿深度把画面摆进空间，没有就一帧都不显示）；
            //   开 HDR 就必须同时开 Allow HDR Display Output 和 DisplayP3 色域，否则 URP 把 HDR 输出的着色器裁掉——
            //     头显上只用 LDR（透视也要求关 HDR），泛光照常；
            //   开屏画面在 visionOS 上还不支持；最低系统版本 2.0。
            // 这些只改打包这份工程的资源；电脑版（PC 画质）不受影响。
            for (int i = 0; i < QualitySettings.names.Length; i++)
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UniversalRenderPipelineAsset q) PrepareUrp(q);
            if (GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset d) PrepareUrp(d);
            PlayerSettings.SplashScreen.show = false;
            // 失去焦点（打开系统菜单）时继续运行，不然镜头会锁在头上（插件的检查规则）
            PlayerSettings.runInBackground = true;
            PlayerSettings.VisionOS.targetOSVersionString = "2.0";
            MarkStatic();
            // 头显上记 GPU 每帧用时（DivaVRPerfLog）
            PlayerSettings.enableFrameTimingStats = true;
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 把镇子里不动的东西标成 Batching Static（只改这个打包副本里的场景）：Unity 打包时按材质合并网格，
        /// 渲染线程每帧提交几千个绘制调用（11–14 毫秒，45 fps 的根源）降到几十个。会动的（大象、靶子、漂的云、火箭、粒子）不标。
        /// </summary>
        static void MarkStatic()
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(Scene);
            // DivaSkyRotate 挂在整个 Diva Game Layer 根上但只转天空的材质，不算；Light / Camera 也不算（只看会动的）
            string[] dynamicTypes = { "DivaTarget", "DivaFloat", "DivaOrbit", "DigiPhantLocomotion", "DigiPhantController", "Animator", "Animation", "ParticleSystem", "DivaBoosters", "DivaBubbleCannons", "DivaTrunkBlaster", "Canvas" };
            int marked = 0, skipped = 0;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                bool dynamic = false;
                for (var t = r.transform; t && !dynamic; t = t.parent)
                {
                    if (t.name.Contains("Rocket") || t.name.Contains("Cloud") || t.name.Contains("Elephant") || t.name.Contains("elephant")) dynamic = true;
                    foreach (var c in t.GetComponents<Component>())
                        if (c && System.Array.IndexOf(dynamicTypes, c.GetType().Name) >= 0) { dynamic = true; break; }
                }
                if (dynamic) { skipped++; continue; }
                GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic);
                marked++;
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
            Debug.Log($"DIVA_VR static batching: {marked} renderers marked static, {skipped} left dynamic");
        }

        static void PrepareUrp(UniversalRenderPipelineAsset urp)
        {
            var so = new SerializedObject(urp);
            bool changed = false;
            void Set(string name, bool value)
            {
                var p = so.FindProperty(name);
                if (p == null) { Debug.LogWarning($"DIVA_VR: URP asset has no {name}"); return; }
                if (p.boolValue == value) return;
                p.boolValue = value; changed = true;
            }
            void SetInt(string name, int value)
            {
                var p = so.FindProperty(name);
                if (p == null) { Debug.LogWarning($"DIVA_VR: URP asset has no {name}"); return; }
                if (p.intValue == value) return;
                p.intValue = value; changed = true;
            }
            // 透视版（-divaMixed）要露出真实房间：开着后期处理时画面的透明通道要一路保留到最后，URP 打包时会把"保留透明度"的
            // 着色器变体裁掉，除非 URP 资源打开了 Alpha Processing。完全沉浸版不要它：画面里半透明的东西（粒子、面板、泡泡）
            // 会写出小于 1 的透明度，头显合成时那些地方会变暗、跟着粒子一闪一闪
            Set("m_AllowPostProcessAlphaOutput", Mixed);
            Set("m_RequireDepthTexture", true);
            Set("m_SupportsHDR", false);
            // 阴影：软阴影 + 2048 的阴影图。Mobile 设置是 1024 的硬阴影，头一动阴影边缘的锯齿就爬来爬去（闪）。
            // 软阴影的着色器变体也是打包时决定的，运行时再开没用
            Set("m_SoftShadowsSupported", true);
            // 试过 Forward+ + GPU Resident Drawer：渲染线程 12→16 毫秒、GPU 20→26 毫秒，更慢，不用。绘制调用靠打包时的静态合批（MarkStatic）
            SetInt("m_SoftShadowQuality", 2);
            SetInt("m_MainLightShadowmapResolution", 2048);
            if (!changed) return;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(urp);
            Debug.Log($"DIVA_VR URP for visionOS: {urp.name} depth texture on, HDR off, alpha processing {Mixed}, soft shadows 2048");
        }
    }
}
