using UnityEngine;

namespace Diva.Show
{
    // 选涂装（转台 + 自动校准）→ 机甲启动特写 → 3·2·1 GO
    public partial class DivaShowDirector
    {
        Transform selectRing;
        float orbitAngle, orbitVelocity, orbitCenter, orbitDrag;

        void EnterSelect()
        {
            Time.timeScale = 1;
            ResetElephant();
            if (game) game.ResetRun();
            bonus = 0; combo = 0; charge = 0;
            ApplyPlayerMode();
            // Eva 的圆形起点台自带旋转的霓虹圈，有它时不再加我们的光环（避免两个圈叠在一起）
            if (!selectRing && fx != null && !GameObject.Find("Start pad")) selectRing = fx.SelectRing();
            if (selectRing) selectRing.gameObject.SetActive(true);
            orbitCenter = BestAngle(ElephantSize * 1.7f, ElephantSize * .2f, 80);
            orbitDrag = 0;
            RefreshSelect();
            entranceNext = 0;
            // 有 Eva 的开场时，出场音效放在开场里（大象出场那一刻）；没有时在这里播，配烟火
            if (entranceMusic && !raceIntro) sound.PlayMusic(entranceMusic, entranceVolume);
        }

        // 出场开场的节拍（秒）：两组"嘚嘚"和一声重击，和 Source~/make_entrance.py 一致
        static readonly float[] EntranceHits = { 0f, .16f, .62f, .78f, 1.24f };
        int entranceNext;

        void UpdateEntrance()
        {
            if (!entranceMusic || raceIntro) return;
            float t = PhaseTime;
            while (entranceNext < EntranceHits.Length && t >= EntranceHits[entranceNext])
            {
                bool big = entranceNext == EntranceHits.Length - 1;
                // 烟火放在镜头看过去的左右两边、大象稍后方，把大象框在中间
                var view = mainCamera ? Vector3.ProjectOnPlane(mainCamera.transform.forward, Vector3.up).normalized : Forward;
                var side = Vector3.Cross(Vector3.up, view);
                var basePos = new Vector3(elephant.position.x, startPosition.y, elephant.position.z) + view * elephantRadius * .6f;
                float s = entranceNext % 2 == 0 ? -1 : 1;
                if (big)
                {
                    fx.Pyro(basePos - side * elephantRadius * 1.15f, Accent * 3f, true);
                    fx.Pyro(basePos + side * elephantRadius * .55f, Primary * 3f, true);
                    fx.Pyro(basePos + view * elephantRadius * .6f - side * elephantRadius * .3f, Color.white * 2.5f, true);
                    shake = .25f;
                }
                else
                {
                    fx.Pyro(basePos + side * s * elephantRadius * (s < 0 ? 1.15f : .55f), (entranceNext < 2 ? Primary : Accent) * 3f, false);
                    shake = .08f;
                }
                entranceNext++;
            }
        }

        void ApplyPlayerMode()
        {
            // 单人游戏：一个人控制全部（Try alone + All）。只在模式真的变了时才切换，因为切换会让校准失效。
            bool wantSolo = singlePlayer;
            int wantRole = singlePlayer ? 0 : demo.soloRole;
            if (demo.solo != wantSolo || wantSolo && demo.soloRole != wantRole) demo.SetSolo(wantSolo, wantRole);
        }

        void SetPlayers(bool one)
        {
            singlePlayer = one;
            ApplyPlayerMode();
            sound.Play("tick");
            RefreshSelect();
        }

        void SwitchSkin(int step)
        {
            if (!skins) return;
            skins.Next(step);
            sound.Play("tick");
        }

        void PickSkin(int index)
        {
            if (!skins) return;
            skins.Apply(index);
            sound.Play("tick");
        }

        void OnSkinChanged(int index) => RefreshSelect();

        void ConfirmSelect()
        {
            if (Phase != ShowPhase.Select) return;
            sound.Play("confirm");
            Enter(raceIntro ? ShowPhase.RaceIntro : ShowPhase.PowerUp);
        }

