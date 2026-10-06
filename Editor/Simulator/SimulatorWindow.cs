using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace susaplay.SDK.Editor.Simulator
{
    /// <summary>SusaPlay → Simulator: the simulated player, the configuration in use, and the request log.</summary>
    internal sealed class SimulatorWindow : EditorWindow
    {
        private Label _project;
        private Label _player;
        private Label _configuration;
        private HelpBox _banner;
        private Label _resetNote;
        private ListView _log;
        private Toggle _errorsOnly;
        private static string _sdkVersion;
        private readonly List<RequestLogEntry> _rows = new List<RequestLogEntry>();

        [MenuItem("SusaPlay/Simulator")]
        public static void Open()
        {
            GetWindow<SimulatorWindow>("SusaPlay Simulator");
        }

        public void CreateGUI()
        {
            var root = rootVisualElement;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;

            root.Add(Heading("Status"));
            _project = new Label();
            _player = new Label();
            _configuration = new Label();
            _banner = new HelpBox("", HelpBoxMessageType.Warning);
            root.Add(_project);
            root.Add(_player);
            root.Add(_configuration);
            root.Add(_banner);

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 4 } };
            actions.Add(new Button(ConfirmReset) { text = "Reset player" });
            actions.Add(new Button(SusaPlaySimulator.ReloadConfig) { text = "Reload configuration" });
            root.Add(actions);
            _resetNote = new Label("Reset applies when Play Mode ends.") { style = { unityFontStyleAndWeight = FontStyle.Italic } };
            root.Add(_resetNote);

            root.Add(Heading("Request log"));
            var toolbar = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            _errorsOnly = new Toggle("Errors only");
            _errorsOnly.RegisterValueChangedCallback(_ => RefreshLog());
            toolbar.Add(_errorsOnly);
            toolbar.Add(new Button(() => SusaPlaySimulator.Log.Clear()) { text = "Clear" });
            toolbar.Add(new Button(CopyLog) { text = "Copy" });
            root.Add(toolbar);

            _log = new ListView(_rows, 18, () => new Label(), (element, index) =>
            {
                var entry = _rows[index];
                var label = (Label)element;
                label.text = entry.Describe();
                label.tooltip = entry.Detail;
                label.style.color = entry.IsError ? new StyleColor(new Color(0.95f, 0.55f, 0.3f)) : new StyleColor(StyleKeyword.Null);
            })
            {
                style = { flexGrow = 1 },
                selectionType = SelectionType.Single,
            };
            root.Add(_log);

            Refresh();
        }

        private void OnEnable()
        {
            SusaPlaySimulator.Changed += Refresh;
            SusaPlaySimulator.Log.Changed += RefreshLog;
        }

        private void OnDisable()
        {
            SusaPlaySimulator.Changed -= Refresh;
            SusaPlaySimulator.Log.Changed -= RefreshLog;
        }

        private void Refresh()
        {
            if (_project == null)
            {
                return;
            }

            var sdkConfig = SDKConfig.Load();
            var projectKey = sdkConfig != null ? sdkConfig.GameKey : null;
            _sdkVersion ??= UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SusaPlaySDK).Assembly)?.version ?? "?";
            _project.text = "Project gameKey: " + (string.IsNullOrEmpty(projectKey) ? "—" : projectKey) + "    SDK " + _sdkVersion;

            var shell = SusaPlaySimulator.Shell;
            var state = SusaPlaySimulator.State;
            var mode = shell.InSession ? (shell.IsGuest ? "guest" : "signed in") : (SusaPlaySimulator.Settings.guest ? "guest at next Play" : "signed in at next Play");
            _player.text = "Player: " + state.displayName + " (" + state.uid + "), " + mode;

            var config = SusaPlaySimulator.Config;
            _configuration.text = config.Loaded
                ? "Configuration: " + config.GameName + " (" + config.GameId + "), exported " + (config.ExportedAt ?? "at an unknown time")
                : "Configuration: none";

            string banner = null;
            if (sdkConfig == null)
            {
                banner = "PlatformConfig.asset is missing, so SusaPlaySDK.Initialize() fails before reaching the simulator. Run SusaPlay → Setup.";
            }
            else if (config.LoadError != null)
            {
                banner = "The configuration file could not be read (" + config.LoadError + "). The simulator behaves as for a game with nothing configured.";
            }
            else if (!config.Loaded)
            {
                banner = "No game configuration imported. The store is empty, every achievement is NOT_FOUND and every purchase is ITEM_NOT_FOUND, " +
                         "as on the platform for a game with nothing configured. Download it from the game's page in the Developer Portal.";
            }
            else if (!string.IsNullOrEmpty(config.GameKey) && config.GameKey != projectKey)
            {
                banner = "The configuration is for " + config.GameName + " (" + config.GameKey + "), not this project's gameKey. One of them is wrong.";
            }
            _banner.text = banner ?? "";
            _banner.style.display = banner == null ? DisplayStyle.None : DisplayStyle.Flex;
            _resetNote.style.display = SusaPlaySimulator.ResetPending ? DisplayStyle.Flex : DisplayStyle.None;

            RefreshLog();
        }

        private void RefreshLog()
        {
            if (_log == null)
            {
                return;
            }
            _rows.Clear();
            var errorsOnly = _errorsOnly != null && _errorsOnly.value;
            _rows.AddRange(SusaPlaySimulator.Log.Entries.Where(e => !errorsOnly || e.IsError));
            _log.RefreshItems();
            if (_rows.Count > 0)
            {
                _log.ScrollToItem(_rows.Count - 1);
            }
        }

        private void ConfirmReset()
        {
            if (EditorUtility.DisplayDialog("Reset the simulated player?",
                    "Saves, achievements, wallet and inventory are deleted and a new player ID is created.", "Reset", "Cancel"))
            {
                SusaPlaySimulator.Reset();
            }
        }

        private void CopyLog()
        {
            EditorGUIUtility.systemCopyBuffer = SusaPlaySimulator.Log.ToText(_errorsOnly != null && _errorsOnly.value);
        }

        private static Label Heading(string text)
        {
            return new Label(text) { style = { unityFontStyleAndWeight = FontStyle.Bold, marginTop = 8, marginBottom = 2 } };
        }
    }
}
