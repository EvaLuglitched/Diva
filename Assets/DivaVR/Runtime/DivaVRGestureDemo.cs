using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 教学里的动作演示：牌子上方一个小小的 3D 动画（一个小头、两只手），循环做当前要学的那个动作，比文字清楚：
    /// 前倾（头往前探）/ 歪头 / 双手张开往前推 / 举手竖拇指 / 双手举到眼前 / 看着按钮捏。
    /// </summary>
    public class DivaVRGestureDemo : MonoBehaviour
    {
        public enum Move { Lean, Tilt, Push, ThumbsUp, HandsUp, Button }
        Move move; bool shown;
        Transform stage, handL, handR, head, thumb, index, target, arrow, button, seat;
        Material handMat, headMat, barMat, targetMat, arrowMat, buttonMat;
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
            seat = Prim(PrimitiveType.Cube, "Seat", new Vector3(0, -.06f, -.08f), new Vector3(.12f, .01f, .1f), barMat);
            target = Prim(PrimitiveType.Cylinder, "Target", new Vector3(.12f, .08f, .18f), new Vector3(.05f, .003f, .05f), targetMat); target.localRotation = Quaternion.Euler(90, 0, 0);
            arrow = Prim(PrimitiveType.Cube, "Arrow", Vector3.zero, new Vector3(.006f, .006f, .05f), arrowMat);
            buttonMat = new Material(glow); Tint(buttonMat, new Color(.5f, .95f, 1f) * 1.4f);
            button = Prim(PrimitiveType.Cube, "Button", new Vector3(.1f, .05f, .16f), new Vector3(.07f, .03f, .006f), buttonMat);
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
            bool pinchy = m == Move.Button;
            target.gameObject.SetActive(m == Move.Push);
            button.gameObject.SetActive(pinchy);
            thumb.gameObject.SetActive(pinchy || m == Move.ThumbsUp); index.gameObject.SetActive(pinchy);
            handL.gameObject.SetActive(m == Move.Push || m == Move.HandsUp || m == Move.Lean || m == Move.Tilt);
            handR.gameObject.SetActive(true);
            seat.gameObject.SetActive(m == Move.Lean);
            arrow.gameObject.SetActive(m == Move.Lean || m == Move.Push || m == Move.HandsUp || pinchy);
            head.localRotation = Quaternion.identity; head.localPosition = new Vector3(0, .11f, -.08f);
            handL.localPosition = new Vector3(-.065f, -.02f, .02f); handR.localPosition = new Vector3(.065f, -.02f, .02f);
            handL.localScale = handR.localScale = Vector3.one * .035f;
        }

        public void Hide() { shown = false; if (stage) stage.gameObject.SetActive(false); }

        void Update()
        {
            if (!shown) return;
            float t = Time.unscaledTime - t0, c = Mathf.Repeat(t, 2.4f) / 2.4f;   // 2.4 秒一轮
            float ease = Mathf.SmoothStep(0, 1, Mathf.PingPong(c * 2, 1));
            switch (move)
            {
                case Move.Lean:
                    // 坐着，头和双手一起往前探，前面的箭头跟着变长
                    head.localPosition = new Vector3(0, .11f - .02f * ease, -.08f + .07f * ease);
                    head.localRotation = Quaternion.Euler(18 * ease, 0, 0);
                    handL.localPosition = new Vector3(-.065f, -.02f, .02f + .04f * ease); handR.localPosition = new Vector3(.065f, -.02f, .02f + .04f * ease);
                    arrow.localPosition = new Vector3(0, .09f, .06f + .05f * ease); arrow.localRotation = Quaternion.identity; arrow.localScale = new Vector3(.006f, .006f, .03f + .05f * ease);
                    break;
                case Move.Tilt:
                {
                    // 头往左右歪（肩膀 = 双手不动）
                    float sw = Mathf.Sin(c * Mathf.PI * 2);
                    head.localRotation = Quaternion.Euler(0, 0, -sw * 28);
                    head.localPosition = new Vector3(sw * .02f, .11f, -.08f);
                    break;
                }
                case Move.Push:
                    // 两只手从胸前张开往前推，推出去时前面的靶子闪白
                    handL.localPosition = Vector3.Lerp(new Vector3(-.04f, .0f, -.02f), new Vector3(-.06f, .02f, .10f), ease);
                    handR.localPosition = Vector3.Lerp(new Vector3(.04f, .0f, -.02f), new Vector3(.06f, .02f, .10f), ease);
                    handL.localScale = handR.localScale = Vector3.one * (.028f + .012f * ease);
                    arrow.localPosition = new Vector3(0, .02f, .06f + .06f * ease); arrow.localRotation = Quaternion.identity; arrow.localScale = new Vector3(.006f, .006f, .05f);
                    Tint(targetMat, (ease > .85f ? Color.white : new Color(1f, .4f, .4f)) * 1.5f);
                    break;
                case Move.Button:
                {
                    // 看着按钮（头朝向它，一条视线），拇指食指捏合，捏下去那一刻它闪白
                    handR.localPosition = new Vector3(.03f, .02f, .05f);
                    float pinch = ease;
                    thumb.localPosition = handR.localPosition + new Vector3(-.02f + .012f * pinch, .005f, .02f);
                    index.localPosition = handR.localPosition + new Vector3(.02f - .012f * pinch, .012f, .022f);
                    head.localRotation = Quaternion.LookRotation(button.localPosition - head.localPosition);
                    arrow.localPosition = Vector3.Lerp(head.localPosition, button.localPosition, .5f);
                    arrow.localRotation = head.localRotation; arrow.localScale = new Vector3(.003f, .003f, Vector3.Distance(head.localPosition, button.localPosition));
                    Tint(buttonMat, (pinch > .9f ? Color.white : new Color(.5f, .95f, 1f)) * 1.4f);
                    break;
                }
                case Move.ThumbsUp:
                    // 右手举到头旁边，拇指（小球）竖在手上面
                    handR.localPosition = Vector3.Lerp(new Vector3(.065f, -.02f, .02f), new Vector3(.09f, .10f, -.02f), ease);
                    handR.localScale = Vector3.one * .03f;
                    thumb.localPosition = handR.localPosition + new Vector3(0, .026f * ease, 0); thumb.localScale = Vector3.one * (.006f + .01f * ease);
                    break;
                case Move.HandsUp:
                    // 双手举到眼睛高度、在前面
                    handL.localPosition = Vector3.Lerp(new Vector3(-.065f, -.02f, .02f), new Vector3(-.06f, .11f, .03f), ease);
                    handR.localPosition = Vector3.Lerp(new Vector3(.065f, -.02f, .02f), new Vector3(.06f, .11f, .03f), ease);
                    arrow.localPosition = new Vector3(0, .02f + .06f * ease, .04f); arrow.localRotation = Quaternion.Euler(-90, 0, 0); arrow.localScale = new Vector3(.006f, .006f, .05f);
                    break;
            }
        }
    }
}
