// Just enough of UnityEngine for DivaShowPerf.cs to compile and run outside Unity.
using System;
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object o) => !ReferenceEquals(o, null); }
    public class MonoBehaviour : Object { }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static float Abs(float a) => Math.Abs(a);
        public static float Exp(float a) => (float)Math.Exp(a);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
    }
    public static class Debug { public static Action<string> Sink = Console.WriteLine; public static void Log(object m) => Sink(m.ToString()); }
    public static class QualitySettings { public static int vSyncCount = 1; }
    public static class Application { public static int targetFrameRate = -1; }
    public struct RefreshRate { public uint numerator, denominator; public double value => (double)numerator / denominator; }
    public struct Resolution { public RefreshRate refreshRateRatio; }
    public static class Screen { public static Resolution currentResolution = new Resolution { refreshRateRatio = new RefreshRate { numerator = 120, denominator = 1 } }; public static int width = 3456, height = 2168; }
    public static class Time { public static float unscaledDeltaTime, unscaledTime; }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; } public static Color white => new Color(1, 1, 1); }
    public enum FontStyle { Normal, Bold }
    public class GUIStyleState { public Color textColor; }
    public class GUIStyle { public GUIStyle() { } public GUIStyle(GUIStyle o) { } public int fontSize; public FontStyle fontStyle; public GUIStyleState normal { get; } = new GUIStyleState(); public Vector2 CalcSize(GUIContent c) => default; }
    public struct Vector2 { public float x, y; }
    public class GUIContent { public GUIContent(string s) { } }
    public struct Rect { public float x, y, width, height; public Rect(float x, float y, float w, float h) { this.x = x; this.y = y; width = w; height = h; } }
    public class GUISkin { public GUIStyle label = new GUIStyle(); }
    public class Texture2D { public static Texture2D whiteTexture => null; }
    public static class GUI { public static GUISkin skin = new GUISkin(); public static Color color; public static void DrawTexture(Rect r, Texture2D t) { } public static void Label(Rect r, string s, GUIStyle st) { } }
}
namespace UnityEngine.Rendering
{
    public class RenderPipelineAsset : UnityEngine.Object { }
    public static class GraphicsSettings { public static RenderPipelineAsset currentRenderPipeline; }
}
namespace UnityEngine.Rendering.Universal { public class UniversalRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset { public float renderScale = 1; } }
namespace UnityEngine.InputSystem
{
    public class KeyControl { public bool wasPressedThisFrame => false; }
    public class Keyboard { public static Keyboard current => null; public KeyControl f2Key = new KeyControl(); }
}
