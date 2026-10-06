using System;
using UnityEditor;

namespace susaplay.SDK.Editor.Simulator
{
    /// <summary>
    /// Wires the simulator into the SDK's two Editor hooks after every domain reload, before any
    /// game code runs, and owns the state, configuration and log the window shows.
    /// </summary>
    [InitializeOnLoad]
    internal static class SusaPlaySimulator
    {
        private const string ResetPendingKey = "SusaPlaySimulator.ResetPending";

        private static readonly SimulatorStateStore Store = new SimulatorStateStore();
        private static SimulatorState _state;

        public static RequestLog Log { get; } = new RequestLog();
        public static SimulatorConfig Config { get; private set; } = SimulatorConfig.Empty();
        public static SimulatorSettings Settings { get; private set; } = SimulatorSettings.Load();
        public static LocalBackend Backend { get; }
        public static EditorShell Shell { get; }

        /// <summary>Raised when anything the window shows may have changed.</summary>
        public static event Action Changed;

        static SusaPlaySimulator()
        {
            ReloadConfig();
            Backend = new LocalBackend(() => State, () => Config, SaveState);
            Shell = new EditorShell(Backend, () => State, () => Config, () => Settings, Log)
            {
                Deliver = WebGLBridge.DeliverFromEditor,
                BeforeSession = ReloadConfig,
            };
            Shell.Changed += () => Changed?.Invoke();

            WebGLBridge.EditorOutbound = Shell.OnOutbound;
            HttpClient.EditorTransport = Shell.OnHttp;

            EditorApplication.update += () => Shell.Pump(EditorApplication.timeSinceStartup);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static SimulatorState State => _state ??= Store.Load();

        /// <summary>True when a Reset was pressed during Play and waits for Play Mode to end.</summary>
        public static bool ResetPending => SessionState.GetBool(ResetPendingKey, false);

        /// <summary>
        /// A new player with an empty state. During Play it waits for the next Play, so the running
        /// session keeps a consistent player.
        /// </summary>
        public static void Reset()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.SetBool(ResetPendingKey, true);
                Changed?.Invoke();
                return;
            }
            ApplyReset();
        }

        public static void ReloadConfig()
        {
            Config = SimulatorConfig.LoadFile();
            Changed?.Invoke();
        }

        public static void SaveSettings()
        {
            Settings.Save();
            Changed?.Invoke();
        }

        private static void ApplyReset()
        {
            SessionState.EraseBool(ResetPendingKey);
            Store.Delete();
            _state = SimulatorState.CreateNew();
            SaveState();
            Log.Clear();
            Changed?.Invoke();
        }

        private static void SaveState()
        {
            Store.Save(State);
            Changed?.Invoke();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode)
            {
                return;
            }
            // Pending purchases and ads belong to the session that just ended.
            Shell.EndSession();
            if (_state != null)
            {
                SaveState();
            }
            if (ResetPending)
            {
                ApplyReset();
            }
        }
    }
}
