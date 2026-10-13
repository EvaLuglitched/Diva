using UnityEngine;
using UnityEngine.UI;

namespace Diva.Show
{
    /// <summary>
    /// 摄像头识别页的火柴人动画（Eva 的手绘稿）：白色线条、没有背景，演示每个玩家要做的动作。
    /// Walk = 双臂交替上下摆；Turn = 压肩 + 歪头，左右来回；Shoot = 握拳到胸前再张开往前推；Drink = 举手竖大拇指（喝水 / 吃）。
    /// 坐标以身高为 1（脚底 0，头顶约 0.97）；像照镜子：往画面左边歪 = 玩家往自己左边歪 = 左转。
    /// </summary>
    public class DivaStickFigure : MaskableGraphic
    {
        public enum Act { Walk, Turn, Shoot, Drink }
        public Act act;
        [Tooltip("Line width in pixels.")]
        public float thickness = 5;

        const float WalkHz = 1.5f, TurnPeriod = 3.2f, ShootPeriod = 2.3f, DrinkPeriod = 2.6f;
        const float Head = .11f;

        /// <summary>When the moves start from their first pose (Time.unscaledTime).</summary>
        [System.NonSerialized] public float startTime;
        /// <summary>Pins the animation to one moment (previews); NaN = live time.</summary>
        float pinnedTime = float.NaN;
        float Now => float.IsNaN(pinnedTime) ? Time.unscaledTime - startTime : pinnedTime;

        /// <summary>Text key for the move being shown right now (the caption under the figure).</summary>
        public string CaptionKey => act == Act.Walk ? "check.cap.swing"
                                  : act == Act.Turn ? (TurnTilt(Now) < 0 ? "check.cap.left" : "check.cap.right")
                                  : act == Act.Shoot ? "check.cap.shoot" : "check.cap.drink";

        VertexHelper vh;
        Vector2 origin;
        float scale;

        void Update() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper v)
        {
            v.Clear();
            vh = v;
            var r = GetPixelAdjustedRect();
            scale = r.height;
            origin = new Vector2(r.center.x, r.yMin);
            float t = Now;
            if (act == Act.Walk) Walk(t);
            else if (act == Act.Turn) Turn(t);
            else if (act == Act.Shoot) Shoot(t);
            else Drink(t);
            vh = null;
        }

        // ------------------------------------------------------------------ the moves
        // Proportions favour the upper body (big head, long arms, short legs): every move happens above the hips.

        static readonly Vector2 ShoulderL = new Vector2(-.16f, .68f), ShoulderR = new Vector2(.16f, .68f), HeadAt = new Vector2(0, .86f);

        /// <summary>P1: both arms pump up and down in turn, like running on the spot.</summary>
        void Walk(float t)
        {
            float w = t * 2 * Mathf.PI * WalkHz;
            origin.y += .015f * scale * Mathf.Abs(Mathf.Sin(w));
            Body(HeadAt, 0, ShoulderL, ShoulderR);
            PumpArm(ShoulderL, .5f + .5f * Mathf.Sin(w), -1);
            PumpArm(ShoulderR, .5f - .5f * Mathf.Sin(w), 1);
        }

        void PumpArm(Vector2 shoulder, float up, float side)
        {
            var elbow = shoulder + new Vector2(side * Mathf.Lerp(.07f, .13f, up), Mathf.Lerp(-.19f, -.12f, up));
            var hand = elbow + new Vector2(side * Mathf.Lerp(.01f, -.01f, up), Mathf.Lerp(-.15f, .27f, up));
            Chain(shoulder, elbow, hand);
            Dot(hand, .032f);
        }

        void HangingArm(Vector2 shoulder, float side)
        {
            var elbow = shoulder + new Vector2(side * .045f, -.19f);
            var hand = elbow + new Vector2(side * .005f, -.16f);
            Chain(shoulder, elbow, hand);
            Dot(hand, .032f);
        }

        /// <summary>-1 = tilted to the left of the picture, 1 = right; holds each side, eases between.</summary>
        static float TurnTilt(float t)
        {
            float u = Mathf.Repeat(t, TurnPeriod) / TurnPeriod;
            return u < .35f ? -1 : u < .5f ? Mathf.Lerp(-1, 1, DivaUi.Smooth((u - .35f) / .15f))
                 : u < .85f ? 1 : Mathf.Lerp(1, -1, DivaUi.Smooth((u - .85f) / .15f));
        }

        /// <summary>P2: drop one shoulder and tilt the head the same way (hips stay level), then the other side.</summary>
        void Turn(float t)
        {
            float tilt = TurnTilt(t);
            var mid = new Vector2(tilt * .03f, .68f - .01f * Mathf.Abs(tilt));
            var half = Rotate(new Vector2(.16f, 0), -tilt * 18);
            var shL = mid - half; var shR = mid + half;
            float headAngle = -tilt * 28;
            Body(mid + Rotate(new Vector2(0, .18f), headAngle), headAngle, shL, shR);
            HangingArm(shL, -1);
            HangingArm(shR, 1);
            // curved arrow over the head showing the way to lean
            var arc = new Vector2[6];
            for (int k = 0; k < arc.Length; k++)
                arc[k] = new Vector2(0, .68f) + Rotate(new Vector2(0, .36f), -tilt * (8 + 34 * k / (arc.Length - 1f)));
            Chain(arc);
            var tip = arc[arc.Length - 1]; var back = (arc[arc.Length - 2] - tip).normalized * .05f;
            Line(tip, tip + Rotate(back, 35)); Line(tip, tip + Rotate(back, -35));
        }

