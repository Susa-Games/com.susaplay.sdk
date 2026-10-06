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

        /// <summary>
        /// Drops every subscriber. Static events outlive a Play session when the Editor's
        /// domain reload is off, so without this a module from an earlier session would
        /// still receive — and act on — the next session's messages.
        /// </summary>
        internal static void ResetForNewSession()
        {
            OnMessageReceived = null;
        }

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