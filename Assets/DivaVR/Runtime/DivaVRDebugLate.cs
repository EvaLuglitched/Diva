using System.Linq;
using Diva.Show;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Diva.VR
{
    /// <summary>排查用（见 DivaVRDebug）：这些要等别的组件 Start 过了再改。</summary>
    public class DivaVRDebugLate : MonoBehaviour
    {
        void Start()
        {
            var look = FindAnyObjectByType<DivaLook>();
            if (DivaVRDebug.Get("DIVA_RIM") == "0")
            {
                if (look) look.rimIntensity = 0;
                foreach (var l in FindObjectsByType<Light>()) if (l.name == "Diva Rim Light") l.intensity = 0;
                Debug.Log("DIVA_VR debug: rim light off");
            }
            if (DivaVRDebug.Get("DIVA_BLOOM") == "0") { if (look) look.bloomIntensity = 0; Debug.Log("DIVA_VR debug: bloom off"); }
            if (DivaVRDebug.On("DIVA_UIPROBE")) Invoke(nameof(Probe), 1f);
        }

        /// <summary>
        /// 对照实验：眼前一排五样东西，哪种画法在头显的立体渲染里看得见。从左到右：
        /// 绿方块（URP 无光照网格）、白方块（网格 + uGUI 默认材质）、3D 文字（TextMeshPro 网格）、Canvas 图片、Canvas 文字。
        /// </summary>
        void Probe()
        {
            var rig = GetComponent<DivaVRRig>();
            float eye = rig ? rig.eyeHeight : .78f;
            var root = new GameObject("UI Probe").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(0, eye + .12f, 1.2f);
            GameObject Quad(string name, float x, Material m)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                var c = q.GetComponent<Collider>(); if (c) Destroy(c);
                q.name = name; q.transform.SetParent(root, false);
                q.transform.localPosition = new Vector3(x, 0, 0); q.transform.localScale = Vector3.one * .18f;
                q.GetComponent<MeshRenderer>().sharedMaterial = m;
                return q;
            }
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit) Quad("Probe Unlit", -.5f, new Material(unlit) { color = Color.green });
            Quad("Probe UI Material", -.25f, new Material(Canvas.GetDefaultCanvasMaterial()));
            var t3 = new GameObject("Probe 3D Text").AddComponent<TextMeshPro>();
            t3.transform.SetParent(root, false); t3.transform.localPosition = Vector3.zero; t3.transform.localScale = Vector3.one * .01f;
            if (DivaUi.Font) t3.font = DivaUi.Font;
            t3.text = "3D"; t3.fontSize = 36; t3.alignment = TextAlignmentOptions.Center; t3.color = Color.yellow;
            t3.rectTransform.sizeDelta = new Vector2(30, 20);
            var cv = new GameObject("Probe Canvas", typeof(RectTransform), typeof(Canvas)) { layer = 5 };
            var crt = (RectTransform)cv.transform; crt.SetParent(root, false); crt.localPosition = new Vector3(.4f, 0, 0);
            crt.sizeDelta = new Vector2(500, 200); crt.localScale = Vector3.one * .001f;
            var canvas = cv.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main;
            DivaUi.Image(crt, "Probe Image", new Vector2(.5f, .5f), new Vector2(-120, 0), new Vector2(160, 160), Color.magenta);
            var tc = DivaUi.Text(crt, "Probe Text", new Vector2(.5f, .5f), new Vector2(120, 0), new Vector2(220, 160), 90, Color.cyan);
            tc.text = "UI";
            Debug.Log("DIVA_VR debug: UI probe placed (unlit quad, UI-material quad, 3D text, canvas image, canvas text)");
        }
    }
}
