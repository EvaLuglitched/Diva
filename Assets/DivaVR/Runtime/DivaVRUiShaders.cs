using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.VR
{
    /// <summary>
    /// 头显的立体渲染里 uGUI 默认的 UI/Default 和 TextMeshPro 的 SDF 着色器都不画（用它们的东西整个不显示，
    /// 但普通摄像机、电脑上都正常，所以之前每一轮测试都测不到）。这里把所有界面图形换成 Diva/VR UI 材质；
    /// 文字的着色器在 DivaUi.MakeFont 时换成 Diva/VR Text（DivaShowDirector 里传入）。
    /// 界面是随时新建的（横幅、提示…），所以每半秒扫一遍还在用默认材质的。
    /// </summary>
    public class DivaVRUiShaders : MonoBehaviour
    {
        Material ui;
        float next;

        void Start()
        {
            ui = Resources.Load<Material>("DivaVR UI");
            if (!ui) { Debug.LogWarning("DIVA_VR ui shaders: Resources/DivaVR UI missing"); enabled = false; return; }
            Fix();
        }

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + .5f;
            Fix();
        }

        void Fix()
        {
            int n = 0;
            foreach (var g in FindObjectsByType<Graphic>(FindObjectsInactive.Include))
            {
                if (g is TMP_Text || g is TMP_SubMeshUI) continue;   // 文字走字体材质
                if (g.material == null || g.material == Graphic.defaultGraphicMaterial) { g.material = ui; n++; }
            }
            if (n > 0) Debug.Log($"DIVA_VR ui shaders: {n} graphics switched to Diva/VR UI");
        }
    }
}
