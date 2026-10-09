using UnityEditor;

namespace Diva.VR.EditorTools
{
    /// <summary>电脑上试 VR 版：不用头显，鼠标转头、键盘代替手势（见 DivaVRSimulator）。</summary>
    static class DivaVRMenu
    {
        const string Menu = "Diva/VR/Play in VR Simulator";

        [MenuItem(Menu, false, 200)]
        static void Toggle()
        {
            bool on = !EditorPrefs.GetBool(DivaVR.SimulatePref, false);
            EditorPrefs.SetBool(DivaVR.SimulatePref, on);
            if (on && !EditorApplication.isPlaying) EditorApplication.EnterPlaymode();
        }

        [MenuItem(Menu, true)]
        static bool Validate()
        {
            Menu_SetChecked();
            return !EditorApplication.isPlaying;
        }

        static void Menu_SetChecked() => UnityEditor.Menu.SetChecked(Menu, EditorPrefs.GetBool(DivaVR.SimulatePref, false));
    }
}
