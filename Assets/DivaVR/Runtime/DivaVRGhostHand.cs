using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 教学演示用的"幽灵手"：按关节画的一只手（和比赛里的手部光点一个画法：关节小球 + 细骨），能张开、握拳、捏合，
    /// 摆到驾驶舱里真正要做动作的位置上演示——比牌子上的小人清楚得多，因为就在"你的手该去的地方"。
    /// 右手按本地坐标画：+z 手指方向、+y 手背、+x 拇指那一侧；左手整体镜像。
    /// </summary>
    public class DivaVRGhostHand : MonoBehaviour
    {
        public bool left;
        /// <summary>0 张开 … 1 握拳。</summary>
        public float Curl;
        /// <summary>0 … 1 拇指食指捏合。</summary>
        public float Pinch;

        public static readonly Color Idle = new Color(1f, .95f, 1f), Grip = new Color(.45f, .95f, 1f), PinchC = new Color(1f, .9f, .35f), Drink = new Color(.55f, .75f, 1f), Ult = new Color(1f, .6f, .95f);

        Material mat; Color colour = Idle, colourTarget = Idle;
        Transform palm, wrist;
        readonly Transform[][] joints = new Transform[5][];
        readonly Transform[][] bones = new Transform[5][];
        // 五根手指：根部位置、根部方向、每节长度、每节最大弯曲角（度）
        static readonly Vector3[] Base = { new Vector3(.042f, -.004f, .005f), new Vector3(.03f, 0, .045f), new Vector3(.01f, 0, .05f), new Vector3(-.011f, 0, .048f), new Vector3(-.031f, 0, .041f) };
        static readonly Vector3[] Dir = { new Vector3(.75f, -.15f, .6f), Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
        static readonly float[][] Len = { new[] { .038f, .032f }, new[] { .036f, .023f, .019f }, new[] { .04f, .026f, .02f }, new[] { .037f, .024f, .019f }, new[] { .029f, .019f, .016f } };
        static readonly float[] Bend = { 35, 75, 85, 60 };

        public static DivaVRGhostHand Create(Transform parent, bool left, Material glow)
        {
            var go = new GameObject(left ? "Ghost Hand L" : "Ghost Hand R");
            go.transform.SetParent(parent, false);
            var h = go.AddComponent<DivaVRGhostHand>();
            h.left = left; h.Build(glow);
            return h;
        }

        void Build(Material glow)
        {
            mat = new Material(glow); Tint(Idle);
            palm = Prim(PrimitiveType.Sphere, "Palm", new Vector3(0, 0, .018f), new Vector3(.076f, .028f, .082f));   // 圆乎乎一点，可爱
            wrist = Prim(PrimitiveType.Sphere, "Wrist", new Vector3(0, 0, -.045f), new Vector3(.045f, .018f, .028f));
            for (int f = 0; f < 5; f++)
            {
                int n = Len[f].Length;
                joints[f] = new Transform[n + 1]; bones[f] = new Transform[n];
                for (int j = 0; j <= n; j++) joints[f][j] = Prim(PrimitiveType.Sphere, $"J{f}{j}", Vector3.zero, Vector3.one * (j == n ? .015f : .017f));
                for (int b = 0; b < n; b++) bones[f][b] = Prim(PrimitiveType.Cylinder, $"B{f}{b}", Vector3.zero, new Vector3(.012f, .01f, .012f));
            }
            if (left) transform.localScale = new Vector3(-1, 1, 1);
            Show(false);
        }

        Transform Prim(PrimitiveType t, string name, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(t);
            var c = go.GetComponent<Collider>(); if (c) Destroy(c);
            go.name = name; go.transform.SetParent(transform, false); go.transform.localPosition = pos; go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            return go.transform;
        }

        void Tint(Color c) { mat.color = c * 1.5f; if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c * 1.5f); }

        public void Show(bool on) { if (gameObject.activeSelf != on) gameObject.SetActive(on); }
        public void SetColour(Color c) => colourTarget = c;

        /// <summary>摆到驾驶舱（父物体）坐标里的某个位置、朝向。</summary>
        public void Place(Vector3 localPos, Quaternion localRot) { transform.localPosition = localPos; transform.localRotation = localRot; }

        void LateUpdate()
        {
            colour = Color.Lerp(colour, colourTarget, Time.unscaledDeltaTime * 6); Tint(colour);
            // 食指尖先算出来，拇指捏合时朝它弯
            Vector3 indexTip = Chain(1, Mathf.Max(Curl, .5f * Pinch), Vector3.right, Vector3.zero);
            for (int f = 2; f < 5; f++) Chain(f, Mathf.Max(Curl, .3f * Pinch), Vector3.right, Vector3.zero);
            Chain(0, Mathf.Max(Curl * .6f, .35f * Pinch), Vector3.zero, indexTip);
        }

        /// <summary>算一根手指的关节位置并摆好小球和细骨；返回指尖位置。拇指：方向在"张开"和"朝食指尖"之间按 Pinch 插值。</summary>
        Vector3 Chain(int f, float curl, Vector3 axis, Vector3 pinchTo)
        {
            Vector3 p = Base[f], dir = Dir[f].normalized;
            if (f == 0)
            {
                var toIndex = (pinchTo - p).normalized;
                dir = Vector3.Slerp(dir, toIndex, Pinch).normalized;
                axis = Vector3.Cross(dir, Vector3.up).normalized; if (axis.sqrMagnitude < .01f) axis = Vector3.right;
            }
            joints[f][0].localPosition = p;
            for (int s = 0; s < Len[f].Length; s++)
            {
                dir = Quaternion.AngleAxis(Bend[Mathf.Min(s + (f == 0 ? 1 : 0), 3)] * curl, axis) * dir;
                var q = p + dir * Len[f][s];
                joints[f][s + 1].localPosition = q;
                var b = bones[f][s];
                b.localPosition = (p + q) * .5f;
                b.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
                b.localScale = new Vector3(.012f, Len[f][s] * .5f, .012f);
                p = q;
            }
            return p;
        }
    }
}
