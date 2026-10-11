using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 教学里的动作演示：牌子上方一个小小的 3D 动画（两只手、一个小头、迷你把手），循环做当前要学的那个动作，
    /// 比文字清楚：推把手 / 转把手 / 看着靶子捏 / 手放到嘴边 / 双手举到眼前。
    /// </summary>
    public class DivaVRGestureDemo : MonoBehaviour
    {
        public enum Move { Push, Twist, Pinch, Mouth, HandsUp }
        Move move; bool shown;
        Transform stage, handL, handR, head, thumb, index, gripL, gripR, bar, target, arrow;
        Material handMat, headMat, barMat, targetMat, arrowMat;
        float t0;

        public static DivaVRGestureDemo Create(Transform parent, Vector3 localPos, Material glow, Material body)
        {
            var go = new GameObject("Gesture Demo");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var d = go.AddComponent<DivaVRGestureDemo>();
            d.Build(glow, body);
            return d;
        }

        void Build(Material glow, Material body)
        {
            stage = new GameObject("Stage").transform;
            stage.SetParent(transform, false);
            handMat = new Material(glow); Tint(handMat, new Color(1f, .8f, .9f) * 1.3f);
            headMat = new Material(body); Tint(headMat, new Color(.9f, .85f, 1f));
            barMat = new Material(body); Tint(barMat, new Color(.25f, .2f, .35f));
            targetMat = new Material(glow); Tint(targetMat, new Color(1f, .4f, .4f) * 1.5f);
            arrowMat = new Material(glow); Tint(arrowMat, new Color(.5f, 1f, .8f) * 1.6f);
            head = Prim(PrimitiveType.Sphere, "Head", new Vector3(0, .11f, -.08f), Vector3.one * .07f, headMat);
            handL = Prim(PrimitiveType.Sphere, "Hand L", new Vector3(-.06f, 0, 0), Vector3.one * .035f, handMat);
            handR = Prim(PrimitiveType.Sphere, "Hand R", new Vector3(.06f, 0, 0), Vector3.one * .035f, handMat);
            thumb = Prim(PrimitiveType.Sphere, "Thumb", Vector3.zero, Vector3.one * .014f, handMat);
            index = Prim(PrimitiveType.Sphere, "Index", Vector3.zero, Vector3.one * .014f, handMat);
            bar = Prim(PrimitiveType.Cylinder, "Bar", new Vector3(0, -.02f, .02f), new Vector3(.008f, .075f, .008f), barMat);
            bar.localRotation = Quaternion.Euler(0, 0, 90);
            gripL = Prim(PrimitiveType.Cylinder, "Grip L", new Vector3(-.065f, -.02f, .02f), new Vector3(.014f, .018f, .014f), handMat); gripL.localRotation = Quaternion.Euler(0, 0, 90);
            gripR = Prim(PrimitiveType.Cylinder, "Grip R", new Vector3(.065f, -.02f, .02f), new Vector3(.014f, .018f, .014f), handMat); gripR.localRotation = Quaternion.Euler(0, 0, 90);
            target = Prim(PrimitiveType.Cylinder, "Target", new Vector3(.12f, .08f, .18f), new Vector3(.05f, .003f, .05f), targetMat); target.localRotation = Quaternion.Euler(90, 0, 0);
            arrow = Prim(PrimitiveType.Cube, "Arrow", Vector3.zero, new Vector3(.006f, .006f, .05f), arrowMat);
            Hide();
        }

        Transform Prim(PrimitiveType type, string name, Vector3 pos, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            var c = go.GetComponent<Collider>(); if (c) Destroy(c);
            go.name = name; go.transform.SetParent(stage, false);
            go.transform.localPosition = pos; go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        static void Tint(Material m, Color c) { m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); }

        public void Show(Move m)
        {
            move = m; shown = true; t0 = Time.unscaledTime;
            stage.gameObject.SetActive(true);
            bool bars = false;   // 把手演示不用了（操控改回前倾 / 侧倾）
            bar.gameObject.SetActive(bars); gripL.gameObject.SetActive(bars); gripR.gameObject.SetActive(bars);
            target.gameObject.SetActive(m == Move.Pinch);
            thumb.gameObject.SetActive(m == Move.Pinch); index.gameObject.SetActive(m == Move.Pinch);
            handL.gameObject.SetActive(m != Move.Pinch && m != Move.Mouth);
            arrow.gameObject.SetActive(m == Move.Push || m == Move.HandsUp || m == Move.Pinch);
        }

        public void Hide() { shown = false; if (stage) stage.gameObject.SetActive(false); }

        void Update()
        {
            if (!shown) return;
            float t = Time.unscaledTime - t0, c = Mathf.Repeat(t, 2f) / 2f;   // 两秒一轮
            float ease = Mathf.SmoothStep(0, 1, Mathf.PingPong(c * 2, 1));
            switch (move)
            {
                case Move.Push:
                    // 拳头（小一点的手）握着杆往前推
                    handR.localScale = Vector3.one * .028f; handL.gameObject.SetActive(false);
                    handR.localPosition = new Vector3(.03f, .01f, .0f + .06f * ease);
                    bar.gameObject.SetActive(true); bar.localRotation = Quaternion.Euler(ease * 30, 0, 0); bar.localPosition = new Vector3(.03f, -.03f, 0); bar.localScale = new Vector3(.006f, .03f, .006f);
                    gripL.gameObject.SetActive(true); gripL.localRotation = Quaternion.identity; gripL.localScale = new Vector3(.05f, .003f, .05f); gripL.localPosition = new Vector3(.03f, -.06f, 0);
                    arrow.localPosition = new Vector3(.03f, .06f, .03f + .04f * ease); arrow.localRotation = Quaternion.identity;
                    break;
                case Move.Twist:
                    // 拳头往左右推杆
                    float sw = Mathf.Sin(c * Mathf.PI * 2) * .05f;
                    handR.localScale = Vector3.one * .028f; handL.gameObject.SetActive(false);
                    handR.localPosition = new Vector3(.03f + sw, .01f, 0);
                    bar.gameObject.SetActive(true); bar.localRotation = Quaternion.Euler(0, 0, -sw / .05f * 30); bar.localPosition = new Vector3(.03f, -.03f, 0); bar.localScale = new Vector3(.006f, .03f, .006f);
                    gripL.gameObject.SetActive(true); gripL.localRotation = Quaternion.identity; gripL.localScale = new Vector3(.05f, .003f, .05f); gripL.localPosition = new Vector3(.03f, -.06f, 0);
                    break;
                case Move.Pinch:
                    // 看着靶子（头朝向靶子），拇指食指捏合，靶子闪
                    handR.localPosition = new Vector3(.03f, .02f, .05f);
                    float pinch = ease;
                    thumb.localPosition = handR.localPosition + new Vector3(-.02f + .012f * pinch, .005f, .02f);
                    index.localPosition = handR.localPosition + new Vector3(.02f - .012f * pinch, .012f, .022f);
                    head.localRotation = Quaternion.LookRotation(target.localPosition - head.localPosition);
                    arrow.localPosition = Vector3.Lerp(head.localPosition, target.localPosition, .5f);
                    arrow.localRotation = head.localRotation; arrow.localScale = new Vector3(.003f, .003f, Vector3.Distance(head.localPosition, target.localPosition));
                    Tint(targetMat, (pinch > .9f ? Color.white : new Color(1f, .4f, .4f)) * 1.5f);
                    break;
                case Move.Mouth:
                    // 手从前面抬到嘴边
                    handR.localPosition = Vector3.Lerp(new Vector3(.05f, -.02f, .06f), new Vector3(.0f, .08f, -.03f), ease);
                    break;
                case Move.HandsUp:
                    // 双手举到眼睛高度、在前面
                    handL.localPosition = Vector3.Lerp(new Vector3(-.065f, -.02f, .02f), new Vector3(-.06f, .11f, .03f), ease);
                    handR.localPosition = Vector3.Lerp(new Vector3(.065f, -.02f, .02f), new Vector3(.06f, .11f, .03f), ease);
                    arrow.localPosition = new Vector3(0, .02f + .06f * ease, .04f); arrow.localRotation = Quaternion.Euler(-90, 0, 0);
                    break;
            }
            if (move != Move.Pinch) { head.localRotation = Quaternion.identity; arrow.localScale = new Vector3(.006f, .006f, .05f); }
            if (move != Move.Push && move != Move.Twist) { head.localPosition = new Vector3(0, .11f, -.08f); handR.localScale = Vector3.one * .035f; }
        }
    }
}
