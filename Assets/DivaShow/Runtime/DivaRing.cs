using UnityEngine;
using UnityEngine.UI;

namespace Diva.Show
{
    /// <summary>圆环（技能充能、倒计时）：fill 0..1 从顶部顺时针。</summary>
    public class DivaRing : MaskableGraphic
    {
        [Range(0, 1)] public float fill = 1;
        public float thickness = 8;
        public int segments = 64;
        public void SetFill(float f) { f = Mathf.Clamp01(f); if (Mathf.Abs(f - fill) > .001f) { fill = f; SetVerticesDirty(); } }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = GetPixelAdjustedRect();
            float outer = Mathf.Min(r.width, r.height) * .5f, inner = Mathf.Max(0, outer - thickness);
            int n = Mathf.Max(1, Mathf.CeilToInt(segments * fill));
            for (int k = 0; k <= n; k++)
            {
                float a = Mathf.PI * .5f - 2 * Mathf.PI * fill * k / n;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                vh.AddVert(r.center + dir * outer, color, Vector2.zero);
                vh.AddVert(r.center + dir * inner, color, Vector2.zero);
                if (k > 0) { int i = vh.currentVertCount - 4; vh.AddTriangle(i, i + 2, i + 1); vh.AddTriangle(i + 1, i + 2, i + 3); }
            }
        }
    }
}
