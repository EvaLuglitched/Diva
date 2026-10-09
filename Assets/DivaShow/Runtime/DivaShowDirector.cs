using System;
using System.Collections.Generic;
using System.Linq;
using DigiPhant;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Playables;

namespace Diva.Show
{
    public enum ShowPhase { Intro, Select, RaceIntro, PowerUp, Countdown, Play, Ult, Finish, Highlight, Results }

    /// <summary>
    /// 整局流程：（Eva 的开场）→ 选涂装 → 机甲启动 → 3·2·1 → 游戏（技能、大招）→ 结束 → 本局高光 → 结算和排行榜。
    /// 场景里只需要一个带这个组件的物体；界面、镜头、特效和音效都在运行时生成。
    /// 不改 Eva 的游戏规则：分数、打靶和任务仍由 DivaGameManager 判定，这里只读它，另外加连击和大招奖励分。
    /// 执行顺序 1500：在 DivaDemo（200）、机甲手势连接（210）和 Eva 的天空镜头（1000）之后，
    /// 在 Eva 的开场（2000）之前。过场镜头和回放姿势由这里最后写；游戏中不碰镜头，天空镜头照常上仰。
    /// </summary>
    [DefaultExecutionOrder(1500)]
    [DisallowMultipleComponent]
    public partial class DivaShowDirector : MonoBehaviour
    {
        [Header("Scene (found automatically when empty)")]
        public DivaDemo demo;
        public DigiPhantController controller;
        public DigiPhantLocomotion locomotion;
        public DivaGameManager game;
        public DivaLaserBlaster laser;
        public DivaMechSkins skins;
        public DivaBoosters boosters;
        public DivaBubbleCannons bubbles;
        public DivaTrunkBlaster waterGun;
        public Camera mainCamera;

        [Header("Eva's race intro")]
        [Tooltip("Eva's DivaIntro (on Diva Game Layer). Played after the skin select, with our entrance sting; its 3-2-1-GO starts the run.")]
        public DivaIntro raceIntro;
        [Tooltip("Optional Timeline played before the skin select. Empty = start at the skin select.")]
        public PlayableDirector introDirector;
        [Tooltip("Global light and post-processing polish (Assets/DivaShow/Runtime/DivaLook.cs).")]
        public DivaLook look;

        [Header("Look (filled in by Diva > Show > Install)")]
        public Font latinFont;
        public Shader sdfShader;
        public Material glowMaterial;
        public Material bubbleMaterial;
        public AnimationClip trumpetClip;
        [Tooltip("Short entrance sting played when the skin select opens (original, made by Source~/make_entrance.py). Swap in any licensed clip.")]
        public AudioClip entranceMusic;
        [Range(0, 1)] public float entranceVolume = .9f;

        [Header("Rules")]
        [Tooltip("One person plays with every control (single-player game). Off = three players, one role each.")]
        public bool singlePlayer = true;
        [Tooltip("Seconds per run; 0 = no limit (the run ends when every course task is done).")]
        [Min(0)] public float timeLimit = 0;
        [Min(3)] public float highlightSeconds = 8;
        [Tooltip("Ultimate charge: per target hit, per course task, per metre travelled (100 = full).")]
        public float chargePerHit = 12, chargePerTask = 25, chargePerMetre = 1.2f;
        [Tooltip("Mega bubble reaches targets within this distance in front of the elephant.")]
        public float ultRange = 18;
        [Range(0, 1)] public float volume = .7f;
        [Tooltip("Hide Eva's IMGUI HUD and the setup panel while the show runs. F1 or SETUP shows the setup panel.")]
        public bool hideDebugPanels = true;

        public static event Action<ShowPhase> PhaseChanged;
        public ShowPhase Phase { get; private set; } = ShowPhase.Intro;
        public float PhaseTime => DivaClock.Time - phaseStart;
        /// <summary>Score shown by the show: the game's score plus combo and ultimate bonuses.</summary>
        public int TotalScore => (game ? game.Score : 0) + bonus;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => PhaseChanged = null;

