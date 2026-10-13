using System.Collections.Generic;
using System.Linq;
using DigiPhant;
using UnityEngine;

namespace Diva.Show
{
    /// <summary>
    /// 赛道上的糖果和小火箭（PC 和 VR 共用；DivaShowDirector 在运行时挂到自己身上，场景不用改）。
    /// 起点和各任务点之间的直线上每隔几米一颗糖（左右交替偏一点），每段直道三分之一、三分之二处各一枚小火箭。
    /// 大象碰到就吃掉：糖果 +15（燃烧 ×1.5）、火箭 +15 并冲刺（DivaShowDirector.Collect）。每局开始全部放回来。
    /// </summary>
    public class DivaShowPickups : MonoBehaviour
    {
        public float spacing = 3f, height = 1.7f, reach = 2.8f;
        public int Taken { get; private set; }

        class Candy { public Transform t; public Vector3 at; public bool taken, rocket; public float phase; public Material mat; }
        readonly List<Candy> candies = new List<Candy>();
        DivaShowDirector show;
        DigiPhantLocomotion loco;
        DivaGameManager game;
        Transform root;

        void Start()
        {
            show = GetComponent<DivaShowDirector>();
            loco = show ? show.locomotion : null;
            game = show ? show.game : null;
            if (!show || !loco || !loco.travelRoot || !game || !show.glowMaterial) { enabled = false; return; }
            root = new GameObject("Diva Candies").transform;
            Spawn();
        }

        void OnDestroy() { if (root) Destroy(root.gameObject); }

        /// <summary>新一局：全部放回来。</summary>
        public void Restore()
        {
            foreach (var c in candies) { c.taken = false; if (c.t) c.t.gameObject.SetActive(true); }
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
                if (len > 18)
                    foreach (float k in new[] { .33f, .67f })
                    {
                        var rp = a + dir * (len * k) + side * (k < .5f ? -1.8f : 1.8f); rp.y = a.y + height + .2f;
                        candies.Add(MakeRocket(rp));
                    }
            }
            Debug.Log($"DIVA_SHOW candies: {candies.Count(c => !c.rocket)} candies and {candies.Count(c => c.rocket)} rockets on the course");
        }

        Material Glow(Color c)
        {
            var m = new Material(show.glowMaterial);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            return m;
        }

        Candy Make(Vector3 at, Color colour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>(); if (col) Destroy(col);
            go.name = "Candy";
            go.transform.SetParent(root, false);
            go.transform.position = at;
            go.transform.localScale = Vector3.one * .42f;
            var mat = Glow(colour * 1.6f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            // 一圈糖纸：扁扁的圆环
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var rc = ring.GetComponent<Collider>(); if (rc) Destroy(rc);
            ring.name = "Wrap";
            ring.transform.SetParent(go.transform, false);
            ring.transform.localScale = new Vector3(1.9f, .05f, 1.9f);
            var rm = ring.GetComponent<MeshRenderer>();
            rm.sharedMaterial = Glow(Color.Lerp(colour, Color.white, .5f) * 1.2f); rm.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; rm.receiveShadows = false;
            return new Candy { t = go.transform, at = at, phase = Random.value * 6.28f, mat = mat };
        }

        Candy MakeRocket(Vector3 at)
        {
            var go = new GameObject("Rocket");
            go.transform.SetParent(root, false);
            go.transform.position = at;
            var body = Glow(new Color(1f, .5f, .8f) * 1.4f);
            var white = Glow(new Color(1f, .97f, 1f) * 1.3f);
            var teal = Glow(new Color(.4f, .95f, .9f) * 1.4f);
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

        void Update()
        {
            float t = Time.time;
            var e = loco.travelRoot.position;
            bool play = show.Phase == ShowPhase.Play;
            foreach (var c in candies)
            {
                if (c.taken || !c.t) continue;
                c.t.position = c.at + Vector3.up * (.12f * Mathf.Sin(t * 2.2f + c.phase));
                c.t.rotation = c.rocket ? Quaternion.Euler(-20, t * 120 + c.phase * 30, 0) : Quaternion.Euler(0, t * 90 + c.phase * 30, 20 * Mathf.Sin(t * 1.5f + c.phase));
                if (!play) continue;
                var d = c.at - e; d.y = 0;
                if (d.magnitude < reach)
                {
                    c.taken = true; Taken++;
                    c.t.gameObject.SetActive(false);
                    var colour = c.mat && c.mat.HasProperty("_BaseColor") ? c.mat.GetColor("_BaseColor") : new Color(1f, .8f, .3f);
                    show.Fx?.Flash(c.t.position, colour, 1.6f, .3f);
                    show.Fx?.Pop(c.t.position, colour, .5f);
                    show.Collect(show.candyPoints, c.t.position, c.rocket);
                }
            }
        }
    }
}
