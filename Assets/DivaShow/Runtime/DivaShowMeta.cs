using System;
using System.Linq;
using UnityEngine;
using Diva.VR;

namespace Diva.Show
{
    /// <summary>
    /// 比赛节奏（参考守望先锋的反馈节奏）。PC 和 VR 共用的规则都在这里；VR 只在下面的事件上加自己的呈现
    /// （驾驶舱灯条、空间音效、浮字），PC 用界面（DivaShowHud / DivaShowCoach / DivaShowHero）。
    /// · 糖果 +15：赛道上的糖果（DivaShowPickups）吃到加分、充一点能量，连着吃音调越来越高；小火箭吃到就冲刺。
    /// · 燃烧 ON FIRE：打靶 / 吃糖 / 完成任务攒火焰条，满了进入 12 秒燃烧——得分 ×1.5、横幅、音乐加强；不操作慢慢冷却。
    /// · 黄金靶子：每 24 秒左右前方一个靶子变金 10 秒，三倍分，靶子后面立一个金色底盘。
    /// · 低水量：水量低于 25% 时每 6 秒一声提示 + 教练提醒去补水。
    /// · 圈速奖牌：冲线按用时给金 / 银 / 铜，英雄时刻卡上盖章，结算页也写。
    /// · 冲刺：吃到火箭、大招放完，大象自动全速往前冲 2.5 秒（1.6 倍速，推进器喷火，速度线）。
    /// · 辅助瞄准：喷水期间锁定的靶子不换（不会在两个靶子之间跳），金靶子优先；水炮打锁定的那个，界面的锁定标记指哪就打哪。
    /// · 喷水耗水 0.022 / 次（控制状态默认 0.008 几乎用不完，补水就没有意义了）。
    /// </summary>
    public partial class DivaShowDirector
    {
        [Header("Race meta (shared by PC and VR)")]
        [Tooltip("Candy on the course: points per candy (rockets give the same and a dash). Off = no candy.")]
        public bool candies = true;
        public int candyPoints = 15;
        [Tooltip("Golden target: one target ahead turns gold for goldSeconds (3x points) every goldEvery seconds.")]
        public float goldEvery = 24f, goldSeconds = 10f;
        [Tooltip("ON FIRE: seconds of x1.5 score once the fire meter is full (a hit adds .22, candy .08, a task .3).")]
        public float fireSeconds = 12f;
        [Tooltip("Lap medals by run time in seconds: gold at or under the first, silver at or under the second, else bronze. The course is ~110 m; a clean run with dashes takes ~30 s, a first-timer 60-90 s.")]
        public float goldLap = 50f, silverLap = 80f;
        [Tooltip("Rocket dash (rocket pickup, after the ultimate): seconds and run-speed multiplier.")]
        public float dashSeconds = 2.5f, dashSpeedScale = 1.6f;
        [Tooltip("Water used per spray burst (the control state's default .008 never runs out; .022 makes refilling matter).")]
        public float waterPerBurst = .022f;
        [Tooltip("Aim assist: a locked target stays locked while it is within this many degrees past the cannon's cone.")]
        public float assistDegrees = 10f;

        /// <summary>燃烧时的得分倍率（糖果和靶子）。</summary>
        public float ScoreMultiplier { get; private set; } = 1;
        public bool OnFire => Time.unscaledTime < fireUntil;
        /// <summary>火焰条 0..1（燃烧中是剩余时间的比例）。</summary>
        public float FireLevel => OnFire ? Mathf.Clamp01((fireUntil - Time.unscaledTime) / Mathf.Max(.1f, fireSeconds)) : fire;
        public DivaTarget GoldenTarget => golden;
        /// <summary>本局的圈速奖牌（文字 key：medal.gold / medal.silver / medal.bronze）；冲线前和超时是空的。</summary>
        public string Medal { get; private set; } = "";
        public bool Dashing => Time.time < dashUntil;
        public int Candies => stats.candies;
        internal DivaShowFx Fx => fx;   // （Sound 在 DivaShowDirector 里，VR 分支早就有）

