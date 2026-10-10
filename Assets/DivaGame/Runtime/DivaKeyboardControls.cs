using DigiPhant;
using Diva.Show;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Diva
{
    /// <summary>
    /// Keyboard play alongside the gestures: the keys act as one more player merged into DivaDemo's control state
    /// (DivaControlState.Keys), so movement, spraying, drinking, the HUD, skills and course tasks all respond the same way.
    /// Works with or without a camera. A small key legend sits at the bottom right during play (H hides it);
    /// Diva Show's camera feed is at the bottom left and its ultimate ring at the bottom centre.
    /// </summary>
    [DefaultExecutionOrder(-50)]   // before DigiPhantController.LateUpdate reads the state
    public class DivaKeyboardControls : MonoBehaviour
    {
        public bool showLegend = true;
        DivaDemo demo;
        DivaLaserBlaster laser;
        DivaIntro intro;
        DivaShowDirector show;
        bool laserSpaceWas = true;
        readonly DivaGestureFrame frame = new DivaGestureFrame { confidence = 1 };
        Canvas canvas;
        TextMeshProUGUI legend;
        bool chinese;

        void OnEnable()
        {
            demo = FindAnyObjectByType<DivaDemo>();
            laser = FindAnyObjectByType<DivaLaserBlaster>();
            intro = FindAnyObjectByType<DivaIntro>();
            show = FindAnyObjectByType<DivaShowDirector>();
            // Space now sprays through the shared state (water jet + laser); stop the blaster's own Space shot doubling it.
            if (laser) { laserSpaceWas = laser.spaceToFire; laser.spaceToFire = false; }
        }

        void OnDisable()
        {
            if (demo != null) demo.State.Keys = null;
            if (laser) laser.spaceToFire = laserSpaceWas;
            if (canvas) Destroy(canvas.gameObject);
        }

        void Update()
        {
            if (!demo) return;
            var kb = Keyboard.current;
            if (kb == null) { demo.State.Keys = null; return; }
            float go = kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1 : 0;
            float steer = (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1 : 0) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1 : 0);
            float aim = (kb.iKey.isPressed ? 1 : 0) - (kb.uKey.isPressed ? 1 : 0);   // U left, I right
            bool shoot = kb.spaceKey.isPressed;
            bool drink = kb.fKey.isPressed || kb.rKey.isPressed;
            bool any = go > 0 || steer != 0 || aim != 0 || shoot || drink;
            if (any)
            {
                frame.go = go; frame.steer = steer; frame.aim = aim; frame.shoot = shoot; frame.drink = drink;
                demo.State.Keys = frame;
            }
            else demo.State.Keys = null;   // gestures alone (and the camera's ready check) as before
            if (kb.hKey.wasPressedThisFrame) showLegend = !showLegend;
            UpdateLegend();
        }

        // ---------- Legend (uGUI + TextMesh Pro, so it also draws on Vision Pro) ----------

        bool InPlay()
        {
            if (intro && intro.Playing) return false;
            if (show && show.isActiveAndEnabled) return show.Phase == ShowPhase.Play;
            return demo && !demo.inputLocked;
        }

        void UpdateLegend()
        {
            bool visible = showLegend && InPlay();
            if (!canvas)
            {
                if (!visible) return;
                BuildLegend();
            }
            canvas.gameObject.SetActive(visible);
            bool zh = DivaText.Chinese && DivaUi.Font;   // Chinese needs Diva Show's font with its CJK fallback
            if (zh != chinese || string.IsNullOrEmpty(legend.text))
            {
                chinese = zh;
                // Plain letters only: the default fonts may lack arrow glyphs.
                legend.text = zh
                    ? "<b>键盘</b>  W 前进 · A/D 转向 · U/I 瞄准（方向键也可以）\n空格 喷水 · F 喝水 · 大招充满时 U 也会放大招 · H 隐藏"
                    : "<b>KEYS</b>  W go · A/D turn · U/I aim (arrow keys too)\nSpace spray · F drink · U also fires a charged ultimate · H hide";
            }
        }

        void BuildLegend()
        {
            var go = new GameObject("Diva Keyboard Legend", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;   // under Diva Show's UI (50) and the intro (60)
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.layer = 5;
            var rt = (RectTransform)panel.transform;
            rt.SetParent(go.transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1, 0);
            rt.anchoredPosition = new Vector2(-28, 24);
            var image = panel.GetComponent<Image>();
            image.color = new Color(.12f, .05f, .2f, .55f);
            image.raycastTarget = false;
            var fitter = panel.AddComponent<HorizontalLayoutGroup>();
            fitter.padding = new RectOffset(18, 18, 8, 8);
            fitter.childControlWidth = fitter.childControlHeight = true;
            var size = panel.AddComponent<ContentSizeFitter>();
            size.horizontalFit = size.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var text = new GameObject("Keys", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            text.layer = 5;
            text.transform.SetParent(panel.transform, false);
            legend = text.GetComponent<TextMeshProUGUI>();
            var font = DivaUi.Font ? DivaUi.Font : TMP_Settings.defaultFontAsset;
            if (font) legend.font = font;
            legend.fontSize = 24;
            legend.alignment = TextAlignmentOptions.Right;
            legend.lineSpacing = 8;
            legend.color = new Color(1, .95f, 1, .95f);
            legend.textWrappingMode = TextWrappingModes.NoWrap;
            legend.richText = true;
            legend.raycastTarget = false;
        }
    }
}
