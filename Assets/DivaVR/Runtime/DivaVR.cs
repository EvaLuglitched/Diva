using System;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;

namespace Diva.VR
{
    /// <summary>
    /// VR 模式开关。在 Vision Pro 上（XR 在运行）自动打开；在电脑上可以用"模拟器"打开：
    /// 菜单 Diva > VR > Play in VR Simulator，或命令行 -divaVR（后台测试用）。
    /// 打开后：玩家坐在大象背上的驾驶舱里（DivaVRRig），镜头只跟着头走，原来所有控制镜头的代码都让开，
    /// 界面变成驾驶舱前方的空间面板，手势来自 Vision Pro 的手部追踪（模拟器里用键盘鼠标）。
    /// </summary>
    public static class DivaVR
    {
        public const string SimulatePref = "Diva.VR.Simulate";

        static bool? active;

        /// <summary>VR 模式是否打开（第一次读取时判断一次，整局不变）。</summary>
        public static bool Active => active ??= Detect();

        /// <summary>电脑上的模拟模式（没有头显：鼠标转头，键盘代替手势）。</summary>
        public static bool Simulated { get; private set; }

        /// <summary>驾驶舱：跟着大象走的玩家空间。头、仪表盘面板都挂在它下面。</summary>
        public static Transform Rig { get; internal set; }
        public static Transform Head { get; internal set; }
        public static Transform HudAnchor { get; internal set; }
        /// <summary>前方 3 米多的空中面板：倒数和技能横幅这些大时刻放在这里。</summary>
        public static Transform MomentsAnchor { get; internal set; }
        /// <summary>仪表台上斜放着正对驾驶员的小屏（技能和大招充能）。</summary>
        public static Transform DashAnchor { get; internal set; }
        /// <summary>仪表盘面板宽多少米。</summary>
        public static float HudWidth { get; internal set; } = 1.6f;

        // 项目关了 domain reload，静态值会跨 Play 保留，进 Play 时清掉
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { active = null; Simulated = false; Rig = Head = HudAnchor = MomentsAnchor = DashAnchor = null; }

        static bool Detect()
        {
            if (XRSettings.isDeviceActive) return true;
            Simulated = Environment.GetCommandLineArgs().Contains("-divaVR");
#if UNITY_EDITOR
            Simulated |= UnityEditor.EditorPrefs.GetBool(SimulatePref, false);
#endif
            return Simulated;
        }
    }
}