        // VR 的呈现挂在这些事件上（驾驶舱灯条、金环、空间音效、浮字）；PC 用界面和 Popup
        public event Action<bool> FireChanged;
        public event Action<DivaTarget> GoldenChanged;
        public event Action<string> Dashed;
        /// <summary>世界里某一点的浮字（VR 接管；没人接管就画成界面上的 Popup）。</summary>
        public event Action<string, Vector3, Color> WorldText;

        static readonly Color FireColour = new Color(1f, .55f, .2f), GoldColour = new Color(1f, .85f, .25f),
                              CandyColour = new Color(1f, .8f, .3f), RocketColour = new Color(1f, .6f, .9f);
        float fire, fireUntil = -1, lastGold = -10, goldUntil = -1, lastWaterBeep = -10, dashUntil = -1, baseRunSpeed = -1;
        int candyCombo; float lastCandyAt = -10;
        DivaTarget golden, lockTarget; int goldenPoints;
        Transform goldRing; Material goldMat;
        DivaShowPickups pickups;

        void MetaStart()
        {
            if (laser) laser.targetOverride = AssistTarget;
            if (locomotion) baseRunSpeed = locomotion.runSpeed;
            if (demo) demo.State.WaterPerBurst = waterPerBurst;
            if (candies && glowMaterial) pickups = gameObject.AddComponent<DivaShowPickups>();
            if (glowMaterial)
            {
                // 金色底盘：圆柱压成一个圆盘，立在靶子后面正对镜头（不挡靶子、不穿过靶子）
                goldMat = new Material(glowMaterial) { name = "Diva Golden Ring" };
                goldMat.SetTexture("_BaseMap", Texture2D.whiteTexture);
                Tint(goldMat, GoldColour * 2.2f);
                var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                var col = ring.GetComponent<Collider>(); if (col) Destroy(col);
                ring.name = "Diva Golden Ring"; ring.transform.SetParent(transform, true);
                var r = ring.GetComponent<MeshRenderer>();
                r.sharedMaterial = goldMat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
                goldRing = ring.transform; goldRing.gameObject.SetActive(false);
            }
            CoachStart();
            BuildWall();
        }