        void UpdateSelect()
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) SwitchSkin(-1);
                if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) SwitchSkin(1);
                if (kb.digit1Key.wasPressedThisFrame) SetPlayers(true);
                if (kb.digit3Key.wasPressedThisFrame) SetPlayers(false);
            }
            UpdateEntrance();
            int step = AimStep();
            if (step != 0) SwitchSkin(step);
            if (confirmEdge || ultEdge) { ConfirmSelect(); return; }

            // 摄像头模式：所有人入镜后自动开始 10 秒校准（就是原来的 Get ready）
            float now = Time.realtimeSinceStartup;
            string status;
            if (!controller || controller.inputMode != DigiPhant.InputMode.Camera) status = DivaText.T("status.test");
            else if (controller.CalibrationPending) status = DivaText.F("status.calibrating", Mathf.CeilToInt(controller.CalibrationSecondsRemaining(now)));
            else if (controller.IsCalibrated) status = "<color=#7CFC9A>" + DivaText.T("status.ready") + "</color>";
            else if (demo.AllVisible(now)) { controller.BeginCalibrationCountdown(now); status = DivaText.F("status.calibrating", 10); }
            else status = singlePlayer ? DivaText.T("status.waiting") : DivaText.F("status.waiting3", MissingPlayers(now));
            statusText.text = status;

            var mouse = UnityEngine.InputSystem.Mouse.current;
            bool overUi = UnityEngine.EventSystems.EventSystem.current && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
            // 在最空的那段弧里来回慢慢转（±40°）；拖动鼠标可以自己转到任何角度
            if (mouse != null && mouse.leftButton.isPressed && !overUi) orbitDrag += mouse.delta.ReadValue().x * .25f;
            orbitAngle = orbitCenter + orbitDrag + 40 * Mathf.Sin(PhaseTime * .25f);
            selectTitle.alpha = DivaUi.Smooth(PhaseTime / .5f);
        }

        string MissingPlayers(float now)
        {
            var list = new System.Collections.Generic.List<string>();
            for (int i = 1; i <= 3; i++) if (!demo.State.Visible(i, now)) list.Add("P" + i);
            return string.Join(", ", list);
        }

        void CameraSelect()
        {
            var center = ElephantCenter;
            float size = ElephantSize;
            var dir = Quaternion.AngleAxis(orbitAngle, Vector3.up) * Forward;
            // 开场时从远处推近，正好在重击（1.24 秒）落位
            float intro = DivaUi.EaseOut(PhaseTime / 1.25f);
            var pos = center + dir * size * Mathf.Lerp(2.8f, 1.7f, intro) + Vector3.up * size * Mathf.Lerp(.5f, .2f, intro);
            // 大象偏左，右边留给按钮
            var right = Vector3.Cross(Vector3.up, (center - pos).normalized);
            SetCamera(pos, center + right * size * .3f + Vector3.down * size * .06f, 36);
            if (selectRing)
            {
                selectRing.position = new Vector3(elephant.position.x, startPosition.y + .04f, elephant.position.z);
                selectRing.rotation = Quaternion.Euler(90, DivaClock.Time * 12, 0);
                selectRing.localScale = Vector3.one * size * .95f * DivaUi.EaseOutBack(PhaseTime / .6f);
            }
        }

        // ------------------------------------------------------------------ mech power-up
        Vector3 powerFrom;
        Quaternion powerFromRot;
        void EnterPowerUp()
        {
            if (selectRing) selectRing.gameObject.SetActive(false);
            powerFrom = mainCamera ? mainCamera.transform.position : elephant.position;
            powerFromRot = mainCamera ? mainCamera.transform.rotation : Quaternion.identity;
            sound.Play("powerup");
            SetMechGlow(0);
            Banner(DivaText.F("powerup", SkinName), null, Accent, 2.4f, null);
        }

        void CameraPowerUp()
        {
            float t = PhaseTime;
            sound.FadeMusic(.5f);
            // 发光部件依次亮起：先全暗，再闪两下到最亮，最后回到正常
            float glow = t < .5f ? 0 : t < 1.4f ? Mathf.Lerp(0, 1.6f, (t - .5f) / .9f) * (.75f + .25f * Mathf.Sin(t * 40)) : Mathf.Lerp(1.6f, 1, (t - 1.4f) / .8f);
            if (t < 2.3f) SetMechGlow(glow); else ClearMechGlow();
            if (boosters) boosters.SetBoost(t > 1.2f && t < 2.0f ? 1 : -1);
            if (bubbles && t > 1.6f && t < 1.65f) bubbles.Burst(.6f);

            var fwd = Forward; var right = Vector3.Cross(Vector3.up, fwd);
            var center = ElephantCenter; float size = ElephantSize;
            var rear = center - fwd * size * .9f - right * size * .55f + Vector3.up * size * .25f;
            FollowPose(out var follow, out var followLook);
            if (t < 1.3f)
            {
                float k = DivaUi.Smooth(t / 1.3f);
                var pos = Vector3.Lerp(powerFrom, rear, k);
                var rot = Quaternion.Slerp(powerFromRot, Quaternion.LookRotation(center + Vector3.up * size * .05f - rear), k);
                SetCamera(pos, rot, Mathf.Lerp(38, 34, k));
            }
            else
            {
                float k = DivaUi.Smooth((t - 1.3f) / 1.3f);
                var pos = Vector3.Lerp(rear, follow, k);
                var look = Vector3.Lerp(center + Vector3.up * size * .05f, followLook, k);
                SetCamera(pos, look, Mathf.Lerp(34, defaultFov, k));
            }
        }

        // ------------------------------------------------------------------ 3 · 2 · 1 · GO
        int lastCount;
        void EnterCountdown()
        {
            ClearMechGlow();
            if (boosters) boosters.SetBoost(-1);
            lastCount = 4;
            if (mainCamera)
            {
                FollowPose(out var pos, out var look);
                mainCamera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
                mainCamera.fieldOfView = defaultFov;
            }
        }

        void UpdateCountdown()
        {
            float t = PhaseTime;
            int n = 3 - Mathf.FloorToInt(t);
            if (n != lastCount && n >= 1) { sound.Play("beep"); lastCount = n; }
            float k = t - Mathf.Floor(t);
            countdownText.text = n >= 1 ? n.ToString() : "";
            countdownText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.8f, 1, DivaUi.EaseOutBack(k / .35f));
            countdownText.alpha = 1 - DivaUi.Smooth((k - .7f) / .3f);
            if (t >= 3 || skipEdge)
            {
                sound.Play("go");
                Enter(ShowPhase.Play);
                countdownRoot.gameObject.SetActive(true);
                countdownText.text = DivaText.T("go");
                countdownText.alpha = 1;
                countdownText.rectTransform.localScale = Vector3.one * 1.3f;
            }
        }
    }
}
