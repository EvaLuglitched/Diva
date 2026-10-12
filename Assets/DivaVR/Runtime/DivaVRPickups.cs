using System.Collections.Generic;
using System.Linq;
using DigiPhant;
using Diva.Show;
using UnityEngine;

namespace Diva.VR
{
    /// <summary>
    /// 赛道上的糖果：在起点和各任务点之间的直线上每隔几米放一颗（左右交替偏一点），大象碰到就吃掉：+15 分、充一点能量、
    /// 一声"叮"、一小团闪光。每局倒数时全部放回来。
    /// </summary>
    public class DivaVRPickups : MonoBehaviour
    {
        public int points = 15;
        public int Taken { get; private set; }
        public float spacing = 3f, height = 1.7f, reach = 2.8f;

        class Candy { public Transform t; public Vector3 at; public bool taken, rocket; public float phase; public Material mat; }
        DivaVRDash dash;
        readonly List<Candy> candies = new List<Candy>();
        DivaShowDirector show;
        DigiPhantLocomotion loco;
        DivaGameManager game;
        DivaShowFx fx;
        Transform root;
        Mesh ringMesh;

        void Start()
        {
            show = FindAnyObjectByType<DivaShowDirector>();
            loco = FindAnyObjectByType<DigiPhantLocomotion>();
            game = FindAnyObjectByType<DivaGameManager>();
            if (!show || !loco || !loco.travelRoot || game == null || !show.glowMaterial) { enabled = false; return; }
            dash = GetComponent<DivaVRDash>();
            root = new GameObject("Diva Candies").transform;
            fx = new DivaShowFx(root, show.glowMaterial, show.bubbleMaterial);
            Spawn();
            DivaShowDirector.PhaseChanged += OnPhase;
        }

        void OnDestroy() => DivaShowDirector.PhaseChanged -= OnPhase;

        void OnPhase(ShowPhase p)
        {
            if (p != ShowPhase.Countdown) return;
            foreach (var c in candies) { c.taken = false; c.t.gameObject.SetActive(true); }
            Taken = 0;
        }

        void Spawn()
        {
            var points = new List<Vector3> { loco.travelRoot.position };
            points.AddRange(game.tasks.Where(t => t != null && t.landmark).Select(t => t.landmark.position));
            var colours = new[] { new Color(1f, .45f, .8f), new Color(1f, .85f, .3f), new Color(.5f, .95f, .85f), new Color(.75f, .6f, 1f) };
            int n = 0;
            for (int i = 0; i + 1 < points.Count; i++)
            {
                var a = points[i]; var b = points[i + 1];
                var flat = Vector3.ProjectOnPlane(b - a, Vector3.up);
                float len = flat.magnitude; if (len < spacing * 2) continue;
                var dir = flat / len; var side = Vector3.Cross(Vector3.up, dir);
                // 离两头各留 6 米（任务点自己有东西），中间每隔 spacing 一颗，左右交替
                for (float d = 6; d <= len - 6; d += spacing)
                {
                    var pos = a + dir * d + side * Mathf.Sin(d * .9f) * 1.4f;
                    pos.y = a.y + height;
                    candies.Add(Make(pos, colours[n++ % colours.Length]));
                }
                // 每段直道三分之一、三分之二处各一枚小火箭：吃到就冲刺
                if (len > 18) foreach (float k in new[] { .33f, .67f }) { var rp = a + dir * (len * k) + side * (k < .5f ? -1.8f : 1.8f); rp.y = a.y + height + .2f; candies.Add(MakeRocket(rp)); }
            }
            Debug.Log($"DIVA_VR candies: {candies.Count} on the course");
        }

