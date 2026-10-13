using UnityEngine;
using UnityEngine.UI;

namespace Diva.VR
{
    /// <summary>
    /// VR 教学牌上的火柴人（照 Eva 摄像头识别页的画法：白色线条、没有背景），演示 Vision Pro 的五个动作：
    /// Lean = 坐着身体前倾（侧面看，前面一支箭头）；Tilt = 歪头，左右来回，头上弯箭头；Push = 握拳到胸前再张开往前推；
    /// ThumbsUp = 举手竖大拇指；HandsUp = 双手举到眼前举住，手上一条进度条慢慢充满。
    /// 坐标以身高为 1（脚底 0，头顶约 0.97）；像照镜子：往画面左边歪 = 玩家往自己左边歪 = 左转。
    /// uGUI 网格画线，VR 的立体渲染靠 DivaVRUiShaders 把材质换成 Diva/VR UI。
    /// </summary>
    public class DivaVRStickFigure : MaskableGraphic
    {
        public enum Act { Lean, Tilt, Push, ThumbsUp, HandsUp }
        public Act act;
        [Tooltip("Line width in pixels.")]
        public float thickness = 5;
        /// <summary>0 = 淡（别的动作在演示），1 = 当前动作。</summary>
        [System.NonSerialized] public float emphasis = 1;
        /// <summary>When the move starts from its first pose (Time.unscaledTime).</summary>
        [System.NonSerialized] public float startTime;

        const float LeanPeriod = 3.0f, TiltPeriod = 3.2f, PushPeriod = 2.3f, ThumbPeriod = 2.6f, HandsPeriod = 3.0f;
        const float Head = .11f;
        static readonly Vector2 ShoulderL = new Vector2(-.16f, .68f), ShoulderR = new Vector2(.16f, .68f), HeadAt = new Vector2(0, .86f);

        float Now => Time.unscaledTime - startTime;
        VertexHelper vh;
        Vector2 origin;
        float scale;

        /// <summary>这一刻该配的小字（动作名）。</summary>
        public string CaptionKey => act == Act.Lean ? "vr.cap.lean" : act == Act.Tilt ? (TiltAmount(Now) < 0 ? "vr.cap.left" : "vr.cap.right")
                                  : act == Act.Push ? "vr.cap.push" : act == Act.ThumbsUp ? "vr.cap.thumb" : "vr.cap.hands";

        void Update() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            vh = v;
            var r = GetPixelAdjustedRect();
            scale = r.height;
            origin = new Vector2(r.center.x, r.yMin);
            float t = Now;
            switch (act)
            {
                case Act.Lean: Lean(t); break;
                case Act.Tilt: Tilt(t); break;
                case Act.Push: Push(t); break;
                case Act.ThumbsUp: Thumb(t); break;
                default: Hands(t); break;
            }
            vh = null;
        }

        // ------------------------------------------------------------------ the moves

        /// <summary>坐在座位上（侧面），上身从直立往前倾再回来；前面一支箭头随前倾变长（= 快）。</summary>
        void Lean(float t)
        {
            float u = Mathf.Repeat(t, LeanPeriod) / LeanPeriod;
            float lean = u < .3f ? DivaSmooth(u / .3f) : u < .6f ? 1 : u < .85f ? 1 - DivaSmooth((u - .6f) / .25f) : 0;
            // 座位：一条横线和靠背
            var hip = new Vector2(-.02f, .40f);
            Line(new Vector2(-.26f, .40f), new Vector2(.20f, .40f)); Line(new Vector2(-.26f, .40f), new Vector2(-.26f, .62f));
            Line(new Vector2(.20f, .40f), new Vector2(.20f, .02f));   // 座位腿
            // 大腿往前、小腿往下
            var knee = hip + new Vector2(.26f, .0f); var foot = knee + new Vector2(.02f, -.36f);
            Chain(hip, knee, foot, foot + new Vector2(.07f, 0));
            // 上身：从髋往上，前倾时整个绕髋转
            float angle = -lean * 28;   // 顺时针 = 往画面右边（前方）倒
            var shoulder = hip + Rotate(new Vector2(0, .30f), angle);
            var head = hip + Rotate(new Vector2(0, .30f + .18f), angle);
            Line(hip, shoulder);
            Circle(head, Head);
            Line(shoulder, head + Rotate(new Vector2(0, -Head), angle));
            // 手臂放在腿上
            var elbow = shoulder + Rotate(new Vector2(.08f, -.18f), angle);
            Chain(shoulder, elbow, elbow + new Vector2(.12f, -.06f));
            Dot(elbow + new Vector2(.12f, -.06f), .03f);
            // 前方的箭头：越倾越长
            if (lean > .05f)
            {
                var a = new Vector2(.36f, .62f); var b = a + new Vector2(.08f + .22f * lean, 0);
                Line(a, b); Line(b, b + new Vector2(-.06f, .05f)); Line(b, b + new Vector2(-.06f, -.05f));
            }
        }

