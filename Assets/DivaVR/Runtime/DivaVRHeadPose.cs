using UnityEngine;
using UnityEngine.XR;

namespace Diva.VR
{
    /// <summary>
    /// 头显的头部追踪：从 Unity 的核心 XR 输入（XRNode.CenterEye，没有就用 Head）读头的位置和朝向，写到主摄像机上。
    /// visionOS 插件只在特定编译条件下才注册 Input System 的 XRHMD 设备，`<XRHMD>/centerEyePosition` 在 Vision Pro 上
    /// 读到的一直是 0（头不动、视角掉到座位高度），所以不用 Input System 的 TrackedPoseDriver。
    /// Update 里先写一次（给手势和瞄准用），渲染前再写一次（减少延迟）。
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public class DivaVRHeadPose : MonoBehaviour
    {
        public Transform head;
        public bool Valid { get; private set; }

        void OnEnable() => Application.onBeforeRender += Apply;
        void OnDisable() => Application.onBeforeRender -= Apply;
        void Update() => Apply();

        void Apply()
        {
            if (!head) return;
            var dev = InputDevices.GetDeviceAtXRNode(XRNode.CenterEye);
            if (!dev.isValid) dev = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!dev.isValid) return;
            bool p = dev.TryGetFeatureValue(CommonUsages.centerEyePosition, out var pos) || dev.TryGetFeatureValue(CommonUsages.devicePosition, out pos);
            bool r = dev.TryGetFeatureValue(CommonUsages.centerEyeRotation, out var rot) || dev.TryGetFeatureValue(CommonUsages.deviceRotation, out rot);
            if (p) head.localPosition = pos;
            if (r) head.localRotation = rot;
            Valid |= p || r;
        }
    }
}
