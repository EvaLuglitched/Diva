using Diva.Show;
using TMPro;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>世界里冒出来的飘字（+15、+分数、任务完成）：往上飘 0.8 米、1.2 秒淡掉，一直正对着人。</summary>
    public class DivaVRFloatText : MonoBehaviour
    {
        TextMeshProUGUI text; CanvasGroup group; float born; Vector3 from;

        public static void Show(string s, Vector3 at, Color colour, float size = 1f)
        {
            if (DivaUi.Font == null) return;
            var go = new GameObject("Float Text", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)) { layer = 5 };
            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(600, 160);
            rt.localScale = Vector3.one * (size * 1.2f / 600f);
            rt.position = at;
            var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace; c.worldCamera = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main; c.sortingOrder = 65;
            var f = go.AddComponent<DivaVRFloatText>();
            f.group = go.GetComponent<CanvasGroup>(); f.group.blocksRaycasts = false;
            f.text = DivaUi.Text(rt, "Text", new Vector2(.5f, .5f), Vector2.zero, new Vector2(600, 160), 110, colour);
            f.text.text = s;
            DivaUi.Glow(f.text, new Color(colour.r, colour.g, colour.b, .9f), .6f, .4f, .14f);
            f.born = Time.unscaledTime; f.from = at;
            Destroy(go, 1.4f);
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime - born;
            transform.position = from + Vector3.up * (.8f * DivaUi.EaseOut(t / 1.2f));
            float pop = DivaUi.EaseOutBack(t / .25f);
            transform.localScale = transform.localScale.normalized * transform.localScale.magnitude; // 保持
            group.alpha = Mathf.Clamp01(1 - (t - .7f) / .5f);
            var head = DivaVR.Head;
            if (head) { var to = transform.position - head.position; if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to); }
            text.rectTransform.localScale = Vector3.one * Mathf.Max(.01f, pop);
        }
    }
}