        /// <summary>-1 = 往画面左歪，1 = 右；每边停一下，中间平滑过渡。</summary>
        static float TiltAmount(float t)
        {
            float u = Mathf.Repeat(t, TiltPeriod) / TiltPeriod;
            return u < .35f ? -1 : u < .5f ? Mathf.Lerp(-1, 1, DivaSmooth((u - .35f) / .15f))
                 : u < .85f ? 1 : Mathf.Lerp(1, -1, DivaSmooth((u - .85f) / .15f));
        }

        /// <summary>歪头：肩膀不动，头往一边歪，头上一支弯箭头指方向。</summary>
        void Tilt(float t)
        {
            float tilt = TiltAmount(t);
            float headAngle = -tilt * 30;
            var neckTop = new Vector2(0, .68f) + Rotate(new Vector2(0, .18f), headAngle);
            Body(neckTop, headAngle, ShoulderL, ShoulderR);
            HangingArm(ShoulderL, -1);
            HangingArm(ShoulderR, 1);
            var arc = new Vector2[6];
            for (int k = 0; k < arc.Length; k++)
                arc[k] = new Vector2(0, .68f) + Rotate(new Vector2(0, .36f), -tilt * (8 + 34 * k / (arc.Length - 1f)));
            Chain(arc);
            var tip = arc[arc.Length - 1]; var back = (arc[arc.Length - 2] - tip).normalized * .05f;
            Line(tip, tip + Rotate(back, 35)); Line(tip, tip + Rotate(back, -35));
        }

        /// <summary>喷水：握拳到胸前，然后两只张开的手往前推、停住、收回（和电脑版一样）。</summary>
        void Push(float t)
        {
            float u = Mathf.Repeat(t, PushPeriod);
            Body(HeadAt, 0, ShoulderL, ShoulderR);
            float push = u < .35f ? 0 : u < .65f ? DivaSmooth((u - .35f) / .3f) : u < 1.75f ? 1 : DivaSmooth(1 - (u - 1.75f) / .3f);
            foreach (var (shoulder, side) in new[] { (ShoulderL, -1f), (ShoulderR, 1f) })
            {
                var elbow = Vector2.Lerp(shoulder + new Vector2(side * .05f, -.22f), shoulder + new Vector2(side * .07f, -.17f), push);
                var hand = Vector2.Lerp(new Vector2(side * .08f, .55f), new Vector2(side * .20f, .58f), push);
                Chain(shoulder, elbow, hand);
                if (push < .3f) Dot(hand, .034f);
                else Palm(hand, Mathf.Lerp(.9f, 1.4f, push), side);
                if (push > .8f)
                {
                    var c = hand + new Vector2(side * .11f, 0);
                    Chain(c + new Vector2(-side * .02f, .06f), c + new Vector2(side * .005f, 0), c + new Vector2(-side * .02f, -.06f));
                }
            }
        }

        /// <summary>竖大拇指：右手举到头旁边竖起拇指，保持，然后放下。</summary>
        void Thumb(float t)
        {
            float u = Mathf.Repeat(t, ThumbPeriod);
            float raise = DivaSmooth(Mathf.Min(u / .3f, (ThumbPeriod - u) / .3f));
            Body(HeadAt, 0, ShoulderL, ShoulderR);
            HangingArm(ShoulderL, -1);
            var elbowR = Vector2.Lerp(ShoulderR + new Vector2(.045f, -.19f), ShoulderR + new Vector2(.12f, -.15f), raise);
            var handR = Vector2.Lerp(elbowR + new Vector2(.005f, -.16f), new Vector2(.27f, .80f), raise);
            Chain(ShoulderR, elbowR, handR);
            if (raise < .5f) Dot(handR, .032f);
            else ThumbsUp(handR, 1 + .12f * Mathf.Sin(u * 9));
        }

        /// <summary>大招：双手举到眼睛高度、手心朝前，举住；两手之间一条进度条慢慢充满。</summary>
        void Hands(float t)
        {
            float u = Mathf.Repeat(t, HandsPeriod);
            float raise = DivaSmooth(Mathf.Min(u / .4f, (HandsPeriod - u) / .3f));
            Body(HeadAt, 0, ShoulderL, ShoulderR);
            foreach (var (shoulder, side) in new[] { (ShoulderL, -1f), (ShoulderR, 1f) })
            {
                var elbow = Vector2.Lerp(shoulder + new Vector2(side * .045f, -.19f), shoulder + new Vector2(side * .14f, -.02f), raise);
                var hand = Vector2.Lerp(elbow + new Vector2(side * .005f, -.16f), new Vector2(side * .24f, .86f), raise);
                Chain(shoulder, elbow, hand);
                if (raise < .5f) Dot(hand, .032f); else Palm(hand, 1.2f, side);
            }
            if (raise > .9f)
            {
                float hold = Mathf.Clamp01((u - .4f) / 1.4f);
                Line(new Vector2(-.15f, 1.0f), new Vector2(.15f, 1.0f), thickness * .6f);
                if (hold > .02f) Line(new Vector2(-.15f, 1.0f), new Vector2(-.15f + .30f * hold, 1.0f), thickness * 1.6f);
            }
        }

        // ------------------------------------------------------------------ body parts（照 Eva 的比例：大头、长手、短腿）

