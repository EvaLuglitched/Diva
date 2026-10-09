using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.VisionOS;
using UnityEngine;
using UnityEngine.XR.Management;

namespace Diva.VR.EditorTools
{
    /// <summary>
    /// 一键打包到 Vision Pro：Metal 完全沉浸（VR），手部追踪，可以看到自己的真手，生成 Xcode 工程到 Builds/visionOS。
    /// 只在装了 Apple visionOS XR Plugin 的电脑上编译（需要 Pro / 学生授权；Personal 授权的电脑上这个插件会被卸载）。
    /// 装机：visionOS/install.sh 或者在 Xcode 里打开 Builds/visionOS 按运行。
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

        public static BuildResult Build()
        {
            Configure();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = Output,
                target = BuildTarget.VisionOS,
                options = BuildOptions.None
            });
            Debug.Log($"DIVA_VR build {report.summary.result}: {report.summary.totalErrors} errors, {report.summary.totalTime}");
            return report.summary.result;
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

            var vos = VisionOSSettings.currentSettings;
            vos.appMode = VisionOSSettings.AppMode.Metal;
            vos.metalImmersionStyle = VisionOSSettings.ImmersionStyle.Full;
            vos.upperLimbVisibility = VisionOSSettings.Visibility.Visible;
            vos.foveatedRendering = true;
            vos.handsTrackingUsageDescription = "Diva uses your hands to pilot the mech elephant: pinch to spray, hands up for the ultimate.";
            vos.worldSensingUsageDescription = "Diva uses your surroundings to keep the cockpit steady.";
            EditorUtility.SetDirty(vos);

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.VisionOS, "com.jchen60.divavr");
            AssetDatabase.SaveAssets();
        }
    }
}
