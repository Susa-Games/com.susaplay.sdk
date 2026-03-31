using System.Runtime.InteropServices;
using UnityEngine;
using System;

namespace susaplay.SDK
{
    public static class WebGLBridge
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SendMessageToShell(string message);

        [DllImport("__Internal")]
        private static extern void InitializeMessageListener(Action<string> callback);
#endif
        public static event Action<string> OnMessageReceived;
        public static void Initialize()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            InitializeMessageListener(HandleMessage);
#endif
        }

        [AOT.MonoPInvokeCallback(typeof(Action<string>))]
        private static void HandleMessage(string message)
        {
            OnMessageReceived?.Invoke(message);
        }

        public static void SendMessage(BridgeMessage message)
        {
            var json = JsonUtility.ToJson(message);
            Logger.Log("Bridge sending: " + json);
#if UNITY_WEBGL && !UNITY_EDITOR
            SendMessageToShell(json);
#endif
        }
    }
}