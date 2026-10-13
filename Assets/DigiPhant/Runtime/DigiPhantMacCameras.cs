using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DigiPhant
{
    /// <summary>
    /// macOS: the camera list in the order the tracking bridge's OpenCV numbers it, so the game can pass the Mac's own
    /// camera as "--camera N". OpenCV (cap_avfoundation_mac) takes [AVCaptureDevice devicesWithMediaType:] for video plus
    /// muxed devices, sorts them by uniqueID and opens entry N. A nearby iPhone (Continuity Camera) whose ID happens to sort
    /// first becomes camera 0, which is why the game kept opening the phone. Read through the Objective-C runtime, so no
    /// plug-in and no change to the (locked) bridge is needed. Other platforms: no list, the configured number is used.
    /// </summary>
    public static class DigiPhantMacCameras
    {
        public struct Device
        {
            public string name, uniqueId, deviceType;
            public int transport;
            public bool continuity;
            /// <summary>The Mac's own camera (FaceTime / MacBook camera), not an iPhone, iPad or USB camera.</summary>
            public bool BuiltIn => !continuity && (deviceType == "AVCaptureDeviceTypeBuiltInWideAngleCamera" || transport == BuiltInTransport);
        }

        const int BuiltInTransport = 0x626C746E;   // 'bltn'

        /// <summary>The built-in camera's number for the bridge, or -1 (not a Mac, no built-in camera, or the list failed).
        /// <paramref name="summary"/> lists every camera in bridge order for the log (null when there is no list).</summary>
        public static int BuiltInIndex(out string summary)
        {
            summary = null;
            var devices = List();
            if (devices == null) return -1;
            int found = -1;
            var parts = new List<string>();
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (found < 0 && d.BuiltIn) found = i;
                parts.Add(i + " " + d.name + (d.BuiltIn ? " [built-in]" : d.continuity ? " [iPhone/iPad]" : ""));
            }
            summary = string.Join(", ", parts);
            return found;
        }

        /// <summary>Every camera in bridge order (null when it cannot be read).</summary>
        public static List<Device> List()
        {
            if (Application.platform != RuntimePlatform.OSXEditor && Application.platform != RuntimePlatform.OSXPlayer) return null;
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            try
            {
                dlopen("/System/Library/Frameworks/AVFoundation.framework/AVFoundation", 1);
                IntPtr deviceClass = objc_getClass("AVCaptureDevice"), stringClass = objc_getClass("NSString");
                if (deviceClass == IntPtr.Zero || stringClass == IntPtr.Zero) return null;
                var devices = new List<Device>();
                foreach (string mediaType in new[] { "vide", "muxx" })   // AVMediaTypeVideo, AVMediaTypeMuxed
                {
                    IntPtr type = SendText(stringClass, Sel("stringWithUTF8String:"), mediaType);
                    IntPtr array = SendPtr(deviceClass, Sel("devicesWithMediaType:"), type);
                    if (array == IntPtr.Zero) continue;
                    ulong count = SendULong(array, Sel("count"));
                    for (ulong i = 0; i < count; i++)
                    {
                        IntPtr device = SendIndex(array, Sel("objectAtIndex:"), i);
                        devices.Add(new Device
                        {
                            name = Text(Send(device, Sel("localizedName"))),
                            uniqueId = Text(Send(device, Sel("uniqueID"))),
                            deviceType = Responds(device, "deviceType") ? Text(Send(device, Sel("deviceType"))) : "",
                            transport = SendInt(device, Sel("transportType")),
                            continuity = Responds(device, "isContinuityCamera") && SendBool(device, Sel("isContinuityCamera")) != 0,
                        });
                    }
                }
                // Same order as OpenCV: [d1.uniqueID compare:d2.uniqueID] (a literal, case-sensitive comparison).
                devices.Sort((a, b) => string.CompareOrdinal(a.uniqueId, b.uniqueId));
                return devices;
            }
            catch (Exception e)
            {
                Debug.LogWarning("DIGIPHANT camera list unavailable: " + e.Message);
                return null;
            }
#else
            return null;
#endif
        }

#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        const string ObjC = "/usr/lib/libobjc.A.dylib";
        [DllImport("/usr/lib/libSystem.dylib")] static extern IntPtr dlopen(string path, int mode);
        [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
        [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr target, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendPtr(IntPtr target, IntPtr selector, IntPtr argument);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendText(IntPtr target, IntPtr selector, string argument);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendIndex(IntPtr target, IntPtr selector, ulong index);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern ulong SendULong(IntPtr target, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern int SendInt(IntPtr target, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern byte SendBool(IntPtr target, IntPtr selector);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern byte SendBoolPtr(IntPtr target, IntPtr selector, IntPtr argument);

        static IntPtr Sel(string name) => sel_registerName(name);
        static bool Responds(IntPtr target, string selector) => SendBoolPtr(target, Sel("respondsToSelector:"), Sel(selector)) != 0;
        static string Text(IntPtr nsString) => nsString == IntPtr.Zero ? "" : Marshal.PtrToStringUTF8(Send(nsString, Sel("UTF8String"))) ?? "";
#endif
    }
}
