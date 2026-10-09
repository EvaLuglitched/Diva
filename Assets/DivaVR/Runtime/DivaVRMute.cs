using System;
using System.Linq;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 测试时不出声：Unity 后台测试（batch 模式）、环境变量 DIVA_MUTE=1（模拟器：SIMCTL_CHILD_DIVA_MUTE=1）
    /// 或启动参数 -divaMute 时把所有声音关掉。真机上照常有声音。
    /// </summary>
    static class DivaVRMute
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (!Application.isBatchMode && Environment.GetEnvironmentVariable("DIVA_MUTE") != "1" &&
                !Environment.GetCommandLineArgs().Contains("-divaMute")) return;
            AudioListener.volume = 0;
            Debug.Log("DIVA_VR muted (test run)");
        }
    }
}