        float phaseStart;
        DivaShowSound sound;
        DivaShowFx fx;
        Transform elephant;
        SkinnedMeshRenderer elephantSkin;
        Animator animator;
        Vector3 startPosition;
        Quaternion startRotation;
        Vector3 motionRootPosition, centerOffset;
        float elephantRadius = 3;
        Quaternion motionRootRotation;
        float defaultFov;
        bool previousHud, previousControls, previousSpace;
        bool ultArmed, confirmEdge, ultEdge, skipEdge;
        float aimHeld;
        bool cameraSet;
        Vector3 cameraPosition;
        Quaternion cameraRotation;
        float cameraFov;

        // ------------------------------------------------------------------ setup
        void Awake()
        {
            DivaText.Set(Lang.En);
            if (!demo) demo = FindAnyObjectByType<DivaDemo>();
            if (!controller && demo) controller = demo.GetComponent<DigiPhantController>();
            if (!locomotion && demo) locomotion = demo.GetComponent<DigiPhantLocomotion>();
            if (!game) game = FindAnyObjectByType<DivaGameManager>();
            if (!laser) laser = FindAnyObjectByType<DivaLaserBlaster>();
            if (!skins) skins = FindAnyObjectByType<DivaMechSkins>();
            if (!boosters) boosters = FindAnyObjectByType<DivaBoosters>();
            if (!bubbles) bubbles = FindAnyObjectByType<DivaBubbleCannons>();
            if (!waterGun) waterGun = FindAnyObjectByType<DivaTrunkBlaster>();
            if (!mainCamera) mainCamera = demo && demo.thirdPersonCamera ? demo.thirdPersonCamera : Camera.main;
            if (!raceIntro) raceIntro = FindAnyObjectByType<DivaIntro>();
            if (!skyCamera) skyCamera = FindAnyObjectByType<DivaSkyCamera>();
            if (!look) look = GetComponent<DivaLook>();
            // Eva 的开场本来在进入 Play 时自动播放；整局流程里改成选完涂装后由这里调用（不改她的组件设置，只在运行时关掉）
            if (raceIntro) raceIntro.playOnStart = false;
        }

        DivaSkyCamera skyCamera;
        DigiPhantCameraPreview cameraPreview;
        float BaseFov => skyCamera && skyCamera.isActiveAndEnabled ? skyCamera.fieldOfView : defaultFov;