        Candy Make(Vector3 at, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.name = "Candy";
            go.transform.SetParent(root, false);
            go.transform.position = at;
            go.transform.localScale = Vector3.one * .42f;
            var mat = new Material(show.glowMaterial);
            Tint(mat, colour * 1.6f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            // 一圈糖纸：扁扁的圆环
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var rc = ring.GetComponent<Collider>(); if (rc) Destroy(rc);
            ring.name = "Wrap";
            ring.transform.SetParent(go.transform, false);
            ring.transform.localScale = new Vector3(1.9f, .05f, 1.9f);
            var rm = ring.GetComponent<MeshRenderer>();
            var ringMat = new Material(show.glowMaterial); Tint(ringMat, Color.Lerp(colour, Color.white, .5f) * 1.2f);
            rm.sharedMaterial = ringMat; rm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rm.receiveShadows = false;
            return new Candy { t = go.transform, at = at, phase = Random.value * 6.28f, mat = mat };
        }

        Candy MakeRocket(Vector3 at)
        {
            var go = new GameObject("Rocket");
            go.transform.SetParent(root, false);
            go.transform.position = at;
            var body = new Material(show.glowMaterial); Tint(body, new Color(1f, .5f, .8f) * 1.4f);
            var white = new Material(show.glowMaterial); Tint(white, new Color(1f, .97f, 1f) * 1.3f);
            var teal = new Material(show.glowMaterial); Tint(teal, new Color(.4f, .95f, .9f) * 1.4f);
            Prim(PrimitiveType.Capsule, go.transform, "Body", Vector3.zero, Quaternion.Euler(90, 0, 0), new Vector3(.22f, .3f, .22f), white);
            Prim(PrimitiveType.Sphere, go.transform, "Nose", new Vector3(0, 0, .3f), Quaternion.identity, new Vector3(.2f, .2f, .3f), body);
            for (int i = 0; i < 3; i++)
                Prim(PrimitiveType.Cube, go.transform, "Fin " + i, Quaternion.Euler(0, 0, i * 120) * new Vector3(0, .15f, -.2f), Quaternion.Euler(0, 0, i * 120), new Vector3(.03f, .18f, .14f), teal);
            Prim(PrimitiveType.Sphere, go.transform, "Flame", new Vector3(0, 0, -.36f), Quaternion.identity, new Vector3(.14f, .14f, .22f), body);
            return new Candy { t = go.transform, at = at, phase = Random.value * 6.28f, mat = body, rocket = true };
        }

        static void Prim(PrimitiveType type, Transform parent, string name, Vector3 pos, Quaternion rot, Vector3 scale, Material m)
        {
            var go = GameObject.CreatePrimitive(type);
            var c = go.GetComponent<Collider>(); if (c) Destroy(c);
            go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = pos; go.transform.localRotation = rot; go.transform.localScale = scale;
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        }

        static void Tint(Material m, Color c) { m.color = c; if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c); if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c); }

        void Update()
        {
            float t = Time.time;
            var e = loco.travelRoot.position;
            bool play = show.Phase == ShowPhase.Play;
            foreach (var c in candies)
            {
                if (c.taken) continue;
                c.t.position = c.at + Vector3.up * (.12f * Mathf.Sin(t * 2.2f + c.phase));
                c.t.rotation = c.rocket ? Quaternion.Euler(-20, t * 120 + c.phase * 30, 0) : Quaternion.Euler(0, t * 90 + c.phase * 30, 20 * Mathf.Sin(t * 1.5f + c.phase));
                if (!play) continue;
                var d = c.at - e; d.y = 0;
                if (d.magnitude < reach)
                {
                    c.taken = true; Taken++;
                    c.t.gameObject.SetActive(false);
                    fx.Flash(c.t.position, c.mat.color, 1.6f, .3f);
                    fx.Pop(c.t.position, c.mat.color, .5f);
                    if (c.rocket) { show.Collect(points, c.t.position); if (dash) dash.Trigger("rocket"); DivaVRFloatText.Show(DivaText.T("vr.feed.rocket"), c.t.position + Vector3.up * .5f, new Color(1f, .6f, .9f), 1.3f); }
                    else { show.Collect(points, c.t.position); DivaVRFloatText.Show("+" + points, c.t.position + Vector3.up * .4f, new Color(1f, .85f, .3f), .9f); }
                }
            }
        }
    }
}