        void Body(Vector2 head, float headAngle, Vector2 shL, Vector2 shR)
        {
            var hipL = new Vector2(-.11f, .34f); var hipR = new Vector2(.11f, .34f);
            Circle(head, Head);
            Line(head + Rotate(new Vector2(0, -Head), headAngle), (shL + shR) * .5f);
            Chain(shL, shR, hipR, hipL, shL);
            var footL = new Vector2(-.12f, .03f); var footR = new Vector2(.12f, .03f);
            Chain(hipL, footL, footL + new Vector2(-.045f, 0));
            Chain(hipR, footR, footR + new Vector2(.045f, 0));
        }

        void HangingArm(Vector2 shoulder, float side)
        {
            var elbow = shoulder + new Vector2(side * .045f, -.19f);
            var hand = elbow + new Vector2(side * .005f, -.16f);
            Chain(shoulder, elbow, hand);
            Dot(hand, .032f);
        }

        void Palm(Vector2 c, float size, float side)
        {
            float w = thickness * .75f;
            Circle(c, .03f * size, w);
            for (int i = 0; i < 4; i++)
            {
                float dx = (-.024f + .016f * i) * size;
                Line(c + new Vector2(dx * .8f, .028f * size), c + new Vector2(dx * 1.25f, .075f * size), w);
            }
            Line(c + new Vector2(-side * .028f * size, 0), c + new Vector2(-side * .065f * size, .03f * size), w);
        }

        void ThumbsUp(Vector2 c, float thumb)
        {
            float w = thickness * .8f, hw = .055f, hh = .044f, r = .02f;
            var box = new System.Collections.Generic.List<Vector2>();
            foreach (var (cx, cy, a0) in new[] { (hw - r, hh - r, 0f), (-hw + r, hh - r, 90f), (-hw + r, -hh + r, 180f), (hw - r, -hh + r, 270f) })
                for (int k = 0; k <= 3; k++)
                    box.Add(c + new Vector2(cx, cy) + Rotate(new Vector2(r, 0), a0 + 30 * k));
            box.Add(box[0]);
            for (int i = 1; i < box.Count; i++) Line(box[i - 1], box[i], w);
            Line(c + new Vector2(-hw, .01f), c + new Vector2(-.01f, .01f), w);
            Line(c + new Vector2(-hw, -.015f), c + new Vector2(-.01f, -.015f), w);
            Line(c + new Vector2(-.028f, hh), c + new Vector2(-.033f, hh + .09f * thumb), thickness * 1.5f);
        }

        // ------------------------------------------------------------------ drawing（身高单位 → 像素）

        static float DivaSmooth(float x) { x = Mathf.Clamp01(x); return x * x * (3 - 2 * x); }
        Vector2 Px(Vector2 p) => origin + p * scale;
        static Vector2 Rotate(Vector2 v, float degrees) => Quaternion.Euler(0, 0, degrees) * v;
        Color32 Col => new Color(color.r, color.g, color.b, color.a * Mathf.Lerp(.28f, 1f, emphasis));

        void Chain(params Vector2[] points) { for (int i = 1; i < points.Length; i++) Line(points[i - 1], points[i]); }

        void Line(Vector2 a, Vector2 b, float width = -1)
        {
            if (width < 0) width = thickness;
            Vector2 pa = Px(a), pb = Px(b), d = pb - pa;
            if (d.sqrMagnitude < 1e-4f) return;
            var n = new Vector2(-d.y, d.x).normalized * width * .5f;
            int i = vh.currentVertCount; var c = Col;
            vh.AddVert(pa - n, c, Vector2.zero); vh.AddVert(pa + n, c, Vector2.zero);
            vh.AddVert(pb + n, c, Vector2.zero); vh.AddVert(pb - n, c, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
            Disc(pa, width * .5f, 8); Disc(pb, width * .5f, 8);
        }

        void Dot(Vector2 c, float r) => Disc(Px(c), r * scale, 16);

        void Circle(Vector2 c, float r, float width = -1)
        {
            if (width < 0) width = thickness;
            var pc = Px(c); var col = Col;
            float outer = r * scale + width * .5f, inner = Mathf.Max(0, r * scale - width * .5f);
            const int n = 32;
            int start = vh.currentVertCount;
            for (int k = 0; k <= n; k++)
            {
                float a = 2 * Mathf.PI * k / n;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(pc + dir * outer, col, Vector2.zero);
                vh.AddVert(pc + dir * inner, col, Vector2.zero);
                if (k > 0) { int i = start + 2 * (k - 1); vh.AddTriangle(i, i + 2, i + 1); vh.AddTriangle(i + 1, i + 2, i + 3); }
            }
        }

        void Disc(Vector2 pc, float radius, int n)
        {
            int centre = vh.currentVertCount; var col = Col;
            vh.AddVert(pc, col, Vector2.zero);
            for (int k = 0; k <= n; k++)
            {
                float a = 2 * Mathf.PI * k / n;
                vh.AddVert(pc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, col, Vector2.zero);
                if (k > 0) vh.AddTriangle(centre, centre + k, centre + k + 1);
            }
        }
    }
}
