using System;
using System.Collections.Generic;
using UnityEngine;

namespace Diva
{
    [Serializable]
    public class DivaMechSkin
    {
        public string id;
        public string nameEn, nameZh;
        [Tooltip("Palette material for all opaque armour (same layout as Mech Atlas, different colours).")]
        public Material atlas;
        public Material glass;
        [Tooltip("The elephant's own skin for this look.")]
        public Material elephant;
        [Tooltip("Colours for menus and cards: main shell, second colour, glow.")]
        public Color primary = Color.white, secondary = Color.white, accent = Color.cyan;
        [Tooltip("Thruster flame over its lifetime: white-hot core to tail.")]
        public Gradient flame = new Gradient();
        public Color flameGlow = new Color(.4f, .9f, 1f, .35f);
    }

    /// <summary>
    /// 机甲皮肤：只换颜色不换模型。每款皮肤是一张调色板材质（和 Mech Atlas 同样的布局）、一个玻璃材质、
    /// 一个大象皮肤材质和一组火焰颜色。Apply 只改运行时的渲染器和粒子，不改资源。
    /// 由 Diva > Add D.Va Mech（或 Diva > Rebuild D.Va Mech Skins）生成和填写。
    /// </summary>
    [DisallowMultipleComponent]
    public class DivaMechSkins : MonoBehaviour
    {
        public DivaMechSkin[] skins = new DivaMechSkin[0];
        [Tooltip("Skin shown when Play starts.")]
        public int startSkin;
        public DivaMechToggle toggle;
        public DivaBoosters boosters;

        public int Current { get; private set; } = -1;
        public DivaMechSkin CurrentSkin => skins != null && Current >= 0 && Current < skins.Length ? skins[Current] : null;
        public event Action<int> Changed;

        Renderer[] renderers;
        readonly HashSet<Material> atlases = new HashSet<Material>(), glasses = new HashSet<Material>();

        void Start()
        {
            if (Current < 0 && skins != null && skins.Length > 0) Apply(startSkin);
        }

        public string DisplayName(int index, bool chinese)
        {
            if (skins == null || index < 0 || index >= skins.Length) return "";
            return chinese && !string.IsNullOrEmpty(skins[index].nameZh) ? skins[index].nameZh : skins[index].nameEn;
        }

        public void Next(int step) { if (skins != null && skins.Length > 0) Apply(((Current < 0 ? 0 : Current) + step + skins.Length) % skins.Length); }

        public void Apply(int index)
        {
            if (skins == null || skins.Length == 0) return;
            index = Mathf.Clamp(index, 0, skins.Length - 1);
            var skin = skins[index];
            if (!toggle) toggle = GetComponentInChildren<DivaMechToggle>(true);
            if (!boosters) boosters = GetComponent<DivaBoosters>();
            CollectRenderers();
            foreach (var r in renderers)
            {
                if (!r) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (atlases.Contains(mats[i]) && skin.atlas && mats[i] != skin.atlas) { mats[i] = skin.atlas; changed = true; }
                    else if (glasses.Contains(mats[i]) && skin.glass && mats[i] != skin.glass) { mats[i] = skin.glass; changed = true; }
                }
                if (changed) r.sharedMaterials = mats;
            }
            if (toggle)
            {
                toggle.skinOverride = skin.elephant;
                toggle.Apply();
            }
            if (boosters)
            {
                foreach (var ps in boosters.cores)
                {
                    if (!ps) continue;
                    var col = ps.colorOverLifetime; col.color = skin.flame;
                }
                foreach (var ps in boosters.glows)
                {
                    if (!ps) continue;
                    var main = ps.main; main.startColor = skin.flameGlow;
                }
            }
            Current = index;
            Changed?.Invoke(index);
        }

        // 机甲零件上凡是任何一款皮肤的调色板/玻璃材质，都算可换的槽位
        void CollectRenderers()
        {
            atlases.Clear(); glasses.Clear();
            foreach (var s in skins)
            {
                if (s == null) continue;
                if (s.atlas) atlases.Add(s.atlas);
                if (s.glass) glasses.Add(s.glass);
            }
            if (renderers != null) return;
            var list = new List<Renderer>();
            if (toggle && toggle.parts != null)
                foreach (var part in toggle.parts)
                    if (part) list.AddRange(part.GetComponentsInChildren<MeshRenderer>(true));
            renderers = list.ToArray();
        }
    }
}
