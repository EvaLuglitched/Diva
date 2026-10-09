using UnityEngine;
using UnityEngine.InputSystem;

namespace Diva.VR
{
    /// <summary>
    /// 电脑上没有头显时代替 Vision Pro：
    /// 按住鼠标右键拖动 = 转头；鼠标左键 = 捏（喷水，朝你看的方向）；
    /// W = 前进，A / D = 转向，E = 喝水，U = 大招（双手举高）；回车、Esc、L 和原来一样。
    /// </summary>
    public class DivaVRSimulator : MonoBehaviour
    {
        [Tooltip("鼠标转头灵敏度（度/像素）。")]
        public float lookSpeed = .15f;

        /// <summary>模拟的头朝向（度）。后台测试可以直接设。</summary>
        public float yaw, pitch;

        /// <summary>后台测试用：不读键盘，直接用这一帧。</summary>
        [System.NonSerialized] public bool scripted;
        [System.NonSerialized] public DivaVRFrame scriptedFrame;

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (!scripted && mouse != null && mouse.rightButton.isPressed)
            {
                var d = mouse.delta.ReadValue();
                yaw += d.x * lookSpeed;
                pitch = Mathf.Clamp(pitch - d.y * lookSpeed, -70, 70);
            }
            // 和头显一样每帧给出头的姿态（头显的位置追踪也是每帧覆盖），别的代码动了镜头也会被纠正
            if (DivaVR.Head) DivaVR.Head.SetLocalPositionAndRotation(Vector3.zero, Quaternion.Euler(pitch, yaw, 0));
        }

        public DivaVRFrame Read()
        {
            if (scripted) return scriptedFrame;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            var f = new DivaVRFrame();
            if (kb != null)
            {
                f.go = kb.wKey.isPressed ? 1 : 0;
                f.steer = (kb.dKey.isPressed ? 1 : 0) - (kb.aKey.isPressed ? 1 : 0);
                f.drink = kb.eKey.isPressed;
                f.ult = kb.uKey.isPressed;
            }
            f.shoot = mouse != null && mouse.leftButton.isPressed;
            return f;
        }
    }
}