        void Start()
        {
            if (!demo || !locomotion || !locomotion.travelRoot)
            {
                Debug.LogWarning("DIVA_SHOW needs a DivaDemo with DigiPhantLocomotion in the scene; the show is off.");
                enabled = false;
                return;
            }
            elephant = locomotion.travelRoot;
            startPosition = elephant.position; startRotation = elephant.rotation;
            animator = locomotion.elephantAnimator;
            elephantSkin = animator ? animator.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(r => r.bones.Length).FirstOrDefault() : null;
            // 镜头取景按大象身体算：开始时量一次中心和半径（相对 travel root），之后不用渲染包围盒（它只在渲染后更新）
            if (elephantSkin)
            {
                var b = elephantSkin.bounds;
                centerOffset = elephant.InverseTransformPoint(b.center);
                elephantRadius = Mathf.Clamp(b.extents.magnitude, 1.5f, 6);
            }
            else centerOffset = Vector3.up * 1.5f;
            Debug.Log($"DIVA_SHOW elephant radius {elephantRadius:F2} center offset {centerOffset}");
            if (locomotion.animationMotionRoot)
            {
                motionRootPosition = locomotion.animationMotionRoot.localPosition;
                motionRootRotation = locomotion.animationMotionRoot.localRotation;
            }
            defaultFov = mainCamera ? mainCamera.fieldOfView : 60;
            sound = new DivaShowSound(gameObject) { volume = volume };
            fx = new DivaShowFx(transform, glowMaterial, bubbleMaterial);
            replay = new DivaReplay(elephant, elephantSkin ? elephantSkin.bones : new Transform[0], waterGun ? waterGun.transform : null);
            DivaUi.MakeFont(latinFont, sdfShader);
            if (!FindAnyObjectByType<EventSystem>())
            {
                var es = new GameObject("Diva Show Events", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }
            if (game) previousHud = game.showHud;
            if (controller) previousControls = controller.showControls;
            if (laser) previousSpace = laser.spaceToFire;
            if (hideDebugPanels)
            {
                if (game) game.showHud = false;
                if (controller) controller.showControls = false;
            }
            DivaTarget.Hit += OnTargetHit;
            DivaText.Changed += OnLanguage;
            if (skins) skins.Changed += OnSkinChanged;
            BuildUi();
            CollectOccluders();
            if (skins && skins.Current < 0) skins.Apply(skins.startSkin);
            Enter(ShowPhase.Intro);
        }

        void OnDisable()
        {
            DivaTarget.Hit -= OnTargetHit;
            DivaText.Changed -= OnLanguage;
            if (skins) skins.Changed -= OnSkinChanged;
            Time.timeScale = 1;
            if (demo) { demo.inputLocked = false; demo.cameraHeld = false; }
            if (cameraPreview) cameraPreview.showPreview = true;
            if (game) { game.showHud = previousHud; game.enabled = true; }
            if (controller) controller.showControls = previousControls;
            if (laser) laser.spaceToFire = previousSpace;
            if (mainCamera && defaultFov > 0) mainCamera.fieldOfView = defaultFov;
            ClearMechGlow();
            if (boosters) boosters.SetBoost(-1);
            if (canvas) canvas.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (canvas) Destroy(canvas.gameObject);
            foreach (var b in blockers) if (b) Destroy(b);
        }

        void OnLanguage() { DivaUi.RefreshLanguage(); RefreshSelect(); }

        // ------------------------------------------------------------------ phases
        void Enter(ShowPhase next)
        {
            var previous = Phase;
            Phase = next;
            phaseStart = DivaClock.Time;
            Time.timeScale = 1;
            switch (next)
            {
                case ShowPhase.Intro: EnterIntro(); break;
                case ShowPhase.Select: EnterSelect(); break;
                case ShowPhase.RaceIntro: EnterRaceIntro(); break;
                case ShowPhase.PowerUp: EnterPowerUp(); break;
                case ShowPhase.Countdown: EnterCountdown(); break;
                case ShowPhase.Play: if (previous != ShowPhase.Ult) StartRun(); break;
                case ShowPhase.Ult: EnterUlt(); break;
                case ShowPhase.Finish: EnterFinish(); break;
                case ShowPhase.Highlight: EnterHighlight(); break;
                case ShowPhase.Results: EnterResults(); break;
            }
            ShowRoots();
            Debug.Log("DIVA_SHOW_PHASE " + next);
            PhaseChanged?.Invoke(next);
        }

        void Update()
        {
            DivaClock.Tick();
            ReadInputs();
            if (raceIntro && raceIntro.Playing && raceIntro.Time < DivaIntro.Go && Phase != ShowPhase.RaceIntro)
            {
                followingIntro = true;   // Eva's I key replays her intro: go along with it
                Enter(ShowPhase.RaceIntro);
            }
            UpdateCameraPreview();
            bool play = Phase == ShowPhase.Play;
            demo.inputLocked = !play;
            // 选角色和过场时由我们拿镜头；Eva 的开场和游戏中交回去（DivaDemo 平滑跟随）
            demo.cameraHeld = Phase != ShowPhase.Play && Phase != ShowPhase.Countdown;
            if (laser) laser.spaceToFire = play && previousSpace;
            if (sound != null) sound.volume = volume;
            switch (Phase)
            {
                case ShowPhase.Intro: UpdateIntro(); break;
                case ShowPhase.Select: UpdateSelect(); break;
                case ShowPhase.RaceIntro: UpdateRaceIntro(); break;
                case ShowPhase.PowerUp: if (PhaseTime > 2.7f || skipEdge) Enter(ShowPhase.Countdown); break;
                case ShowPhase.Countdown: UpdateCountdown(); break;
                case ShowPhase.Play: UpdatePlay(); break;
                case ShowPhase.Ult: UpdateUlt(); break;
                case ShowPhase.Finish: UpdateFinish(); break;
                case ShowPhase.Highlight: UpdateHighlight(); break;
                case ShowPhase.Results: UpdateResults(); break;
            }
            UpdateHud();
            UpdateBanners();
        }

        float shake;

        void LateUpdate()
        {
            cameraSet = false;
            shake = Mathf.MoveTowards(shake, 0, DivaClock.DeltaTime * 1.5f);
            switch (Phase)
            {
                case ShowPhase.Select: CameraSelect(); break;
                case ShowPhase.PowerUp: CameraPowerUp(); break;
                case ShowPhase.Ult: CameraUlt(); break;
                case ShowPhase.Finish: PoseFinish(); break;
                case ShowPhase.Highlight: PoseHighlight(); break;
                case ShowPhase.Results: CameraResults(); break;
            }
            if (mainCamera)
            {
                if (cameraSet)
                {
                    mainCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                    mainCamera.fieldOfView = cameraFov;
                }
                else if (Phase == ShowPhase.Play || Phase == ShowPhase.Countdown)
                    mainCamera.fieldOfView = Mathf.Lerp(mainCamera.fieldOfView, BaseFov + fovKick, 1 - Mathf.Exp(-DivaClock.DeltaTime * 6));
            }
            UpdatePip();
            UpdateLook();
        }

        void SetCamera(Vector3 position, Vector3 lookAt, float fov)
        {
            cameraSet = true;
            position = Unblocked(ElephantCenter, position);
            position += shake * (Mathf.PerlinNoise(DivaClock.Time * 25, 0) - .5f) * Vector3.up + shake * (Mathf.PerlinNoise(0, DivaClock.Time * 25) - .5f) * Vector3.right;
            cameraPosition = position;
            var dir = lookAt - position;
            cameraRotation = dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir, Vector3.up) : cameraRotation;
            cameraFov = fov;
        }

