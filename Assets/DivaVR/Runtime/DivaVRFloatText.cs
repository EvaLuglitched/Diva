using Diva.Show;
using TMPro;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 世界里冒出来的飘字（+15、+分数、任务完成）：往上飘 0.8 米、1.2 秒淡掉，一直正对着人。
    /// 从池子里拿（一局几百次：每次新建一个世界画布 + 字体材质实例再销毁，头显上就是一下一下的卡）。
    /// </summary>
    public class DivaVRFloatText : MonoBehaviour
    {
        TextMeshProUGUI text; CanvasGroup group; float born; Vector3 from; Material glowMat;
        static readonly System.Collections.Generic.Stack<DivaVRFloatText> pool = new System.Collections.Generic.Stack<DivaVRFloatText>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => pool.Clear();

        public static void Show(string s, Vector3 at, Color colour, float size = 1f)
        {
            if (DivaUi.Font == null) return;
            DivaVRFloatText f = null;
            while (pool.Count > 0 && !f) f = pool.Pop();
            if (!f)
            {
                var go = new GameObject("Float Text", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup)) { layer = 5 };
                var rt = (RectTransform)go.transform;
                rt.sizeDelta = new Vector2(600, 160);
                var c = go.GetComponent<Canvas>(); c.renderMode = RenderMode.WorldSpace; c.worldCamera = DivaVR.Head ? DivaVR.Head.GetComponent<Camera>() : Camera.main; c.sortingOrder = 65;
                f = go.AddComponent<DivaVRFloatText>();
                f.group = go.GetComponent<CanvasGroup>(); f.group.blocksRaycasts = false;
                f.text = DivaUi.Text(rt, "Text", new Vector2(.5f, .5f), Vector2.zero, new Vector2(600, 160), 110, colour);
                DivaUi.Glow(f.text, new Color(colour.r, colour.g, colour.b, .9f), .6f, .4f, .14f);
                f.glowMat = f.text.fontMaterial;
            }
            f.gameObject.SetActive(true);
            f.transform.localScale = Vector3.one * (size * 1.2f / 600f);
            f.transform.position = at;
            f.text.text = s; f.text.color = colour;
            if (f.glowMat) f.glowMat.SetColor("_UnderlayColor", new Color(colour.r, colour.g, colour.b, .9f));
            f.group.alpha = 1;
            f.born = Time.unscaledTime; f.from = at;
        }

        void LateUpdate()
        {
            float t = Time.unscaledTime - born;
            if (t > 1.4f) { gameObject.SetActive(false); pool.Push(this); return; }
            transform.position = from + Vector3.up * (.8f * DivaUi.EaseOut(t / 1.2f));
            float pop = DivaUi.EaseOutBack(t / .25f);
            group.alpha = Mathf.Clamp01(1 - (t - .7f) / .5f);
            var head = DivaVR.Head;
            if (head) { var to = transform.position - head.position; if (to.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(to); }
            text.rectTransform.localScale = Vector3.one * Mathf.Max(.01f, pop);
        }
    }
}
