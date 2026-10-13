using Diva.Show;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.VR
{
    /// <summary>
    /// 头显的立体渲染里 uGUI 默认的 UI/Default 和 TextMeshPro 的 SDF 着色器都不画（用它们的东西整个不显示，
    /// 但普通摄像机、电脑上都正常，所以之前每一轮测试都测不到）。这里把所有界面图形换成 Diva/VR UI 材质；
    /// 文字的着色器在 DivaUi.MakeFont 时换成 Diva/VR Text（DivaShowDirector 里传入）。
    /// DivaUi 新建的图形直接带这个材质（DivaUi.GraphicMaterial，在 Diva Show 搭界面之前设好）；别处建的（Eva 的开场、键盘图例）
    /// 靠扫描补：开头每秒一次，之后每 6 秒和每次阶段切换后一次。以前每半秒扫一遍全场所有 Graphic，头显上是固定的小卡顿。
    /// </summary>
    public class DivaVRUiShaders : MonoBehaviour
    {
        Material ui;
        float next;

        void Start()
        {
            ui = Resources.Load<Material>("DivaVR UI");
            if (!ui) { Debug.LogWarning("DIVA_VR ui shaders: Resources/DivaVR UI missing"); enabled = false; return; }
            DivaUi.GraphicMaterial = ui;
            DivaShowDirector.PhaseChanged += OnPhase;
            Fix();
        }

        void OnDestroy() => DivaShowDirector.PhaseChanged -= OnPhase;

        void OnPhase(ShowPhase p) => next = Mathf.Min(next, Time.unscaledTime + .3f);

        void Update()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + (Time.unscaledTime < 12 ? 1f : 6f);
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
