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

#if UNITY_EDITOR
        /// <summary>
        /// Where outbound messages go in the Editor, in place of the jslib. Set by the Editor
        /// Simulator; null means the Editor drops them, as it always has. Not cleared by
        /// <see cref="ResetForNewSession"/>: it belongs to the Editor assembly, which sets it
        /// once per domain reload, not to a Play session.
        /// </summary>
        internal static Action<string> EditorOutbound;

        /// <summary>The Editor Simulator's way to answer, as the page does through the jslib callback.</summary>
        internal static void DeliverFromEditor(string json)
        {
            HandleMessage(json);
        }
#endif

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
#elif UNITY_EDITOR
            EditorOutbound?.Invoke(json);
#endif
        }
    }
}