        void SetCamera(Vector3 position, Quaternion rotation, float fov) { cameraSet = true; cameraPosition = position; cameraRotation = rotation; cameraFov = fov; }

        Vector3 ElephantCenter => elephant.TransformPoint(centerOffset);
        float ElephantSize => elephantRadius * 2;

        // 挡镜头的检测：很多道具没有碰撞体，开局时给它们各加一个看不见的网格碰撞体（Ignore Raycast 层，
        // 只有这里的镜头检测会用到，不影响游戏），这样按真实形状判断，而不是按包围盒
        const int BlockerLayer = 2;
        readonly List<GameObject> blockers = new List<GameObject>();
        void CollectOccluders()
        {
            foreach (var mf in FindObjectsByType<MeshFilter>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (!mf || !mf.sharedMesh) continue;
                var r = mf.GetComponent<MeshRenderer>();
                if (!r || !r.enabled || r.gameObject.layer == 5) continue;
                if (mf.transform.IsChildOf(elephant) || mf.transform.IsChildOf(transform)) continue;
                if (r.bounds.max.y < startPosition.y + .4f) continue;          // 贴地的东西不挡
                var own = mf.GetComponent<Collider>();
                if (own && own.enabled && !own.isTrigger) continue;               // 已经有实心碰撞体（触发器不算，检测时会被忽略）
                if (!mf.sharedMesh.isReadable && !Application.isEditor) continue;
                var go = new GameObject("Diva Camera Blocker") { layer = BlockerLayer, hideFlags = HideFlags.DontSave };
                go.transform.SetParent(mf.transform, false);
                go.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                blockers.Add(go);
            }
            Debug.Log("DIVA_SHOW camera blockers " + blockers.Count);
        }

        static readonly RaycastHit[] hits = new RaycastHit[32];

