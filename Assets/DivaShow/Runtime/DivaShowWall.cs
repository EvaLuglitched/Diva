using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diva.Show
{
    /// <summary>
    /// 终点前的冰墙（参考守望先锋小美的冰墙；PC 和 VR 共用）：立在终点拱门前 wallDistance 米、横跨整条路（宽度按两边建筑量出来），
    /// 大象顶上去就停住。只有超级泡泡（大招）能把它炸碎：泡泡飞过墙的那条线上就碎成冰晶飞出去，大招后的冲锋接着冲过去。
    /// 没大招也不会卡死：顶着墙推每秒充能 10%，打靶、吃糖照常充能；教练会说明。每局开始墙重新立起来。
    /// 建模：每根冰柱是程序生成的 5–7 边棱柱（四圈顶点各自抖动、顶上收成偏心的尖，全部硬边小面），前后两排、中间高两边矮，
    /// 顶上盖雪、脚下一道雪堆；材质是 Diva/Ice（菲涅尔边缘发光 + 小面高光 + 呼吸的内光，能量越满墙越亮）。
    /// </summary>
    public partial class DivaShowDirector
    {
        [Header("Ice wall before the finish (shared by PC and VR)")]
        [Tooltip("An ice wall across the road before the finish arch; only the MEGA BUBBLE breaks it.")]
        public bool iceWall = true;
        [Tooltip("Metres before the finish line; column height; widest the wall may get (it stops at the buildings on each side).")]
        public float wallDistance = 14f, wallHeight = 4.2f, wallMaxWidth = 18f;
        [Tooltip("Ultimate charge per second while pushing against the wall, so a player without a charge is never stuck.")]
        public float wallPushCharge = 10f;
        public int wallBreakBonus = 50;

        public bool WallUp => wallPieces.Count > 0 && wallUp;
        /// <summary>墙立起来（true）/ 炸碎（false）。</summary>
        public event Action<bool> WallChanged;
        /// <summary>大象顶到墙了（每 1.6 秒最多一次）。</summary>
        public event Action WallBumped;

        static readonly Color IceColour = new Color(.55f, .9f, 1f);
        class Piece { public Transform t; public Vector3 at, scale, vel, spin; public Quaternion rot; public bool column; }
        readonly List<Piece> wallPieces = new List<Piece>();     // 冰柱、雪顶、雪堆（每局放回来）
        readonly List<Piece> wallShards = new List<Piece>();     // 炸开时飞出去的小冰晶（飞完就删）
        Transform wallRoot; Material iceMat, snowMat; ParticleSystem wallFrost;
        bool wallUp; float wallBrokeAt = -1, lastBump = -10, wallTipAt = -10, wallLeft, wallRight;
        Vector3 wallCentre, wallForward, wallRight3;
        System.Random wallRng;

        float WRand(float a, float b) => Mathf.Lerp(a, b, (float)wallRng.NextDouble());

        void BuildWall()
        {
            if (!iceWall || !game || !game.finishLine || !elephant) return;
            wallForward = Vector3.ProjectOnPlane(game.finishLine.forward, Vector3.up).normalized;
            if (wallForward.sqrMagnitude < .5f) wallForward = Vector3.forward;
            wallCentre = game.finishLine.position - wallForward * wallDistance; wallCentre.y = elephant.position.y;
            wallRight3 = Vector3.Cross(Vector3.up, wallForward);
            // 宽度：从路中间往两边量到最近的建筑（镜头遮挡检测给道具加的碰撞体也算）
            float Free(Vector3 dir)
            {
                var from = wallCentre + Vector3.up * 1.6f;
                int n = Physics.SphereCastNonAlloc(from, .6f, dir, hits, wallMaxWidth / 2, ~(1 << 5), QueryTriggerInteraction.Ignore);
                float best = wallMaxWidth / 2;
                for (int i = 0; i < n; i++)
                {
                    var h = hits[i];
                    if (h.distance <= 0 || h.transform.IsChildOf(elephant) || h.transform.IsChildOf(transform)) continue;
                    best = Mathf.Min(best, h.distance);
                }
                return best;
            }
            wallLeft = Mathf.Max(3f, Free(-wallRight3)); wallRight = Mathf.Max(3f, Free(wallRight3));
            wallRoot = new GameObject("Diva Ice Wall").transform;
            wallRoot.SetParent(transform, true);
            iceMat = IceMaterial(); snowMat = SnowMaterial();
            wallRng = new System.Random(11);
            float width = wallLeft + wallRight, mid = (wallRight - wallLeft) * .5f;
            // 前排：一根挨一根，中间高、两边矮（圆弧形的轮廓）；后排错开半根，矮一截，墙才有厚度
            for (float x = -wallLeft - .3f; x <= wallRight + .3f; x += WRand(1.05f, 1.35f))
            {
                float edge = Mathf.Abs(x - mid) / Mathf.Max(1f, width * .5f);          // 0 中间 … 1 两端
                float h = wallHeight * Mathf.Lerp(WRand(1.0f, 1.18f), WRand(.68f, .88f), edge * edge);
                float r = WRand(.55f, .85f);
                var lean = new Vector2(Mathf.Sign(x - mid) * WRand(.05f, .22f) * r, WRand(-.1f, .1f) * r);   // 两边的柱子往外歪一点
                Column(wallCentre + wallRight3 * x + wallForward * WRand(-.25f, .25f), r, h, lean, true);
                float h2 = h * WRand(.5f, .72f);
                Column(wallCentre + wallRight3 * (x + WRand(.4f, .8f)) + wallForward * WRand(.95f, 1.35f), r * WRand(.7f, .95f), h2, new Vector2(0, WRand(.05f, .2f) * r), false);
            }
            // 脚下一道雪堆（前后各一条）
            foreach (float z in new[] { -.9f, 1.9f })
            {
                var snow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var sc = snow.GetComponent<Collider>(); if (sc) Destroy(sc);
                snow.name = "Snow Bank"; snow.transform.SetParent(wallRoot, true);
                var at = wallCentre + wallRight3 * mid + wallForward * z + Vector3.up * .05f;
                var scale = new Vector3(width + 2.5f, .7f, 2.4f);
                var rot = Quaternion.LookRotation(wallForward, Vector3.up);
                snow.transform.SetPositionAndRotation(at, rot); snow.transform.localScale = scale;
                var sr = snow.GetComponent<MeshRenderer>(); sr.sharedMaterial = snowMat; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sr.receiveShadows = false;
                wallPieces.Add(new Piece { t = snow.transform, at = at, rot = rot, scale = scale });
            }
            // 冰晶闪光：沿着墙面飘的小亮点
            if (glowMaterial)
            {
                var fgo = new GameObject("Ice Frost");
                fgo.transform.SetParent(wallRoot, true);
                fgo.transform.SetPositionAndRotation(wallCentre + wallRight3 * mid + Vector3.up * wallHeight * .5f, Quaternion.LookRotation(wallForward, Vector3.up));
                wallFrost = fgo.AddComponent<ParticleSystem>();
                var main = wallFrost.main; main.startLifetime = 2.5f; main.startSpeed = .2f; main.startSize = new ParticleSystem.MinMaxCurve(.06f, .16f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(.8f, .97f, 1f, .9f), new Color(.5f, .85f, 1f, .6f)); main.maxParticles = 200;
                var em = wallFrost.emission; em.rateOverTime = 45;
                var sh = wallFrost.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(width, wallHeight, 1.6f);
                var fr = wallFrost.GetComponent<ParticleSystemRenderer>(); fr.sharedMaterial = glowMaterial; fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            // 地上一圈薄霜（压扁的冰盘，半透明）
            {
                var frost = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var fc = frost.GetComponent<Collider>(); if (fc) Destroy(fc);
                frost.name = "Ground Frost"; frost.transform.SetParent(wallRoot, true);
                var fat = wallCentre + wallRight3 * mid + wallForward * .5f + Vector3.up * .02f;
                var frot = Quaternion.LookRotation(wallForward, Vector3.up);
                var fscale = new Vector3(width + 5f, .015f, 5.5f);
                frost.transform.SetPositionAndRotation(fat, frot); frost.transform.localScale = fscale;
                var fr2 = frost.GetComponent<MeshRenderer>(); fr2.sharedMaterial = snowMat; fr2.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; fr2.receiveShadows = true;
                wallPieces.Add(new Piece { t = frost.transform, at = fat, rot = frot, scale = fscale });
            }
            // 折射（透过冰看到后面扭曲的街景）：要不透明纹理，PC 的渲染设置里开着；头显上太贵，不开
            if (iceMat && iceMat.HasProperty("_Alpha") && !UnityEngine.XR.XRSettings.isDeviceActive) iceMat.EnableKeyword("_REFRACTION");
            wallUp = true;
            Debug.Log($"DIVA_SHOW ice wall at {wallCentre} facing {wallForward}, {wallLeft:F1} m left / {wallRight:F1} m right, {wallPieces.Count} pieces, shader {(iceMat ? iceMat.shader.name : "none")}, refraction {(iceMat && iceMat.IsKeywordEnabled("_REFRACTION"))}");
        }

        // 一根冰柱（+ 顶上的雪）
        void Column(Vector3 at, float radius, float height, Vector2 lean, bool front)
        {
            var go = new GameObject("Ice Column", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(wallRoot, true);
            var rot = Quaternion.LookRotation(wallForward, Vector3.up) * Quaternion.Euler(0, WRand(0, 360), 0);
            go.transform.SetPositionAndRotation(at, rot);
            go.GetComponent<MeshFilter>().sharedMesh = CrystalMesh(wallRng, radius, height, lean, out var apex);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = iceMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = false;
            wallPieces.Add(new Piece { t = go.transform, at = at, rot = rot, scale = Vector3.one, column = true });
            // 前排脚下偶尔一块斜着的小冰块，像崩出来的碎冰
            if (front && wallRng.NextDouble() < .45)
            {
                var sgo = new GameObject("Ice Chunk", typeof(MeshFilter), typeof(MeshRenderer));
                sgo.transform.SetParent(wallRoot, true);
                float cr = radius * WRand(.3f, .5f);
                var cat = at - wallForward * WRand(.9f, 1.6f) + wallRight3 * WRand(-.6f, .6f);
                var crot = Quaternion.LookRotation(wallForward, Vector3.up) * Quaternion.Euler(WRand(-35, -15), WRand(0, 360), WRand(-20, 20));
                sgo.transform.SetPositionAndRotation(cat, crot);
                sgo.GetComponent<MeshFilter>().sharedMesh = CrystalMesh(wallRng, cr, cr * WRand(2.2f, 3.6f), Vector2.zero, out _);
                var cm = sgo.GetComponent<MeshRenderer>(); cm.sharedMaterial = iceMat; cm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; cm.receiveShadows = false;
                wallPieces.Add(new Piece { t = sgo.transform, at = cat, rot = crot, scale = Vector3.one, column = true });
            }
            // 六成的前排柱子顶上盖一团雪（偏一点、歪一点）
            if (front && wallRng.NextDouble() < .6)
            {
                var snow = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var sc = snow.GetComponent<Collider>(); if (sc) Destroy(sc);
                snow.name = "Snow Cap"; snow.transform.SetParent(wallRoot, true);
                var sat = go.transform.TransformPoint(apex + new Vector3(WRand(-.3f, .3f) * radius, -radius * .35f, WRand(-.3f, .3f) * radius));
                var srot = rot * Quaternion.Euler(WRand(-12, 12), 0, WRand(-12, 12));
                var sscale = new Vector3(radius * WRand(1.4f, 1.9f), radius * .34f, radius * WRand(1.2f, 1.7f));
                snow.transform.SetPositionAndRotation(sat, srot); snow.transform.localScale = sscale;
                var sr = snow.GetComponent<MeshRenderer>(); sr.sharedMaterial = snowMat; sr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; sr.receiveShadows = false;
                wallPieces.Add(new Piece { t = snow.transform, at = sat, rot = srot, scale = sscale });
            }
        }

        /// <summary>
        /// 冰晶网格：5–7 边、四圈顶点各自抖动的棱柱（中段微鼓、顶段收窄），顶上收成偏心的尖（斜着的切面）；每个面独立顶点，硬边。
        /// 顶点色 r = 高度 0..1，g = 雪（顶面）。lean = 顶端相对底端的水平偏移。
        /// </summary>
        static Mesh CrystalMesh(System.Random rng, float radius, float height, Vector2 lean, out Vector3 apex)
        {
            float R(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            int sides = 5 + rng.Next(3), ringCount = 4;
            float[] ys = { 0, height * R(.28f, .4f), height * R(.6f, .76f), height };
            float[] bulge = { .9f, 1.08f, 1.0f, .74f };
            var rings = new Vector3[ringCount][];
            float phase = R(0, 6.2832f);
            for (int r = 0; r < ringCount; r++)
            {
                rings[r] = new Vector3[sides];
                for (int i = 0; i < sides; i++)
                {
                    float ang = phase + i * 6.2832f / sides + R(-.16f, .16f);
                    float rad = radius * bulge[r] * R(.82f, 1.18f), t = ys[r] / height;
                    rings[r][i] = new Vector3(Mathf.Cos(ang) * rad + lean.x * t, ys[r], Mathf.Sin(ang) * rad + lean.y * t);
                }
            }
            apex = new Vector3(R(-.45f, .45f) * radius + lean.x, height + R(.25f, .9f) * radius, R(-.45f, .45f) * radius + lean.y);
            var verts = new List<Vector3>(); var cols = new List<Color>(); var tris = new List<int>();
            Vector3 axis = new Vector3(lean.x, 0, lean.y) / Mathf.Max(.01f, height);
            void Tri(Vector3 a, Vector3 b, Vector3 c, float snowA, float snowB, float snowC)
            {
                // 法线要朝外：Unity 的正面是顺时针，几何法线 = cross(c - a, b - a)；算出来朝里就把 b、c 换一下
                var n = Vector3.Cross(c - a, b - a);
                var centre = (a + b + c) / 3;
                var outward = new Vector3(centre.x - axis.x * centre.y, .25f * radius, centre.z - axis.z * centre.y);
                if (Vector3.Dot(n, outward) < 0) { var tmp = b; b = c; c = tmp; var ts = snowB; snowB = snowC; snowC = ts; }
                int i0 = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                cols.Add(new Color(a.y / height, snowA, 0)); cols.Add(new Color(b.y / height, snowB, 0)); cols.Add(new Color(c.y / height, snowC, 0));
                tris.Add(i0); tris.Add(i0 + 1); tris.Add(i0 + 2);
            }
            for (int r = 0; r + 1 < ringCount; r++)
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    Vector3 a = rings[r][i], b = rings[r][j], c = rings[r + 1][j], d = rings[r + 1][i];
                    float s0 = r + 1 == ringCount - 1 ? .2f : 0;
                    // 每个侧面从中间一点凹进去或凸出来一点，分成四个小面：棱角更多，光打上去更像天然冰
                    var m = (a + b + c + d) * .25f;
                    var outDir = new Vector3(m.x - axis.x * m.y, 0, m.z - axis.z * m.y).normalized;
                    m += outDir * radius * R(-.14f, .12f) + Vector3.up * R(-.08f, .08f) * radius;
                    float sm = s0 * .5f;
                    Tri(a, b, m, 0, 0, sm); Tri(b, c, m, 0, s0, sm); Tri(c, d, m, s0, s0, sm); Tri(d, a, m, s0, 0, sm);
                }
            for (int i = 0; i < sides; i++) Tri(rings[ringCount - 1][i], rings[ringCount - 1][(i + 1) % sides], apex, .45f, .45f, 1f);
            var mesh = new Mesh { name = "Diva Ice Column" };
            mesh.SetVertices(verts); mesh.SetColors(cols); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        // 冰：自己的 Diva/Ice（Resources 里，VR 的立体渲染也能画）；找不到就退回 URP Lit 半透明
        Material IceMaterial()
        {
            var ice = Shader.Find("Diva/Ice");
            if (ice) return new Material(ice) { name = "Diva Ice Wall" };
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) { var g = glowMaterial ? new Material(glowMaterial) : null; if (g) Tint(g, IceColour * 1.5f); return g; }
            var m = new Material(sh) { name = "Diva Ice Wall (Lit)" };
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_ZWrite", 0);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetColor("_BaseColor", new Color(.6f, .88f, 1f, .86f));
            m.SetFloat("_Smoothness", .92f); m.SetFloat("_Metallic", 0);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(.2f, .62f, 1f) * 1.1f);
            return m;
        }

        // 雪：URP Lit，柔白，不太亮
        Material SnowMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) return iceMat;
            var m = new Material(sh) { name = "Diva Snow" };
            m.SetColor("_BaseColor", new Color(.93f, .97f, 1f, 1f));
            m.SetFloat("_Smoothness", .35f); m.SetFloat("_Metallic", 0);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(.25f, .35f, .45f) * .5f);
            return m;
        }

        /// <summary>新一局：墙立回来。</summary>
        void WallReset()
        {
            if (wallPieces.Count == 0) return;
            foreach (var p in wallPieces) { p.t.gameObject.SetActive(true); p.t.SetPositionAndRotation(p.at, p.rot); p.t.localScale = p.scale; }
            foreach (var s in wallShards) if (s.t) Destroy(s.t.gameObject);
            wallShards.Clear();
            if (wallFrost) wallFrost.Play();
            bool was = wallUp; wallUp = true; wallBrokeAt = -1; wallTipAt = -10;
            if (iceMat && iceMat.HasProperty("_Glow")) iceMat.SetFloat("_Glow", 1);
            if (!was) WallChanged?.Invoke(true);
        }

        /// <summary>在 DivaDemo / 移动之后（LateUpdate）：顶到墙就推回去，顶着推充能；碎冰飞出去。</summary>
        void WallLateUpdate()
        {
            if (wallPieces.Count == 0) return;
            if (wallBrokeAt >= 0) UpdateWallPieces();
            if (!wallUp || Phase != ShowPhase.Play && Phase != ShowPhase.Ult && Phase != ShowPhase.Countdown) return;
            var off = elephant.position - wallCentre;
            float along = Vector3.Dot(off, wallForward), across = Vector3.Dot(off, wallRight3);
            // 能量越满墙越亮（告诉人"可以了"）
            if (iceMat && iceMat.HasProperty("_Glow")) iceMat.SetFloat("_Glow", charge >= 100 ? 1.6f + .5f * Mathf.Sin(Time.unscaledTime * 6) : 1f + .4f * charge / 100f);
            if (across < -wallLeft - 1.5f || across > wallRight + 1.5f) return;
            float margin = elephantRadius * .95f;   // 鼻子别穿进冰里
            if (along <= -margin || along > 4f) return;
            elephant.position -= wallForward * (along + margin);
            if (Phase != ShowPhase.Play) return;
            bool pushing = demo.State.Forward > .2f;
            if (pushing) charge = Mathf.Min(100, charge + wallPushCharge * Time.deltaTime);
            // 顶到的反馈：闷响、闪一下、震（VR）；每 1.6 秒一次
            if (Time.unscaledTime > lastBump + 1.6f)
            {
                lastBump = Time.unscaledTime;
                sound.Play("hit", .6f, .55f);
                fx.Flash(wallCentre + wallRight3 * across + Vector3.up * 1.8f - wallForward * .6f, IceColour, 2f, .25f);
                if (look) look.Pulse(.15f);
                WallBumped?.Invoke();
                Debug.Log($"DIVA_SHOW wall bump charge {charge:F0}");
            }
            // 教练：能量没满说怎么充，满了说放大招（冰墙的提示插队：这时候别的提示都没它要紧）
            string tip = charge >= 100 ? "coach.wall.ult" : "coach.wall";
            if (Time.unscaledTime > wallTipAt + (charge >= 100 ? 4f : 8f) || tip == "coach.wall.ult" && tipKey != tip && Time.unscaledTime > wallTipAt + 1.5f)
            {
                wallTipAt = Time.unscaledTime;
                CoachTip(tip, 5f, true);
                WorldPopup(DivaText.T(charge >= 100 ? "wall.hint.ult" : "wall.hint"), wallCentre + wallRight3 * Mathf.Clamp(across, -wallLeft, wallRight) + Vector3.up * (wallHeight + 1.4f), IceColour);
            }
        }

        /// <summary>超级泡泡飞过来：泡泡那条线穿过墙就炸碎。origin / dir 是泡泡的起点和方向。</summary>
        bool WallTryBreak(Vector3 origin, Vector3 dir)
        {
            if (!wallUp || wallPieces.Count == 0) return false;
            var flat = Vector3.ProjectOnPlane(wallCentre - origin, Vector3.up);
            float ahead = Vector3.Dot(flat, Vector3.ProjectOnPlane(dir, Vector3.up).normalized);
            if (ahead < -3f || ahead > ultRange + 4f) return false;
            float across = Vector3.Dot(Vector3.ProjectOnPlane(origin + dir * Mathf.Max(0, ahead) - wallCentre, Vector3.up), wallRight3);
            if (across < -wallLeft - 3f || across > wallRight + 3f) return false;
            BreakWall();
            return true;
        }

        void BreakWall()
        {
            wallUp = false; wallBrokeAt = Time.time;
            var rng = new System.Random(5);
            float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            float mid = (wallRight - wallLeft) * .5f;
            foreach (var p in wallPieces)
            {
                float side = Vector3.Dot(p.at - wallCentre, wallRight3) - mid;
                p.vel = wallForward * Rand(5f, 12f) + Vector3.up * Rand(2.5f, 8f) + wallRight3 * side * Rand(.1f, .35f);
                p.spin = new Vector3(Rand(-260, 260), Rand(-140, 140), Rand(-260, 260));
                // 每根柱子再崩出几粒小冰晶
                if (p.column)
                    for (int k = 0; k < 3; k++)
                    {
                        var go = new GameObject("Ice Shard", typeof(MeshFilter), typeof(MeshRenderer));
                        go.transform.SetParent(wallRoot, true);
                        float sr = Rand(.14f, .32f);
                        go.GetComponent<MeshFilter>().sharedMesh = CrystalMesh(rng, sr, sr * Rand(1.8f, 3.2f), Vector2.zero, out _);
                        var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = iceMat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
                        var at = p.at + Vector3.up * Rand(.5f, wallHeight * .8f) + wallRight3 * Rand(-.6f, .6f);
                        go.transform.SetPositionAndRotation(at, Quaternion.Euler(Rand(0, 360), Rand(0, 360), Rand(0, 360)));
                        wallShards.Add(new Piece { t = go.transform, at = at, scale = Vector3.one,
                                                   vel = wallForward * Rand(3f, 14f) + Vector3.up * Rand(2f, 10f) + wallRight3 * Rand(-5f, 5f),
                                                   spin = new Vector3(Rand(-500, 500), Rand(-500, 500), Rand(-500, 500)) });
                    }
            }
            if (wallFrost) wallFrost.Stop();
            sound.Play("boom", 1f); sound.Play("pop", .9f, .8f); sound.Play("skill", .7f, 1.2f);
            var centre = wallCentre + wallRight3 * mid + Vector3.up * wallHeight * .5f;
            fx.Pop(centre, IceColour, 4.5f);
            fx.Flash(centre, Color.white, 9f, .4f);
            if (look) look.Pulse(1);
            bonus += wallBreakBonus;
            Banner(DivaText.T("wall.break"), DivaText.F("wall.break.b", wallBreakBonus), IceColour, 2.4f, null);
            Feed(DivaText.T("wall.break") + "  +" + wallBreakBonus, IceColour);
            replay.Mark(Time.time - runStart, "wall", 90, "wall.break");
            WallChanged?.Invoke(false);
            Debug.Log("DIVA_SHOW wall smashed");
        }

        // 碎冰飞出去：带重力、转着、慢慢缩小，2 秒后消失（大招的慢动作里也跟着慢）
        void UpdateWallPieces()
        {
            float age = Time.time - wallBrokeAt, dt = Time.deltaTime;
            foreach (var p in wallPieces)
            {
                if (!p.t.gameObject.activeSelf) continue;
                if (age > 1.9f) { p.t.gameObject.SetActive(false); continue; }
                p.vel += Vector3.down * 14f * dt;
                p.t.position += p.vel * dt;
                p.t.Rotate(p.spin * dt, Space.Self);
                p.t.localScale = p.scale * (p.column ? .6f : .5f) * Mathf.Clamp01(1 - (age - .9f) / 1f);
            }
            for (int i = wallShards.Count - 1; i >= 0; i--)
            {
                var s = wallShards[i];
                if (!s.t || age > 2.4f) { if (s.t) Destroy(s.t.gameObject); wallShards.RemoveAt(i); continue; }
                s.vel += Vector3.down * 16f * dt;
                s.t.position += s.vel * dt;
                s.t.Rotate(s.spin * dt, Space.Self);
                s.t.localScale = Vector3.one * Mathf.Clamp01(1 - (age - 1.4f) / 1f);
            }
            if (age > 2.4f) wallBrokeAt = -1;
        }
    }
}