        /// <summary>P3, spray: fists at the chest, then both open hands pushed forward, held, and back.</summary>
        void Shoot(float t)
        {
            float u = Mathf.Repeat(t, ShootPeriod);
            Body(HeadAt, 0, ShoulderL, ShoulderR);
            float push = u < .35f ? 0 : u < .65f ? DivaUi.Smooth((u - .35f) / .3f) : u < 1.75f ? 1 : DivaUi.Smooth(1 - (u - 1.75f) / .3f);
            foreach (var (shoulder, side) in new[] { (ShoulderL, -1f), (ShoulderR, 1f) })
            {
                var elbow = Vector2.Lerp(shoulder + new Vector2(side * .05f, -.22f), shoulder + new Vector2(side * .07f, -.17f), push);
                var hand = Vector2.Lerp(new Vector2(side * .08f, .55f), new Vector2(side * .20f, .58f), push);
                Chain(shoulder, elbow, hand);
                if (push < .3f) Dot(hand, .034f);
                else Palm(hand, Mathf.Lerp(.9f, 1.4f, push), side);
                // "push!" marks beside the hands once they are out
                if (push > .8f)
                {
                    var c = hand + new Vector2(side * .11f, 0);
                    Chain(c + new Vector2(-side * .02f, .06f), c + new Vector2(side * .005f, 0), c + new Vector2(-side * .02f, -.06f));
                }
            }
        }

        /// <summary>P3, drink (and eat / refill): the right arm comes up beside the head and holds a thumbs-up; drops briefly each loop.</summary>
        void Drink(float t)
        {
            float u = Mathf.Repeat(t, DrinkPeriod);
            float raise = DivaUi.Smooth(Mathf.Min(u / .3f, (DrinkPeriod - u) / .3f));
            Body(HeadAt, 0, ShoulderL, ShoulderR);
            HangingArm(ShoulderL, -1);
            var elbowR = Vector2.Lerp(ShoulderR + new Vector2(.045f, -.19f), ShoulderR + new Vector2(.12f, -.15f), raise);
            var handR = Vector2.Lerp(elbowR + new Vector2(.005f, -.16f), new Vector2(.27f, .80f), raise);
            Chain(ShoulderR, elbowR, handR);
            if (raise < .5f) Dot(handR, .032f);
            else ThumbsUp(handR, 1 + .12f * Mathf.Sin(u * 9));
        }

        /// <summary>A fist seen from the front (rounded box, curled fingers toward the head) with the thumb sticking up.</summary>
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

        // ------------------------------------------------------------------ body parts

        /// <summary>Head (tilted by headAngle degrees), neck, torso outline (shoulders to level hips) and straight legs.</summary>
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

        /// <summary>Open hand facing the viewer: palm, four fingers and the thumb toward the body.</summary>
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

        // ------------------------------------------------------------------ drawing (figure units → pixels)

        Vector2 Px(Vector2 p) => origin + p * scale;
        static Vector2 Rotate(Vector2 v, float degrees) => Quaternion.Euler(0, 0, degrees) * v;

        void Chain(params Vector2[] points)
        {
            for (int i = 1; i < points.Length; i++) Line(points[i - 1], points[i]);
        }

        void Line(Vector2 a, Vector2 b, float width = -1)
        {
            if (width < 0) width = thickness;
            Vector2 pa = Px(a), pb = Px(b), d = pb - pa;
            if (d.sqrMagnitude < 1e-4f) return;
            var n = new Vector2(-d.y, d.x).normalized * width * .5f;
            int i = vh.currentVertCount;
            vh.AddVert(pa - n, color, Vector2.zero); vh.AddVert(pa + n, color, Vector2.zero);
            vh.AddVert(pb + n, color, Vector2.zero); vh.AddVert(pb - n, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
            Disc(pa, width * .5f, 8); Disc(pb, width * .5f, 8);   // round joints
        }

        /// <summary>Filled dot (fists, hands) of radius r in figure units.</summary>
        void Dot(Vector2 c, float r) => Disc(Px(c), r * scale, 16);

        /// <summary>Outline circle (head, fist) of radius r in figure units.</summary>
        void Circle(Vector2 c, float r, float width = -1)
        {
            if (width < 0) width = thickness;
            var pc = Px(c);
            float outer = r * scale + width * .5f, inner = Mathf.Max(0, r * scale - width * .5f);
            const int n = 32;
            int start = vh.currentVertCount;
            for (int k = 0; k <= n; k++)
            {
                float a = 2 * Mathf.PI * k / n;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(pc + dir * outer, color, Vector2.zero);
                vh.AddVert(pc + dir * inner, color, Vector2.zero);
                if (k > 0) { int i = start + 2 * (k - 1); vh.AddTriangle(i, i + 2, i + 1); vh.AddTriangle(i + 1, i + 2, i + 3); }
            }
        }

        void Disc(Vector2 pc, float radius, int n)
        {
            int centre = vh.currentVertCount;
            vh.AddVert(pc, color, Vector2.zero);
            for (int k = 0; k <= n; k++)
            {
                float a = 2 * Mathf.PI * k / n;
                vh.AddVert(pc + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius, color, Vector2.zero);
                if (k > 0) vh.AddTriangle(centre, centre + k, centre + k + 1);
            }
        }
    }
}