        /// <summary>从 target 看向 desired 的路上最近的遮挡距离（没有就是全长）。</summary>
        /// <param name="ignoreNear">Hits closer than this to the target are ignored (default: inside the elephant's body).</param>
        float ClearDistance(Vector3 target, Vector3 desired, float ignoreNear = -1)
        {
            var dir = desired - target;
            float dist = dir.magnitude;
            if (dist < .01f) return dist;
            // 只忽略大象身体里面的东西（大象自己的碰撞体本来就排除了）；起点台上的棒棒糖这种贴着大象的道具也要算
            if (ignoreNear < 0) ignoreNear = elephantRadius * .25f;
            float nearest = dist;
            int n = Physics.SphereCastNonAlloc(target, .25f, dir / dist, hits, dist, ~(1 << 5), QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.distance <= ignoreNear || h.transform.IsChildOf(elephant)) continue;
                nearest = Mathf.Min(nearest, h.distance - .3f);
            }
            return nearest;
        }

        /// <summary>
        /// 镜头和大象之间有东西挡住时：先绕着大象左右转一点（25°、50°、80°）找不被挡的位置，转过去是平滑的；
        /// 都不行再升高，最后才拉近。
        /// </summary>
        float dodgeTarget, dodgeApplied;
        ShowPhase dodgePhase;
        Vector3 Unblocked(Vector3 target, Vector3 desired)
        {
            if (dodgePhase != Phase) { dodgePhase = Phase; dodgeTarget = dodgeApplied = 0; }
            var offset = desired - target;
            float dist = offset.magnitude;
            if (dist < .01f) return desired;
            // 从身体中心和腿的高度各看一次：矮的道具（起点台上的棒棒糖）挡的是腿，不在中心那条线上
            var low = target + Vector3.down * centerOffset.y * .55f;
            bool Clear(Vector3 p) => ClearDistance(target, p) >= Vector3.Distance(target, p) - .01f &&
                                     ClearDistance(low, p) >= Vector3.Distance(low, p) - .01f;
            Vector3 At(float angle) => target + Quaternion.AngleAxis(angle, Vector3.up) * offset;
            // 当前用的角度还没被挡就继续用（避免来回跳），否则按顺序找
            if (!Clear(At(dodgeTarget)))
            {
                float found = float.NaN;
                foreach (float a in new[] { 0f, 25f, -25f, 50f, -50f, 80f, -80f })
                    if (Clear(At(a))) { found = a; break; }
                if (!float.IsNaN(found)) dodgeTarget = found;
                else
                {
                    foreach (float up in new[] { 1.5f, 3f })
                    {
                        var raised = desired + Vector3.up * up;
                        if (Clear(raised)) return raised;
                    }
                    return target + offset / dist * Mathf.Max(elephantRadius * .9f, ClearDistance(target, desired));
                }
            }
            else if (dodgeTarget != 0 && Clear(desired)) dodgeTarget = 0;   // 挡住的东西过去了，回到原来的角度
            // 过场刚开始时直接用避开后的角度，之后才平滑转动
            dodgeApplied = PhaseTime < .1f ? dodgeTarget : Mathf.MoveTowards(dodgeApplied, dodgeTarget, 140 * DivaClock.DeltaTime);
            return At(dodgeApplied);
        }

