using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diva.Show
{
    /// <summary>
    /// 终点前的冰墙（参考守望先锋小美的冰墙；PC 和 VR 共用）：立在终点拱门前 wallDistance 米、横跨整条路（宽度按两边建筑量出来），
    /// 大象顶上去就停住。只有超级泡泡（大招）能把它炸碎：泡泡飞过墙的那条线上就碎成冰块飞出去，大招后的冲锋接着冲过去。
    /// 没大招也不会卡死：顶着墙推每秒充能 10%，打靶、吃糖照常充能；教练会说明。每局开始墙重新立起来。
    /// </summary>
    public partial class DivaShowDirector
    {
        [Header("Ice wall before the finish (shared by PC and VR)")]
        [Tooltip("An ice wall across the road before the finish arch; only the MEGA BUBBLE breaks it.")]
        public bool iceWall = true;
        [Tooltip("Metres before the finish line; pillar height; widest the wall may get (it stops at the buildings on each side).")]
        public float wallDistance = 14f, wallHeight = 5.5f, wallMaxWidth = 22f;
        [Tooltip("Ultimate charge per second while pushing against the wall, so a player without a charge is never stuck.")]
        public float wallPushCharge = 10f;
        public int wallBreakBonus = 50;

        public bool WallUp => wallPillars.Count > 0 && wallUp;
        /// <summary>墙立起来（true）/ 炸碎（false）。</summary>
        public event Action<bool> WallChanged;
        /// <summary>大象顶到墙了（每 1.6 秒最多一次）。</summary>
        public event Action WallBumped;

        static readonly Color IceColour = new Color(.55f, .9f, 1f);
        class Pillar { public Transform t; public Vector3 at, scale, vel, spin; public Quaternion rot; }
        readonly List<Pillar> wallPillars = new List<Pillar>();
        Transform wallRoot; Material wallMat; ParticleSystem wallFrost;
        bool wallUp; float wallBrokeAt = -1, lastBump = -10, wallTipAt = -10, wallLeft, wallRight;
        Vector3 wallCentre, wallForward;

        void BuildWall()
        {
            if (!iceWall || !game || !game.finishLine || !elephant) return;
            wallForward = Vector3.ProjectOnPlane(game.finishLine.forward, Vector3.up).normalized;
            if (wallForward.sqrMagnitude < .5f) wallForward = Vector3.forward;
            wallCentre = game.finishLine.position - wallForward * wallDistance; wallCentre.y = elephant.position.y;
            var right = Vector3.Cross(Vector3.up, wallForward);
            // 宽度：从路中间往两边量到最近的建筑（镜头遮挡检测给道具加的碰撞体也算），再往墙里伸 1 米把缝封上
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
            wallLeft = Mathf.Max(3f, Free(-right)); wallRight = Mathf.Max(3f, Free(right));
            wallRoot = new GameObject("Diva Ice Wall").transform;
            wallRoot.SetParent(transform, true);
            wallMat = IceMaterial();
            // 一排冰柱：每根 1.45 米宽，高矮不一、前后稍微错开，像一面刚冒出来的冰墙
            var rng = new System.Random(11);
            float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            void Block(Vector3 at, Quaternion rot, Vector3 scale)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = go.GetComponent<Collider>(); if (col) Destroy(col);
                go.name = "Ice"; go.transform.SetParent(wallRoot, true);
                go.transform.SetPositionAndRotation(at, rot); go.transform.localScale = scale;
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = wallMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                wallPillars.Add(new Pillar { t = go.transform, at = at, rot = rot, scale = scale });
            }
            var face = Quaternion.LookRotation(wallForward, Vector3.up);
            for (float x = -wallLeft - .6f; x <= wallRight + .6f; x += 1.45f)
            {
                // 主柱：高矮不一、前后错开、微微歪；柱顶再盖一块斜的小冰块，顶部参差像刚冒出来的冰
                float h = wallHeight * Rand(.78f, 1.12f);
                var at = wallCentre + right * x + wallForward * Rand(-.35f, .35f) + Vector3.up * h * .5f;
                Block(at, face * Quaternion.Euler(Rand(-3, 3), Rand(-5, 5), Rand(-4, 4)), new Vector3(1.5f, h, Rand(1.1f, 1.5f)));
                float cap = Rand(.5f, 1.1f);
                Block(at + Vector3.up * (h * .5f + cap * .3f) + right * Rand(-.3f, .3f), face * Quaternion.Euler(Rand(-14, 14), Rand(-20, 20), Rand(-14, 14)), new Vector3(Rand(.8f, 1.3f), cap, Rand(.8f, 1.3f)));
                // 后面再一层矮一点的柱子，墙看起来有厚度
                float h2 = h * Rand(.45f, .7f);
                Block(wallCentre + right * (x + .7f) + wallForward * Rand(.9f, 1.3f) + Vector3.up * h2 * .5f, face * Quaternion.Euler(Rand(-4, 4), Rand(-8, 8), Rand(-4, 4)), new Vector3(1.4f, h2, 1.1f));
            }
            // 冰晶闪光：沿着墙面飘的小亮点
            if (glowMaterial)
            {
                var fgo = new GameObject("Ice Frost");
                fgo.transform.SetParent(wallRoot, true);
                fgo.transform.SetPositionAndRotation(wallCentre + Vector3.up * wallHeight * .5f, Quaternion.LookRotation(wallForward, Vector3.up));
                wallFrost = fgo.AddComponent<ParticleSystem>();
                var main = wallFrost.main; main.startLifetime = 2.5f; main.startSpeed = .2f; main.startSize = new ParticleSystem.MinMaxCurve(.06f, .16f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(.8f, .97f, 1f, .9f), new Color(.5f, .85f, 1f, .6f)); main.maxParticles = 200;
                var em = wallFrost.emission; em.rateOverTime = 45;
                var sh = wallFrost.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(wallLeft + wallRight, wallHeight, 1.2f);
                var fr = wallFrost.GetComponent<ParticleSystemRenderer>(); fr.sharedMaterial = glowMaterial; fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            wallUp = true;
            Debug.Log($"DIVA_SHOW ice wall at {wallCentre} facing {wallForward}, {wallLeft:F1} m left / {wallRight:F1} m right, {wallPillars.Count} pillars");
        }

        // 冰：URP Lit 半透明、很光滑、带一点自发光（VR 只认 URP 着色器）
        Material IceMaterial()
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            if (!sh) { var g = glowMaterial ? new Material(glowMaterial) : null; if (g) Tint(g, IceColour * 1.5f); return g; }
            var m = new Material(sh) { name = "Diva Ice Wall" };
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", 0); m.SetFloat("_ZWrite", 0);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetOverrideTag("RenderType", "Transparent"); m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetColor("_BaseColor", new Color(.6f, .88f, 1f, .86f));
            m.SetFloat("_Smoothness", .92f); m.SetFloat("_Metallic", 0);
            m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", new Color(.2f, .62f, 1f) * 1.1f);
            return m;
        }

        /// <summary>新一局：墙立回来。</summary>
        void WallReset()
        {
            if (wallPillars.Count == 0) return;
            foreach (var p in wallPillars) { p.t.gameObject.SetActive(true); p.t.SetPositionAndRotation(p.at, p.rot); p.t.localScale = p.scale; }
            if (wallFrost) wallFrost.Play();
            bool was = wallUp; wallUp = true; wallBrokeAt = -1; wallTipAt = -10;
            if (!was) WallChanged?.Invoke(true);
        }

        /// <summary>在 DivaDemo / 移动之后（LateUpdate）：顶到墙就推回去，顶着推充能；碎冰块飞出去。</summary>
        void WallLateUpdate()
        {
            if (wallPillars.Count == 0) return;
            if (wallBrokeAt >= 0) UpdateWallPieces();
            if (!wallUp || Phase != ShowPhase.Play && Phase != ShowPhase.Ult && Phase != ShowPhase.Countdown) return;
            var right = Vector3.Cross(Vector3.up, wallForward);
            var off = elephant.position - wallCentre;
            float along = Vector3.Dot(off, wallForward), across = Vector3.Dot(off, right);
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
                fx.Flash(wallCentre + right * across + Vector3.up * 1.8f - wallForward * .6f, IceColour, 2f, .25f);
                if (look) look.Pulse(.15f);
                WallBumped?.Invoke();
                if (wallPillars.Count > 0) Debug.Log($"DIVA_SHOW wall bump charge {charge:F0}");
            }
            // 教练：能量没满说怎么充，满了说放大招
            string tip = charge >= 100 ? "coach.wall.ult" : "coach.wall";
            if (Time.unscaledTime > wallTipAt + (charge >= 100 ? 4f : 8f) || tip == "coach.wall.ult" && tipKey != tip && Time.unscaledTime > wallTipAt + 1.5f)
            {
                wallTipAt = Time.unscaledTime;
                CoachTip(tip, 5f, true);   // 冰墙的提示插队：这时候别的提示都没它要紧
                WorldPopup(DivaText.T(charge >= 100 ? "wall.hint.ult" : "wall.hint"), wallCentre + right * Mathf.Clamp(across, -wallLeft, wallRight) + Vector3.up * (wallHeight + 1.2f), IceColour);
            }
        }

        /// <summary>超级泡泡飞过来：泡泡那条线穿过墙就炸碎。origin / dir 是泡泡的起点和方向。</summary>
        bool WallTryBreak(Vector3 origin, Vector3 dir)
        {
            if (!wallUp || wallPillars.Count == 0) return false;
            var flat = Vector3.ProjectOnPlane(wallCentre - origin, Vector3.up);
            float ahead = Vector3.Dot(flat, Vector3.ProjectOnPlane(dir, Vector3.up).normalized);
            if (ahead < -3f || ahead > ultRange + 4f) return false;
            var right = Vector3.Cross(Vector3.up, wallForward);
            float across = Vector3.Dot(Vector3.ProjectOnPlane(origin + dir * Mathf.Max(0, ahead) - wallCentre, Vector3.up), right);
            if (across < -wallLeft - 3f || across > wallRight + 3f) return false;
            BreakWall();
            return true;
        }

        void BreakWall()
        {
            wallUp = false; wallBrokeAt = Time.time;
            var right = Vector3.Cross(Vector3.up, wallForward);
            var rng = new System.Random(5);
            float Rand(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
            foreach (var p in wallPillars)
            {
                float side = Vector3.Dot(p.at - wallCentre, right);
                p.vel = wallForward * Rand(5f, 11f) + Vector3.up * Rand(3f, 8f) + right * side * Rand(.1f, .35f);
                p.spin = new Vector3(Rand(-240, 240), Rand(-120, 120), Rand(-240, 240));
            }
            if (wallFrost) wallFrost.Stop();
            sound.Play("boom", 1f); sound.Play("pop", .9f, .8f); sound.Play("skill", .7f, 1.2f);
            fx.Pop(wallCentre + Vector3.up * wallHeight * .5f, IceColour, 4.5f);
            fx.Flash(wallCentre + Vector3.up * wallHeight * .5f, Color.white, 9f, .4f);
            if (look) look.Pulse(1);
            bonus += wallBreakBonus;
            Banner(DivaText.T("wall.break"), DivaText.F("wall.break.b", wallBreakBonus), IceColour, 2.4f, null);
            Feed(DivaText.T("wall.break") + "  +" + wallBreakBonus, IceColour);
            replay.Mark(Time.time - runStart, "wall", 90, "wall.break");
            WallChanged?.Invoke(false);
            Debug.Log("DIVA_SHOW wall smashed");
        }

        // 碎冰飞出去：带重力、转着、慢慢缩小，1.8 秒后消失（大招的慢动作里也跟着慢）
        void UpdateWallPieces()
        {
            float age = Time.time - wallBrokeAt, dt = Time.deltaTime;
            if (age > 1.9f) { foreach (var p in wallPillars) if (p.t.gameObject.activeSelf) p.t.gameObject.SetActive(false); wallBrokeAt = -1; return; }
            float shrink = Mathf.Clamp01(1 - (age - .9f) / 1f);
            foreach (var p in wallPillars)
            {
                if (!p.t.gameObject.activeSelf) continue;
                p.vel += Vector3.down * 14f * dt;
                p.t.position += p.vel * dt;
                p.t.Rotate(p.spin * dt, Space.Self);
                p.t.localScale = new Vector3(p.scale.x * .5f, p.scale.y * .3f, p.scale.z * .5f) * shrink;
            }
        }
    }
}
