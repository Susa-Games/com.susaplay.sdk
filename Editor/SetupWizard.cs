using UnityEditor;
using UnityEngine;
using susaplay.SDK;

namespace susaplay.SDK.Editor
{

    public class SetupWizard : EditorWindow
    {
        private string _gameKey;
        private SDKConfig _config;
        private bool _automaticAnalyticsFlushEnabled = true;
        private float _analyticsFlushIntervalSeconds = 300f;
        private bool _flushAnalyticsOnInitialize = true;
        private bool _flushAnalyticsOnPause = true;
        private bool _flushAnalyticsOnQuit = true;

        [MenuItem("SusaPlay/Setup")]
        public static void ShowWindow()
        {
            GetWindow<SetupWizard>("susaplay Setup");
        }
        private void OnEnable()
        {
            _config = SDKConfig.Load();
            if (_config != null)
            {
                _gameKey = _config.GameKey;
                _automaticAnalyticsFlushEnabled = _config.AutomaticAnalyticsFlushEnabled;
                _analyticsFlushIntervalSeconds = _config.AnalyticsFlushIntervalSeconds;
                _flushAnalyticsOnInitialize = _config.FlushAnalyticsOnInitialize;
                _flushAnalyticsOnPause = _config.FlushAnalyticsOnPause;
                _flushAnalyticsOnQuit = _config.FlushAnalyticsOnQuit;
            }
        }

        private void OnGUI()
        {
            GUILayout.Label("susaplay SDK Setup", EditorStyles.boldLabel);
            _gameKey = EditorGUILayout.TextField("Game Key", _gameKey);
            EditorGUILayout.Space();
            GUILayout.Label("Analytics Flush", EditorStyles.boldLabel);
            _automaticAnalyticsFlushEnabled = EditorGUILayout.Toggle("Automatic Flush", _automaticAnalyticsFlushEnabled);
            using (new EditorGUI.DisabledScope(!_automaticAnalyticsFlushEnabled))
            {
                _analyticsFlushIntervalSeconds = EditorGUILayout.FloatField("Interval Seconds", _analyticsFlushIntervalSeconds);
                _flushAnalyticsOnInitialize = EditorGUILayout.Toggle("On SDK Ready", _flushAnalyticsOnInitialize);
                _flushAnalyticsOnPause = EditorGUILayout.Toggle("On App Pause", _flushAnalyticsOnPause);
                _flushAnalyticsOnQuit = EditorGUILayout.Toggle("On App Quit", _flushAnalyticsOnQuit);
            }
            if (GUILayout.Button("Save"))
            {
                if (_config == null)
                {
                    _config = ScriptableObject.CreateInstance<SDKConfig>();
                    if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                        AssetDatabase.CreateFolder("Assets", "Resources");
                    _config.SetGameKey(_gameKey);
                    AssetDatabase.CreateAsset(_config, "Assets/Resources/PlatformConfig.asset");
                }
                _config.SetGameKey(_gameKey);
                _config.SetAutomaticAnalyticsFlushEnabled(_automaticAnalyticsFlushEnabled);
                _config.SetAnalyticsFlushIntervalSeconds(_analyticsFlushIntervalSeconds);
                _config.SetAnalyticsLifecycleFlushes(
                    _flushAnalyticsOnInitialize,
                    _flushAnalyticsOnPause,
                    _flushAnalyticsOnQuit);
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
