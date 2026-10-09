using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Diva.Show
{
    // 游戏中：技能判定、连击、大招充能和"超级泡泡"过场、录制、结束条件。
    public partial class DivaShowDirector
    {
        const float ComboWindow = 2.5f;

        class RunStats
        {
            public float distance, topSpeed, turned, bubbleTime, sprayTime;
            public int hits, shots0, bestCombo, ults, ultHits;
            public readonly int[] roleHits = new int[4];
        }

        RunStats stats = new RunStats();
        DivaReplay replay;
        float runStart, charge, lastHitTime = -100, rushTime, rushCooldown, bubbleCooldownUntil, cannonCooldownUntil;
        int combo, bonus;
        bool boosting, wasShooting, wasDrinking;
        Vector3 lastPosition;
        float lastYaw;
        bool[] tasksDone = new bool[0];
        float bubbleCooldown => Mathf.Clamp01(1 - (bubbleCooldownUntil - Time.time) / 6f);
        float RunTime => Phase == ShowPhase.Play || Phase == ShowPhase.Ult ? Time.time - runStart : finishedTime;
        float finishedTime;
        bool timeUp;

        void StartRun()
        {
            if (game) game.ResetRun();
            demo.State.FillTank();
            stats = new RunStats { shots0 = laser ? laser.Shots : 0 };
            replay?.Clear();
            runStart = Time.time;
            charge = 0; combo = 0; bonus = 0; lastHitTime = -100; rushTime = 0; rushCooldown = 0;
            bubbleCooldownUntil = cannonCooldownUntil = 0; boosting = wasShooting = wasDrinking = false; timeUp = false;
            lastPosition = elephant.position; lastYaw = elephant.eulerAngles.y;
            tasksDone = game ? new bool[game.tasks.Length] : new bool[0];
            foreach (var f in feed) if (f.plate) Destroy(f.plate.gameObject);
            feed.Clear();
        }

        void UpdatePlay()
        {
            if (PhaseTime > 1 && countdownRoot.gameObject.activeSelf) countdownRoot.gameObject.SetActive(false);
            else if (countdownRoot.gameObject.activeSelf) countdownText.alpha = 1 - DivaUi.Smooth((PhaseTime - .5f) / .5f);
            float t = Time.time - runStart, dt = Time.deltaTime;

            // 统计
            var p = elephant.position;
            float moved = Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(lastPosition.x, 0, lastPosition.z));
            if (moved < 2) { stats.distance += moved; charge = Mathf.Min(100, charge + moved * chargePerMetre); }
            lastPosition = p;
            float yaw = elephant.eulerAngles.y;
            stats.turned += Mathf.Abs(Mathf.DeltaAngle(lastYaw, yaw)); lastYaw = yaw;
            float speed = Mathf.Abs(locomotion.CurrentSpeed);
            stats.topSpeed = Mathf.Max(stats.topSpeed, speed);
            if (demo.State.Drink) stats.bubbleTime += dt;
            if (demo.State.Shoot) stats.sprayTime += dt;

            // 推进冲刺：接近最高速持续 1 秒
            bool fast = speed >= locomotion.runSpeed * .88f;
            rushTime = fast ? rushTime + dt : 0;
            if (boosting && !fast) boosting = false;
            if (!boosting && rushTime > 1 && Time.time > rushCooldown)
            {
                boosting = true; rushCooldown = Time.time + 8;
                if (look) look.Pulse(.6f);
                boostSlot.flash = DivaClock.Time;
                Banner(DivaText.T("skill.boost"), null, Primary);
                Pip("boost", DivaText.T("skill.boost"));
                Feed(DivaText.T("skill.boost"), Primary);
                replay.Mark(t, "boost", 25, "skill.boost");
            }

            // 象鼻水炮：开始喷水时（8 秒内只弹一次横幅）
            bool shooting = demo.State.Shoot;
            if (shooting && !wasShooting)
            {
                cannonSlot.flash = DivaClock.Time;
                if (Time.time > cannonCooldownUntil)
                {
                    cannonCooldownUntil = Time.time + 8;
                    Banner(DivaText.T("skill.cannon"), null, new Color(.35f, .8f, 1f), 1.3f);
                    Pip("cannon", DivaText.T("skill.cannon"));
                }
            }
            wasShooting = shooting;

            // 泡泡补给：开始喝水
            bool drinking = demo.State.Drink;
            if (drinking && !wasDrinking)
            {
                bubbleSlot.flash = DivaClock.Time;
                if (Time.time > bubbleCooldownUntil)
                {
                    bubbleCooldownUntil = Time.time + 6;
                    Banner(DivaText.T("skill.bubble"), null, new Color(.85f, .6f, 1f), 1.4f);
                    Pip("bubble", DivaText.T("skill.bubble"));
                    Feed(DivaText.T("skill.bubble"), new Color(.85f, .6f, 1f));
                    replay.Mark(t, "bubble", 12, "skill.bubble");
                }
            }
            wasDrinking = drinking;

            // 任务完成
            if (game)
                for (int i = 0; i < game.tasks.Length && i < tasksDone.Length; i++)
                    if (game.tasks[i].Done && !tasksDone[i])
                    {
                        tasksDone[i] = true;
                        charge = Mathf.Min(100, charge + chargePerTask);
                        string label = DivaText.Task(game.tasks[i].label);
                        Banner(DivaText.T("skill.task"), label, new Color(.45f, 1f, .6f));
                        Feed(DivaText.T("skill.task") + " · " + label, new Color(.45f, 1f, .6f));
                        replay.Mark(t, "task", 45, "skill.task");
                    }

            // 大招
            if (charge >= 100 && !chargeReadyAnnounced) { chargeReadyAnnounced = true; sound.Play("ready"); }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            bool ultKey = kb != null && kb.uKey.wasPressedThisFrame;
            if (charge >= 100 && (ultEdge || ultKey)) { Enter(ShowPhase.Ult); return; }

            replay.Capture(t, boosters ? boosters.Level : 0, locomotion.CurrentSpeed, waterGun && waterGun.IsSpraying,
                           bubbles && bubbles.IsBlowing, waterGun ? waterGun.jetSpeed : 0);

            // 结束：Eva 的四个任务全部完成，或者超时
            if (game && game.Finished) { Enter(ShowPhase.Finish); return; }
            if (timeLimit > 0 && t >= timeLimit) { timeUp = true; Enter(ShowPhase.Finish); return; }
            var kb2 = UnityEngine.InputSystem.Keyboard.current;
            if (kb2 != null && kb2.endKey.wasPressedThisFrame) { timeUp = true; Enter(ShowPhase.Finish); }
        }

        bool chargeReadyAnnounced;

        /// <summary>Testing: fill the ultimate charge (also handy in the Inspector's context menu).</summary>
        [ContextMenu("Fill ultimate charge")]
        public void FillCharge() => charge = 100;
        public float Charge => charge;
        public int RunHits => stats.hits;

        void OnTargetHit(DivaTarget target)
        {
            if (Phase != ShowPhase.Play && Phase != ShowPhase.Ult) return;
            float t = Time.time - runStart;
            stats.hits++;
            if (Phase == ShowPhase.Ult) stats.ultHits++;
            Popup("+" + target.points, target.AimPoint, Color.white);
            sound.Play("hit", .8f);
            fx.Flash(target.AimPoint, Accent, 2.2f, .25f);
            // 大招打中的不算连击，也不再给大招充能
            if (Phase == ShowPhase.Ult) { replay.Mark(t, "hit", 10, "skill.ult", target); return; }
            charge = Mathf.Min(100, charge + chargePerHit);
            combo = DivaClock.Time - lastHitTime <= ComboWindow ? combo + 1 : 1;
            lastHitTime = DivaClock.Time;
            stats.bestCombo = Mathf.Max(stats.bestCombo, combo);
            Feed(DivaText.F("feed.hit", target.points), new Color(.35f, .8f, 1f));
            replay.Mark(t, "hit", 30, combo >= 3 ? "skill.combo3" : "skill.cannon", target);
            if (combo == 3 || combo == 5)
            {
                string key = combo == 3 ? "skill.combo3" : "skill.combo5";
                int extra = combo == 3 ? 30 : 60;
                bonus += extra;
                if (look) look.Pulse(.5f);
                Banner(DivaText.T(key), "+" + extra, Accent, 1.8f, "combo");
                Feed(DivaText.F("feed.combo", DivaText.T(key), extra), Accent);
                Pip("cannon", DivaText.T(key));
                replay.Mark(t, combo == 3 ? "combo3" : "combo5", combo == 3 ? 60 : 100, key);
            }
        }

        // ------------------------------------------------------------------ ultimate: MEGA BUBBLE
        Transform megaBubble;
        Vector3 ultOrigin, ultDirection, ultBubble;
        bool ultPopped;
        float ultRunStart;

        void EnterUlt()
        {
            charge = 0; chargeReadyAnnounced = false; ultPopped = false;
            stats.ults++;
            ultRunStart = Time.time - runStart;
            replay.Mark(ultRunStart, "ult", 150, "skill.ult");
            ultOrigin = ElephantCenter + Vector3.up * ElephantSize * .15f;
            ultDirection = Forward;
            sound.Play("whoosh");
            Banner(DivaText.T("skill.ult") + "!", null, Accent, 2.2f, "ready");
            if (bubbles) bubbles.Burst(2.5f);
            if (!megaBubble) megaBubble = fx.GiantBubble();
            megaBubble.gameObject.SetActive(true);
            megaBubble.localScale = Vector3.zero;
            Feed(DivaText.T("skill.ult"), Accent);
        }

        void UpdateUlt()
        {
            float t = PhaseTime;
            replay.Capture(Time.time - runStart, boosters ? boosters.Level : 0, 0, false, true, 0);
            // 泡泡从背上的炮舱冒出，长大，往前飞，然后爆开
            float grow = DivaUi.EaseOut(Mathf.Clamp01((t - .2f) / 1f));
            float fly = DivaUi.Smooth(Mathf.Clamp01((t - .7f) / .8f));
            float radius = Mathf.Lerp(.3f, 4.2f, grow);
            var center = ultOrigin + ultDirection * Mathf.Lerp(0, ultRange * .55f, fly) + Vector3.up * (radius * .6f + fly * 1.5f);
            ultBubble = center;
            if (megaBubble && !ultPopped)
            {
                megaBubble.position = center;
                if (mainCamera) megaBubble.rotation = Quaternion.LookRotation(megaBubble.position - mainCamera.transform.position);
                megaBubble.localScale = Vector3.one * radius * 2 * (1 + .04f * Mathf.Sin(t * 14));
            }
            if (!ultPopped && t >= 1.55f)
            {
                ultPopped = true;
                megaBubble.gameObject.SetActive(false);
                sound.Play("pop");
                fx.Pop(center, Accent, radius);
                if (look) look.Pulse(1);
                int count = 0;
                foreach (var target in DivaTarget.All.ToArray())
                {
                    if (!target || !target.IsUp) continue;
                    var off = target.AimPoint - ultOrigin;
                    float d = new Vector2(off.x, off.z).magnitude;
                    float ahead = Vector3.Dot(new Vector3(off.x, 0, off.z), ultDirection);
                    if (d <= ultRange && ahead > -2 && target.TryHit()) count++;
                }
                bonus += count * 10;
                Feed(DivaText.F("feed.ult", DivaText.T("skill.ult"), count), Accent);
            }
            // 爆开那一刻放慢
            Time.timeScale = t > 1.45f && t < 2.2f ? .35f : 1;
            if (t > 2.9f || skipEdge && t > .5f)
            {
                Time.timeScale = 1;
                if (megaBubble) megaBubble.gameObject.SetActive(false);
                Enter(ShowPhase.Play);
            }
        }

        void CameraUlt()
        {
            float t = PhaseTime;
            var fwd = ultDirection; var right = Vector3.Cross(Vector3.up, fwd);
            float size = ElephantSize;
            var center = ElephantCenter;
            if (t < 1.0f)
            {
                // 正面低角度特写
                float k = DivaUi.Smooth(t / 1.0f);
                var pos = center + fwd * size * Mathf.Lerp(.8f, .95f, k) + right * size * .25f + Vector3.down * size * .05f;
                SetCamera(pos, center + Vector3.up * size * .12f, Mathf.Lerp(40, 46, k));
            }
            else
            {
                // 拉到侧后上方，大象和飞出去的泡泡都在画面里
                float k = DivaUi.Smooth((t - 1f) / .7f);
                var a = center + fwd * size * .95f + right * size * .25f + Vector3.down * size * .05f;
                var b = center - fwd * size * .9f + right * size * 1.4f + Vector3.up * size * .6f;
                var look = Vector3.Lerp(center + Vector3.up * size * .12f, Vector3.Lerp(center, ultBubble, .55f), k);
                SetCamera(Vector3.Lerp(a, b, k), look, Mathf.Lerp(46, 58, k));
            }
        }
    }
}
