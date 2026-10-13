using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Diva.Editor
{
    /// <summary>
    /// 发布版打包（v1.0 起）：菜单 Diva > Release，或命令行
    /// Unity -batchmode -quit -projectPath . -buildTarget OSXUniversal -executeMethod Diva.Editor.DivaReleaseBuild.BuildMac
    /// Unity -batchmode -quit -projectPath . -buildTarget Win64 -executeMethod Diva.Editor.DivaReleaseBuild.BuildWindows
    /// 输出在 Builds/Release/（不进 git）。打完再跑 Tools/release/package_release.sh：把摄像头识别程序、模型、摄像头权限说明
    /// 和使用说明装进包里，签名、压缩成可以放到 GitHub Release 的 zip。
    /// 打包时临时把产品名改成 "Diva"、公司名 "Team Diva"、桌面版包名 com.teamdiva.diva，打完还原（不改工程设置；头显版的
    /// 名字和包名由 DivaVRBuild 管）。macOS 只打 Apple 芯片（包里的识别程序是 arm64）。
    /// </summary>
    public static class DivaReleaseBuild
    {
        const string Scene = "Assets/DigiPhant/Scenes/Diva.unity";

        [MenuItem("Diva/Release/Build macOS (Apple silicon)")]
        public static void BuildMac()
        {
            UnityEditor.OSXStandalone.UserBuildSettings.architecture = UnityEditor.Build.OSArchitecture.ARM64;
            Build(BuildTarget.StandaloneOSX, "Builds/Release/macOS/Diva.app");
        }

        [MenuItem("Diva/Release/Build Windows (64-bit)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Builds/Release/Windows/Diva/Diva.exe");

        static void Build(BuildTarget target, string path)
        {
            string product = PlayerSettings.productName, company = PlayerSettings.companyName;
            string id = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Standalone);
            var backend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
            try
            {
                PlayerSettings.productName = "Diva";
                PlayerSettings.companyName = "Team Diva";
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.teamdiva.diva");
                // Windows 在 Mac 上只能用 Mono 打（IL2CPP 要在 Windows 上编译）
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                string full = Path.GetFullPath(path);
                string outDir = target == BuildTarget.StandaloneOSX ? Path.GetDirectoryName(full) : Path.GetDirectoryName(full);
                if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
                Directory.CreateDirectory(outDir);
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { Scene }, locationPathName = full, target = target, options = BuildOptions.None,
                });
                var s = report.summary;
                Debug.Log($"DIVA_RELEASE_BUILD {target} {s.result} {s.totalSize / (1024 * 1024)} MB errors {s.totalErrors} in {s.totalTime.TotalSeconds:0} s -> {full}");
                if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
            }
            finally
            {
                PlayerSettings.productName = product;
                PlayerSettings.companyName = company;
                PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, id);
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, backend);
            }
        }
    }
}
