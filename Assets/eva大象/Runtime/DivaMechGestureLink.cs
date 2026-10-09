using System.Reflection;
using UnityEngine;

namespace Diva
{
    /// <summary>
    /// 把 Diva 三人手势（DivaDemo.State）接到机甲大象的动画上：
    ///   P3 双手张开往前推（Shoot）-> 鼻尖水枪喷水，方向和弹道跟 DivaDemo 的打靶水滴一致，P3 举手瞄准时跟着左右转；
    ///   P3 双手放到嘴边（Drink）-> 泡泡炮冒泡泡；
    ///   P1 摆臂 / P2 倾身 -> 通过移动速度驱动推进器火焰和脚步声（DivaBoosters、DivaMechAudio 自己读速度，这里不用管）。
    /// 用反射读 DivaDemo，这个包放在没有 DivaDemo 的普通 DigiPhant 项目里也能编译，那时本组件什么都不做。
    /// 在 DivaDemo（执行顺序 200）之后运行，读到的是同一帧的手势状态。
    /// </summary>
    [DefaultExecutionOrder(210)]
    [DisallowMultipleComponent]
    public class DivaMechGestureLink : MonoBehaviour
    {
        [Tooltip("The scene's DivaDemo (on DigiPhant Controls). Filled in by Diva > Add D.Va Mech.")]
        public MonoBehaviour demo;
        public DivaTrunkBlaster waterGun;
        public DivaBubbleCannons bubbles;

        [Header("P3 spray -> trunk water gun")]
        public bool sprayFromGesture = true;
        [Tooltip("Shoot the jet along DivaDemo's water path (same direction, speed and drop as the target-hit droplets).")]
        public bool followGameAim = true;
        [Tooltip("Hide DivaDemo's blue placeholder droplets; their target hits still count.")]
        public bool hideDemoDroplets = true;

        [Header("P3 drink -> bubble cannons")]
        public bool bubblesFromDrinking = true;

        // DivaDemo 的水滴：水平速度 17，重力 2.5（Unity 默认 9.81），最多飞 2 秒
        const float DemoSpeed = 17, DemoGravity = 2.5f;

        PropertyInfo shootProp, drinkProp, aimProp;
        FieldInfo aimDegreesField;
        object state;
        DigiPhant.DigiPhantLocomotion locomotion;
        Transform dropletRoot;
        float nextDropletSearch;
        bool tunedJet;
        Quaternion restLocalRotation;
        float restJetSpeed;

        /// <summary>True when a DivaDemo is present and running, so gestures drive the effects.</summary>
        public bool Linked => demo && demo.isActiveAndEnabled && state != null;

        void OnEnable()
        {
            if (waterGun) { restLocalRotation = waterGun.transform.localRotation; restJetSpeed = waterGun.jetSpeed; }
            Bind();
        }

        void OnDisable()
        {
            if (waterGun)
            {
                waterGun.gestureSpray = false;
                waterGun.transform.localRotation = restLocalRotation;
                waterGun.jetSpeed = restJetSpeed;
            }
            if (bubbles) bubbles.gestureBlow = false;
        }

        void Bind()
        {
            if (!demo) return;
            var t = demo.GetType();
            var stateProp = t.GetProperty("State", BindingFlags.Public | BindingFlags.Instance);
            state = stateProp?.GetValue(demo);
            if (state == null) return;
            var st = state.GetType();
            shootProp = st.GetProperty("Shoot");
            drinkProp = st.GetProperty("Drink");
            aimProp = st.GetProperty("Aim");
            aimDegreesField = t.GetField("trunkAimDegrees");
            locomotion = demo.GetComponent<DigiPhant.DigiPhantLocomotion>();
        }

        bool ReadBool(PropertyInfo p) => p != null && (bool)p.GetValue(state);
        float ReadFloat(PropertyInfo p) => p != null ? (float)p.GetValue(state) : 0;

        void LateUpdate()
        {
            if (state == null) Bind();
            if (!Linked) return;
            bool shoot = ReadBool(shootProp), drink = ReadBool(drinkProp);
            float aim = ReadFloat(aimProp);

            if (waterGun)
            {
                waterGun.gestureSpray = sprayFromGesture && shoot;
                if (followGameAim && shoot) AimLikeDemo(aim);
                else waterGun.transform.localRotation = restLocalRotation;
            }
            if (bubbles) bubbles.gestureBlow = bubblesFromDrinking && drink;
            if (hideDemoDroplets) HideDroplets();
        }

        // 和 DivaDemo.EmitBurst 同样的方向和初速度，所以水柱落点就是打靶判定的位置
        void AimLikeDemo(float aim)
        {
            var root = locomotion ? locomotion.travelRoot : null;
            if (!root) return;
            if (!tunedJet && waterGun.jet)
            {
                var main = waterGun.jet.main;
                main.gravityModifier = DemoGravity / 9.81f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.5f);
                tunedJet = true;
            }
            float degrees = aimDegreesField != null ? (float)aimDegreesField.GetValue(demo) : 35;
            Vector3 origin = waterGun.transform.position;
            Vector3 dir = Quaternion.AngleAxis(aim * degrees, Vector3.up) * root.forward;
            float up = (root.position.y + 1.4f - origin.y) * 1.7f + .74f;
            Vector3 v = dir * DemoSpeed + Vector3.up * up;
            waterGun.transform.rotation = Quaternion.LookRotation(v.normalized, Vector3.up);
            waterGun.jetSpeed = v.magnitude;
        }

        void HideDroplets()
        {
            if (!dropletRoot && Time.unscaledTime >= nextDropletSearch)
            {
                var go = GameObject.Find("Diva water targets");
                dropletRoot = go ? go.transform : null;
                nextDropletSearch = Time.unscaledTime + 1;
            }
            if (!dropletRoot) return;
            foreach (Transform child in dropletRoot)
                if (child.name == "Water")
                {
                    var r = child.GetComponent<Renderer>();
                    if (r && r.enabled) r.enabled = false;
                }
        }

        /// <summary>Editor preview: pose the water gun as if the gesture were held this frame.</summary>
        public void PreviewAim(float aim)
        {
            if (state == null) Bind();
            if (waterGun) AimLikeDemo(aim);
        }
    }
}
