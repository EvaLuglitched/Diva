using System.Collections.Generic;
using UnityEngine;

namespace Diva
{
    /// <summary>
    /// A floating, bobbing, glowing arrow above each course task that is not done yet, shown while the elephant is
    /// near it (feeding bush, water basin, fallen log, finish), so players notice where the task is. Built at runtime
    /// from simple meshes; nothing is saved in the scene.
    /// </summary>
    public class DivaTaskMarkers : MonoBehaviour
    {
        public DivaGameManager game;
        [Tooltip("Show a task's arrow when the elephant is within this distance (metres) of it.")]
        public float showDistance = 16;
        [Tooltip("Hide it again beyond this distance (a little larger, so it does not flicker at the edge).")]
        public float hideDistance = 19;
        public float arrowSize = 1.2f;
        public Color[] colours = { new Color(1, .42f, .78f), new Color(.35f, .9f, 1), new Color(1, .82f, .3f), new Color(.6f, 1, .7f) };

        class Marker
        {
            public CourseTask task;
            public Transform root, spin;
            public float height, shown;
            public bool wanted;
        }
        readonly List<Marker> markers = new List<Marker>();
        readonly List<Material> materials = new List<Material>();

        void Start() => BuildMarkers();

        void BuildMarkers()
        {
            if (markers.Count > 0) return;
            if (!game) game = FindAnyObjectByType<DivaGameManager>();
            if (!game) return;
            for (int i = 0; i < game.tasks.Length; i++)
            {
                var task = game.tasks[i];
                if (!task.landmark) continue;
                markers.Add(Build(task, colours[i % colours.Length], i));
            }
        }

        /// <summary>Editor previews: show every task's arrow at full size (call ClearPreview afterwards).</summary>
        public void ShowAllForPreview()
        {
            BuildMarkers();
            foreach (var m in markers)
            {
                m.root.gameObject.SetActive(true);
                m.root.position = m.task.landmark.position + Vector3.up * m.height;
                m.root.localScale = Vector3.one * arrowSize;
            }
        }

        public void ClearPreview()
        {
            foreach (var m in markers) if (m.root) DestroyImmediate(m.root.gameObject);
            foreach (var m in materials) if (m) DestroyImmediate(m);
            markers.Clear(); materials.Clear();
        }

        void OnDestroy()
        {
            foreach (var m in markers) if (m.root) Destroy(m.root.gameObject);
            foreach (var m in materials) if (m) Destroy(m);
        }

        Marker Build(CourseTask task, Color colour, int index)
        {
            var root = new GameObject("Task arrow · " + task.label).transform;
            root.SetParent(transform, false);
            var spin = new GameObject("Spin").transform;
            spin.SetParent(root, false);
            var material = Glow(colour);
            // Arrow pointing down at the task: a cone head with its tip at the bottom, and a shaft above it.
            var head = new GameObject("Head", typeof(MeshFilter), typeof(MeshRenderer));
            head.transform.SetParent(spin, false);
            head.GetComponent<MeshFilter>().sharedMesh = Cone(.55f, .75f, 20);
            head.GetComponent<MeshRenderer>().sharedMaterial = material;
            var shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(shaft.GetComponent<Collider>());
            shaft.name = "Shaft";
            shaft.transform.SetParent(spin, false);
            shaft.transform.localPosition = new Vector3(0, 1.1f, 0);
            shaft.transform.localScale = new Vector3(.32f, .38f, .32f);
            shaft.GetComponent<MeshRenderer>().sharedMaterial = material;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            root.gameObject.SetActive(false);
            return new Marker { task = task, root = root, spin = spin, height = TopOf(task.landmark) + .6f, shown = 0 };
        }

        /// <summary>Height above the landmark's base where the arrow tip floats: just over its visible top.</summary>
        static float TopOf(Transform landmark)
        {
            float top = float.NegativeInfinity;
            foreach (var r in landmark.GetComponentsInChildren<Renderer>())
                if (r.enabled) top = Mathf.Max(top, r.bounds.max.y);
            return float.IsInfinity(top) ? 2.5f : Mathf.Clamp(top - landmark.position.y, .6f, 6);
        }

        void Update()
        {
            if (!game) return;
            var elephant = game.Elephant;
            float dt = Time.deltaTime;
            foreach (var m in markers)
            {
                if (!m.root) continue;
                float distance = elephant ? Flat(elephant.position - m.task.landmark.position).magnitude : float.PositiveInfinity;
                bool active = game.Running && !m.task.Done;
                m.wanted = active && (distance < showDistance || m.wanted && distance < hideDistance);
                m.shown = Mathf.MoveTowards(m.shown, m.wanted ? 1 : 0, dt * 4);
                bool visible = m.shown > .01f;
                if (m.root.gameObject.activeSelf != visible) m.root.gameObject.SetActive(visible);
                if (!visible) continue;
                float pop = m.wanted ? EaseOutBack(m.shown) : m.shown;
                float bob = Mathf.Sin(Time.time * 3.2f) * .25f;
                m.root.position = m.task.landmark.position + Vector3.up * (m.height + bob);
                m.root.localScale = Vector3.one * arrowSize * pop;
                m.spin.Rotate(Vector3.up, 90 * dt, Space.Self);
            }
        }

        static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
        static float EaseOutBack(float t) { t = Mathf.Clamp01(t); const float c = 1.70158f; return 1 + (c + 1) * Mathf.Pow(t - 1, 3) + c * Mathf.Pow(t - 1, 2); }

        Material Glow(Color colour)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var m = new Material(shader ? shader : Shader.Find("Standard")) { name = "Diva Task Arrow" };
            m.SetColor("_BaseColor", colour); m.color = colour;
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", colour * 1.2f);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            materials.Add(m);
            return m;
        }

        /// <summary>A cone with its tip at the origin pointing down and its base `height` above.</summary>
        static Mesh Cone(float radius, float height, int segments)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a0 = i * Mathf.PI * 2 / segments, a1 = (i + 1) * Mathf.PI * 2 / segments;
                Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, height, Mathf.Sin(a0) * radius);
                Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, height, Mathf.Sin(a1) * radius);
                int v = vertices.Count;
                // Unity front faces: normal = cross(b - a, c - a). Side faces outward (and down), the cap faces up.
                vertices.Add(Vector3.zero); vertices.Add(p0); vertices.Add(p1);                 // side
                triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
                v = vertices.Count;
                vertices.Add(new Vector3(0, height, 0)); vertices.Add(p1); vertices.Add(p0);   // base cap (faces up)
                triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 2);
            }
            var mesh = new Mesh { name = "Task arrow head" };
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
