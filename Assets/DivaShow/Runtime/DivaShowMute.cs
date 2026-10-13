using System;
using System.Linq;
using UnityEngine;

namespace Diva.Show
{
    /// <summary>
    /// 测试时不出声（桌面版）：环境变量 DIVA_MUTE=1 或启动参数 -divaMute 时把所有声音关掉。
    /// 正常打开游戏照常有声音；后台（batch）测试本来就没有声音。头显版有自己的 DivaVRMute。
    /// </summary>
    static class DivaShowMute
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            if (Environment.GetEnvironmentVariable("DIVA_MUTE") != "1" && !Environment.GetCommandLineArgs().Contains("-divaMute")) return;
            AudioListener.volume = 0;
            Debug.Log("DIVA_SHOW muted (test run)");
        }
    }
}
