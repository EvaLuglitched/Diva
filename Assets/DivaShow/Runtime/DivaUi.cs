using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace Diva.Show
{
    /// <summary>界面小工具：字体、文字、面板、按钮。所有界面都在运行时生成，场景里只放一个 Diva Show 物体。</summary>
    public static class DivaUi
    {
        public static TMP_FontAsset Font { get; private set; }
        /// <summary>All plain texts share this material: a soft dark outline so they read on the bright candy town.</summary>
        public static Material Shadowed { get; private set; }
        static readonly List<(TMP_Text text, Func<string> source)> localized = new List<(TMP_Text, Func<string>)>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Font = null; Shadowed = null; localized.Clear(); }

        // 拉丁字母用 Saira（项目里自带，OFL），中文用系统字体做后备（不把系统字体放进仓库）
        static readonly (string family, string style)[] CjkFonts =
        {
            ("PingFang SC", "Semibold"), ("PingFang SC", "Medium"), ("PingFang SC", "Regular"),
            ("Hiragino Sans GB", "W6"), ("Hiragino Sans GB", "W3"), ("Heiti SC", "Medium"), ("STHeiti", "Medium"),
            ("Microsoft YaHei", "Bold"), ("Microsoft YaHei", "Regular"), ("Noto Sans CJK SC", "Bold"), ("Noto Sans SC", "Bold"),
            ("Source Han Sans SC", "Bold"),
        };

        public static TMP_FontAsset MakeFont(Font latin, Shader sdf)
        {
            if (Font) return Font;
            TMP_FontAsset asset = null;
            try { if (latin) asset = TMP_FontAsset.CreateFontAsset(latin, 72, 6, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true); }
            catch (Exception e) { Debug.LogWarning("DIVA_SHOW font: " + e.Message); }
            if (!asset) asset = TMP_Settings.defaultFontAsset;
            if (!asset) return null;
            if (sdf && asset.material) asset.material.shader = sdf;
            asset.name = "Diva Show Font";
            TMP_FontAsset cjk = null;
            foreach (var (family, style) in CjkFonts)
            {
                try { cjk = TMP_FontAsset.CreateFontAsset(family, style, 72); } catch (Exception) { cjk = null; }
                if (cjk) break;
            }
            if (cjk)
            {
                if (sdf && cjk.material) cjk.material.shader = sdf;
                asset.fallbackFontAssetTable = new List<TMP_FontAsset> { cjk };
            }
            else Debug.LogWarning("DIVA_SHOW no Chinese system font found; Chinese text may show as boxes.");
            Font = asset;
            if (asset.material)
            {
                Shadowed = new Material(asset.material) { name = "Diva Show Text" };
                Shadowed.EnableKeyword("UNDERLAY_ON");
                Shadowed.SetColor("_UnderlayColor", new Color(.05f, .02f, .12f, .75f));
                Shadowed.SetFloat("_UnderlaySoftness", .5f);
                Shadowed.SetFloat("_UnderlayDilate", .25f);
                Shadowed.SetFloat("_OutlineWidth", .06f);
                Shadowed.SetColor("_OutlineColor", new Color(.05f, .02f, .12f, .6f));
            }
            return asset;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax; rt.pivot = pivot;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size) =>
            Rect(parent, name, anchor, anchor, anchor, pos, size);

        public static RectTransform Stretch(Transform parent, string name)
        {
            var rt = Rect(parent, name, Vector2.zero, Vector2.one, new Vector2(.5f, .5f), Vector2.zero, Vector2.zero);
            return rt;
        }

        public static DivaSlant Panel(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color top, Color bottom, float skew = 18)
        {
            var rt = Rect(parent, name, anchor, pos, size);
            rt.gameObject.AddComponent<CanvasRenderer>();
            var s = rt.gameObject.AddComponent<DivaSlant>();
            s.color = top; s.bottom = bottom; s.skew = skew; s.raycastTarget = false;
            return s;
        }

        public static TextMeshProUGUI Text(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize,
                                           Color color, TextAlignmentOptions align = TextAlignmentOptions.Center, bool italic = true)
        {
            var rt = Rect(parent, name, anchor, pos, size);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (Font) t.font = Font;
            if (Shadowed) t.fontSharedMaterial = Shadowed;
            t.fontSize = fontSize; t.color = color; t.alignment = align;
            t.fontStyle = italic ? FontStyles.Italic : FontStyles.Normal;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false; t.richText = true;
            return t;
        }

        /// <summary>发光字：用 SDF 字体材质的 Underlay 做柔光，Outline 做描边。每个调用生成一个材质实例。</summary>
        public static void Glow(TMP_Text t, Color glow, float softness = .6f, float dilate = .35f, float outline = .12f, Color? outlineColor = null)
        {
            var m = t.fontMaterial;
            m.EnableKeyword("UNDERLAY_ON");
            m.SetColor("_UnderlayColor", glow);
            m.SetFloat("_UnderlaySoftness", softness);
            m.SetFloat("_UnderlayDilate", dilate);
            m.SetFloat("_UnderlayOffsetX", 0); m.SetFloat("_UnderlayOffsetY", 0);
            m.SetFloat("_OutlineWidth", outline);
            m.SetColor("_OutlineColor", outlineColor ?? new Color(0, 0, 0, .7f));
            t.UpdateMeshPadding();
        }

        public static void Localize(TMP_Text t, Func<string> source)
        {
            localized.Add((t, source));
            t.text = source();
        }

        public static void RefreshLanguage()
        {
            for (int i = localized.Count - 1; i >= 0; i--)
            {
                var (t, source) = localized[i];
                if (!t) { localized.RemoveAt(i); continue; }
                t.text = source();
            }
        }

        public static Button Button(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Func<string> label,
                                    Color fill, Color textColor, float fontSize, UnityAction onClick, float skew = 18)
        {
            var panel = Panel(parent, name, anchor, pos, size, fill, fill * new Color(.8f, .8f, .8f, 1), skew);
            panel.raycastTarget = true;
            var b = panel.gameObject.AddComponent<Button>();
            b.targetGraphic = panel;
            var colors = b.colors;
            colors.normalColor = Color.white; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1);
            colors.pressedColor = new Color(.8f, .8f, .8f, 1); colors.colorMultiplier = 1.3f; colors.fadeDuration = .08f;
            b.colors = colors;
            b.onClick.AddListener(onClick);
            var t = Text(panel.transform, "Label", new Vector2(.5f, .5f), Vector2.zero, size, fontSize, textColor);
            Localize(t, label);
            return b;
        }

        public static Image Image(Transform parent, string name, Vector2 anchor, Vector2 pos, Vector2 size, Color color, Sprite sprite = null)
        {
            var rt = Rect(parent, name, anchor, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color; img.sprite = sprite; img.raycastTarget = false;
            return img;
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        public static float EaseOutBack(float t) { t = Mathf.Clamp01(t); float c = 1.70158f; return 1 + (c + 1) * Mathf.Pow(t - 1, 3) + c * Mathf.Pow(t - 1, 2); }
        public static float EaseOut(float t) { t = Mathf.Clamp01(t); return 1 - (1 - t) * (1 - t) * (1 - t); }
        public static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3 - 2 * t); }

        public static string Clock(float seconds)
        {
            seconds = Mathf.Max(0, seconds);
            int m = (int)(seconds / 60);
            return m + ":" + (seconds - m * 60).ToString("00.0", System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>程序生成的小贴图（环形地台、速度线、命中标记）。</summary>
        public static Texture2D MakeTexture(string name, int size, Func<float, float, Color> f)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = name, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    px[y * size + x] = f((x + .5f) / size * 2 - 1, (y + .5f) / size * 2 - 1);
            tex.SetPixels(px); tex.Apply(false, true);
            return tex;
        }
    }
}
