using UnityEditor;
using UnityEngine;
using susaplay.SDK;

namespace susaplay.SDK.Editor
{

    public class SetupWizard : EditorWindow
    {
        private string _gameKey;
        private SDKConfig _config;

        [MenuItem("SusaPlay/Setup")]
        public static void ShowWindow()
        {
            GetWindow<SetupWizard>("susaplay Setup");
        }
        private void OnEnable()
        {
            _config = SDKConfig.Load();
            if (_config != null)
                _gameKey = _config.GameKey;
        }

        private void OnGUI()
        {
            GUILayout.Label("susaplay SDK Setup", EditorStyles.boldLabel);
            _gameKey = EditorGUILayout.TextField("Game Key", _gameKey);
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
                EditorUtility.SetDirty(_config);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