        /// <summary>
        /// 在大象周围找一段最空的方向（相对大象正前方的角度），镜头在这段弧里转，不会被路边道具挡住。
        /// </summary>
        float BestAngle(float distance, float height, float arc)
        {
            var center = ElephantCenter;
            const int n = 36;
            var clear = new float[n];
            for (int i = 0; i < n; i++)
            {
                var dir = Quaternion.AngleAxis(i * 360f / n, Vector3.up) * Forward;
                var cam = center + dir * distance + Vector3.up * height;
                float score = 0;
                var low = center + Vector3.down * centerOffset.y * .55f;
                foreach (var p in new[] { center, low, center + Forward * elephantRadius * .6f, center - Forward * elephantRadius * .6f })
                    score += ClearDistance(p, cam) >= Vector3.Distance(p, cam) - .05f ? 1 : 0;
                clear[i] = score;
            }
            int half = Mathf.Max(1, Mathf.RoundToInt(arc / 2 / (360f / n)));
            float best = -1; int bestI = 0;
            for (int i = 0; i < n; i++)
            {
                float sum = 0;
                for (int k = -half; k <= half; k++) sum += clear[(i + k + n) % n];
                // 稍微偏好斜前方（看得到脸和机甲）
                float angle = Mathf.DeltaAngle(0, i * 360f / n);
                sum += Mathf.Cos(angle * Mathf.Deg2Rad) * .5f + (Mathf.Abs(angle) > 20 && Mathf.Abs(angle) < 70 ? .6f : 0);
                if (sum > best) { best = sum; bestI = i; }
            }
            return bestI * 360f / n;
        }
        Vector3 Forward => Vector3.ProjectOnPlane(elephant.forward, Vector3.up).normalized;

        /// <summary>游戏里 DivaDemo 的第三人称镜头位置（倒数前把镜头放到这里，交接时不跳）。</summary>
        void FollowPose(out Vector3 position, out Vector3 lookAt)
        {
            position = elephant.position + elephant.rotation * demo.cameraOffset;
            lookAt = elephant.position + Vector3.up * 1.5f;
        }