        static void Tint(Material m, Color c)
        {
            if (!m) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        void MetaDisable()
        {
            StopDash();
            EndGold();
            if (laser) laser.targetOverride = null;
            if (locomotion) locomotion.externalForward = 0;
        }

        void MetaStartRun()
        {
            fire = 0; fireUntil = -1; ScoreMultiplier = 1; Medal = ""; candyCombo = 0; lastCandyAt = -10; lastWaterBeep = -10; lockTarget = null;
            EndGold(); lastGold = Time.unscaledTime; StopDash();
            if (demo) demo.State.WaterPerBurst = waterPerBurst;
            if (pickups) pickups.Restore();
            WallReset();
            FireChanged?.Invoke(false);
            CoachStartRun();
        }

        void MetaPhase(ShowPhase previous, ShowPhase next)
        {
            // 大招放完接着冲出去（泡泡炸开回到比赛的那一刻）
            if (next == ShowPhase.Play && previous == ShowPhase.Ult) Dash("ult");
            if (next != ShowPhase.Play && next != ShowPhase.Ult) { StopDash(); EndGold(); }
            if (next == ShowPhase.Finish)
            {
                // 圈速奖牌：按 Eva 的计时（大象离开起点 1.5 米开始算）；超时没有奖牌
                float lap = game ? game.RunSeconds : finishedTime;
                Medal = timeUp ? "" : lap <= goldLap ? "medal.gold" : lap <= silverLap ? "medal.silver" : "medal.bronze";
                if (Medal != "") Debug.Log($"DIVA_SHOW medal {Medal} lap {lap:F1} s");
                if (OnFire) { fireUntil = -1; ScoreMultiplier = 1; FireChanged?.Invoke(false); }
                sound.MusicBoost(.45f, 1);
                if (!DivaVR.Active) HeroBegin();   // VR 的英雄时刻卡在驾驶舱前（DivaVRCelebration）
            }
            else HeroStop();
        }

        void MetaUpdate()
        {
            bool play = Phase == ShowPhase.Play || Phase == ShowPhase.Ult;
            float now = Time.unscaledTime;
            if (dashUntil > 0 && (Time.time >= dashUntil || !play)) StopDash();
            if (!play) return;
            // 火焰条：不操作慢慢冷却（燃烧中不掉）；燃烧到时间结束
            if (!OnFire) fire = Mathf.MoveTowards(fire, 0, Time.unscaledDeltaTime * .04f);
            if (fireUntil > 0 && now >= fireUntil) { fireUntil = -1; ScoreMultiplier = 1; sound.MusicBoost(.45f, 1); FireChanged?.Invoke(false); }
            // 黄金靶子
            if (Phase == ShowPhase.Play && golden == null && now - lastGold > goldEvery) StartGold();
            if (golden != null)
            {
                if (goldRing && mainCamera)
                {
                    var toCam = Vector3.ProjectOnPlane(mainCamera.transform.position - golden.AimPoint, Vector3.up);
                    if (toCam.sqrMagnitude < .01f) toCam = -Forward;
                    goldRing.position = golden.AimPoint - toCam.normalized * .5f;
                    goldRing.rotation = Quaternion.LookRotation(toCam.normalized) * Quaternion.Euler(90, 0, 0);
                    float d = 2f + .15f * Mathf.Sin(now * 5); goldRing.localScale = new Vector3(d, .04f, d);
                }
                if (now >= goldUntil || !golden.IsUp) EndGold();
            }
            // 低水量
            if (Phase == ShowPhase.Play && demo.State.Water < .25f && now - lastWaterBeep > 6f)
            {
                lastWaterBeep = now;
                sound.Play("beep", .5f, .8f);
                CoachTip("coach.water", 3.5f);
                Debug.Log($"DIVA_SHOW water low {demo.State.Water:F2}");
            }
            CoachUpdate();
        }

        /// <summary>攒火焰条；满了进入燃烧。</summary>
        void Heat(float amount)
        {
            if (OnFire || Phase != ShowPhase.Play) return;
            fire = Mathf.Clamp01(fire + amount);
            if (fireSlot != null) fireSlot.flash = DivaClock.Time;
            if (fire < 1) return;
            fireUntil = Time.unscaledTime + fireSeconds; fire = 0; ScoreMultiplier = 1.5f;
            Banner(DivaText.T("fire"), DivaText.T("fire.b"), FireColour, 2.2f, null);
            sound.Play("fanfare", .5f, 1.2f); sound.MusicBoost(.6f, 1.06f);
            if (look) look.Pulse(.6f);
            Feed(DivaText.T("fire"), FireColour);
            replay.Mark(Time.time - runStart, "fire", 40, "fire");
            FireChanged?.Invoke(true);
            Debug.Log("DIVA_SHOW on fire");
        }

        /// <summary>
        /// 吃到道具：加分（燃烧 ×1.5）、充一点能量、攒火焰条，连着吃音越来越高；火箭还会冲刺。
        /// 返回实际加的分；不在 Play 阶段返回 0。
        /// </summary>
        public int Collect(int points, Vector3 at, bool rocket = false)
        {
            if (Phase != ShowPhase.Play) return 0;
            int gained = Mathf.RoundToInt(points * ScoreMultiplier);
            bonus += gained;
            charge = Mathf.Min(100, charge + 4);
            stats.candies++;
            candyCombo = Time.time - lastCandyAt < 3.5f ? candyCombo + 1 : 0; lastCandyAt = Time.time;
            float pitch = 1 + .06f * Mathf.Min(candyCombo, 8);
            if (DivaVR.Active) sound.PlayAt("candy", at, .9f, pitch); else sound.Play("candy", .9f, pitch);
            Feed(DivaText.F("feed.candy", gained), CandyColour);
            WorldPopup("+" + gained, at + Vector3.up * .4f, CandyColour);
            Heat(.08f);
            Debug.Log($"DIVA_SHOW candy +{gained} chain {candyCombo}" + (rocket ? " rocket" : ""));
            if (rocket) { WorldPopup(DivaText.T("feed.rocket"), at + Vector3.up * 1f, RocketColour); Dash("rocket"); }
            return gained;
        }

        void WorldPopup(string text, Vector3 world, Color colour)
        {
            if (WorldText != null) WorldText(text, world, colour);
            else Popup(text, world, colour);
        }

        /// <summary>冲刺（吃到火箭、大招放完）：2.5 秒自动全速前冲，推进器喷火，速度线，横幅。</summary>
        public void Dash(string why)
        {
            if (Phase != ShowPhase.Play && Phase != ShowPhase.Ult) return;
            dashUntil = Time.time + dashSeconds;
            if (locomotion)
            {
                if (baseRunSpeed <= 0) baseRunSpeed = locomotion.runSpeed;
                locomotion.runSpeed = baseRunSpeed * dashSpeedScale;
                locomotion.externalForward = 1;
            }
            if (boosters) boosters.SetBoost(1);
            boosting = true; rushCooldown = Time.time + 8;
            if (boostSlot != null) boostSlot.flash = DivaClock.Time;
            string key = why == "ult" ? "dash.ult" : "dash";
            Banner(DivaText.T(key), DivaText.T("dash.b"), RocketColour, 1.6f, null);
            Feed(DivaText.T(key), RocketColour);
            sound.Play("whoosh", .8f); sound.Play("rise", .5f);
            if (bubbles) bubbles.Burst(1.2f);
            if (look) look.Pulse(.5f);
            replay.Mark(Time.time - runStart, "dash", 30, "dash");
            Dashed?.Invoke(why);
            Debug.Log("DIVA_SHOW dash " + why);
        }

        void StopDash()
        {
            if (dashUntil < 0) return;
            dashUntil = -1;
            if (locomotion) { if (baseRunSpeed > 0) locomotion.runSpeed = baseRunSpeed; locomotion.externalForward = 0; }
            // 冲线那一刻 EnterFinish 自己把推进器点亮了，别在这里关掉
            if (boosters && (Phase == ShowPhase.Play || Phase == ShowPhase.Ult)) boosters.SetBoost(-1);
            boosting = false;
        }

        void StartGold()
        {
            var from = elephant.position; var fwd = Forward;
            var candidates = DivaTarget.All.Where(t => t && t.IsUp && Vector3.Distance(t.AimPoint, from) < 45 &&
                                                       Vector3.Dot(fwd, (t.AimPoint - from).normalized) > -.2f).ToList();
            if (candidates.Count == 0) { lastGold = Time.unscaledTime; return; }
            golden = candidates[UnityEngine.Random.Range(0, candidates.Count)];
            goldenPoints = golden.points; golden.points = goldenPoints * 3;
            goldUntil = Time.unscaledTime + goldSeconds; lastGold = goldUntil;
            if (goldRing) goldRing.gameObject.SetActive(true);
            Banner(DivaText.T("golden"), DivaText.T("golden.b"), GoldColour, 2f, null);
            sound.Play("skill", .9f, 1.3f);
            WorldPopup(DivaText.T("golden"), golden.AimPoint + Vector3.up * 1.5f, GoldColour);
            GoldenChanged?.Invoke(golden);
            Debug.Log($"DIVA_SHOW golden target {golden.name} for {goldSeconds} s");
        }

        void EndGold()
        {
            if (golden != null) { golden.points = goldenPoints; golden = null; GoldenChanged?.Invoke(null); }
            if (goldRing) goldRing.gameObject.SetActive(false);
        }

        /// <summary>
        /// 辅助瞄准：水炮每一发打哪个靶子（DivaLaserBlaster.targetOverride）。喷水期间锁定的靶子不换——只要它还立着、
        /// 还在锥角 + assistDegrees 以内；金靶子在锥角里就优先锁它。界面的锁定标记用同一个判定，所以标记指哪就打哪。
        /// </summary>
        DivaTarget AssistTarget(float aimDegrees)
        {
            if (!laser || !elephant) return null;
            Vector3 origin = laser.muzzle ? laser.muzzle.position : elephant.position + Vector3.up * 2, forward = laser.AimForward(aimDegrees);
            bool InCone(DivaTarget t, float extra)
            {
                if (!t || !t.IsUp) return false;
                var off = t.AimPoint - origin;
                return off.magnitude <= laser.range + extra * .3f &&
                       Vector3.Angle(forward, Vector3.ProjectOnPlane(off, Vector3.up)) <= laser.coneDegrees + extra;
            }
            if (golden && InCone(golden, 0)) lockTarget = golden;
            else if (!(demo && demo.State.Shoot && InCone(lockTarget, assistDegrees))) lockTarget = laser.FindTarget(aimDegrees);
            return lockTarget;
        }
    }
}
