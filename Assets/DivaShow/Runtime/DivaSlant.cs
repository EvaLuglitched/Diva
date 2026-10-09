using UnityEngine;
using UnityEngine.UI;

namespace Diva.Show
{
    /// <summary>
    /// 斜切面板（Overwatch 风格的平行四边形）：上下渐变，左边可以有一条亮色竖条。
    /// 用 uGUI 网格画，不需要贴图。
    /// </summary>
    public class DivaSlant : MaskableGraphic
    {
        [Tooltip("Pixels the top edge is shifted right (negative = left).")]
        public float skew = 18;
        public Color bottom = Color.white;
        public Color stripe = Color.clear;
        public float stripeWidth = 0;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float s = skew * .5f;
            Quad(vh, new Vector2(r.xMin - s, r.yMin), new Vector2(r.xMax - s, r.yMin), new Vector2(r.xMax + s, r.yMax), new Vector2(r.xMin + s, r.yMax),
                 bottom * color, color);
            if (stripeWidth > 0 && stripe.a > 0)
                Quad(vh, new Vector2(r.xMin - s, r.yMin), new Vector2(r.xMin - s + stripeWidth, r.yMin), new Vector2(r.xMin + s + stripeWidth, r.yMax),
                     new Vector2(r.xMin + s, r.yMax), stripe, stripe);
        }

        static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color lo, Color hi)
        {
            int i = vh.currentVertCount;
            vh.AddVert(a, lo, Vector2.zero); vh.AddVert(b, lo, Vector2.right);
            vh.AddVert(c, hi, Vector2.one); vh.AddVert(d, hi, Vector2.up);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }

        public void Set(Color top, Color bottomColor) { color = top; bottom = bottomColor; SetVerticesDirty(); }
    }
}