        // ------------------------------------------------------------------ input
        void ReadInputs()
        {
            var kb = Keyboard.current;
            confirmEdge = kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame);
            skipEdge = confirmEdge || kb != null && kb.escapeKey.wasPressedThisFrame;
            bool held = demo.State.Ult;
            ultEdge = false;
            if (!held) ultArmed = true;
            else if (ultArmed) { ultArmed = false; ultEdge = true; }
            if (kb != null)
            {
                if (kb.lKey.wasPressedThisFrame) DivaText.Toggle();
                if (kb.f1Key.wasPressedThisFrame) ToggleSetup();
            }
        }

        public void ToggleSetup()
        {
            if (!controller) return;
            controller.showControls = !controller.showControls;
            if (game) game.showHud = controller.showControls && previousHud;
        }

        /// <summary>举左/右手切换：手放下后才能再切一次。</summary>
        int AimStep()
        {
            float aim = demo.State.MenuAim;
            int step = 0;
            if (Mathf.Abs(aim) > .5f && Mathf.Abs(aimHeld) < .5f) step = aim < 0 ? -1 : 1;
            aimHeld = aim;
            return step;
        }

        // ------------------------------------------------------------------ Eva's race intro + our entrance sting
        bool followingIntro, stingPlayed;
        // 出场音效的重击（1.24 秒处）对准开场俯冲落到起跑线、开始绕大象的那一刻
        const float StingBoom = 1.24f;

        void EnterRaceIntro()
        {
            if (selectRing) selectRing.gameObject.SetActive(false);
            if (!followingIntro) raceIntro.Begin();
            followingIntro = false;
            stingPlayed = raceIntro.Time > DivaIntro.SwoopEnd - StingBoom;
        }

        void UpdateRaceIntro()
        {
            if (!raceIntro) { Enter(ShowPhase.Play); return; }
            if (!stingPlayed && raceIntro.Time >= DivaIntro.SwoopEnd - StingBoom)
            {
                stingPlayed = true;
                if (entranceMusic) sound.PlayMusic(entranceMusic, entranceVolume);
                Debug.Log($"DIVA_SHOW_STING at intro {raceIntro.Time:F2}s (boom at {raceIntro.Time + StingBoom:F2}s, orbit starts {DivaIntro.SwoopEnd}s)");
            }
            if ((skipEdge || ultEdge) && PhaseTime > .5f) raceIntro.Skip();
            if (raceIntro.Time >= DivaIntro.Go || !raceIntro.Playing) Enter(ShowPhase.Play);
        }

        // ------------------------------------------------------------------ camera preview
        // 原来的摄像头预览在调试面板收起时会变成右上角的浮动窗口，盖住界面；整局流程里改成画在我们自己的界面里
        void UpdateCameraPreview()
        {
            if (!cameraPreview && controller) cameraPreview = controller.GetComponent<DigiPhantCameraPreview>();
            if (!cameraPreview) return;
            bool panel = controller && controller.showControls;
            bool inIntro = raceIntro && raceIntro.Playing;
            if (!inIntro) cameraPreview.showPreview = panel;   // 面板打开时照常画在面板里；Eva 的开场自己管
            ShowCameraFeed(!panel && !inIntro && (Phase == ShowPhase.Select || Phase == ShowPhase.Play));
        }

        // ------------------------------------------------------------------ intro (Timeline, optional)
        void EnterIntro()
        {
            if (introDirector && introDirector.playableAsset)
            {
                introDirector.time = 0;
                introDirector.Play();
            }
        }

        void UpdateIntro()
        {
            bool playing = introDirector && introDirector.playableAsset && introDirector.state == PlayState.Playing && PhaseTime < 120;
            if (playing && !skipEdge && !ultEdge) return;
            if (introDirector && introDirector.state == PlayState.Playing) introDirector.Stop();
            Enter(ShowPhase.Select);
        }

        /// <summary>Eva 的开场可以在结束时调用它（不用 PlayableDirector 时）。</summary>
        public void IntroFinished() { if (Phase == ShowPhase.Intro) Enter(ShowPhase.Select); }

        // ------------------------------------------------------------------ elephant helpers
        void ResetElephant()
        {
            locomotion.StopMotion();
            elephant.SetPositionAndRotation(startPosition, startRotation);
            demo.State.FillTank();
        }

        // 机甲启动：所有发光部分从暗到亮（用 MaterialPropertyBlock，不改材质资源）
        MaterialPropertyBlock glowBlock;
        Renderer[] mechRenderers;
        void SetMechGlow(float k)
        {
            if (mechRenderers == null)
            {
                var toggle = skins ? skins.toggle : FindAnyObjectByType<DivaMechToggle>();
                mechRenderers = toggle ? toggle.parts.Where(p => p).SelectMany(p => p.GetComponentsInChildren<MeshRenderer>(true)).ToArray() : new Renderer[0];
            }
            glowBlock ??= new MaterialPropertyBlock();
            glowBlock.SetColor("_EmissionColor", Color.white * 2f * k);
            foreach (var r in mechRenderers) if (r) r.SetPropertyBlock(glowBlock);
        }

        void ClearMechGlow()
        {
            if (mechRenderers == null) return;
            foreach (var r in mechRenderers) if (r) r.SetPropertyBlock(null);
        }

        // 过场用电影感（景深对焦大象、胶片颗粒），游戏中保持清楚
        void UpdateLook()
        {
            if (!look) return;
            bool introOrbit = Phase == ShowPhase.RaceIntro && raceIntro && raceIntro.Time > DivaIntro.SwoopEnd && raceIntro.Time < DivaIntro.OrbitEnd;
            bool cut = Phase == ShowPhase.Select || Phase == ShowPhase.PowerUp || Phase == ShowPhase.Ult || Phase == ShowPhase.Finish ||
                       Phase == ShowPhase.Highlight || Phase == ShowPhase.Results || introOrbit;
            look.cinematic = cut ? 1 : 0;
            if (mainCamera) look.focusDistance = Vector3.Distance(mainCamera.transform.position, ElephantCenter);
        }

        Color Accent => skins && skins.CurrentSkin != null ? skins.CurrentSkin.accent : new Color(.27f, .94f, .78f);
        Color Primary => skins && skins.CurrentSkin != null ? skins.CurrentSkin.primary : new Color(.93f, .49f, .75f);
        Color Secondary => skins && skins.CurrentSkin != null ? skins.CurrentSkin.secondary : new Color(.96f, .92f, .87f);
        string SkinName => skins ? skins.DisplayName(skins.Current, DivaText.Chinese) : "";
    }
}
