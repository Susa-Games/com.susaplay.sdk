using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using susaplay.SDK.Samples;

namespace susaplay.SDK.Samples.Editor
{
    public static class SusaPlaySmokeTestTools
    {
        [MenuItem("SusaPlay/Smoke Test/Create Test Scene")]
        public static void CreateTestScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var testerGo = new GameObject("SusaPlaySdkSmokeTester");
            testerGo.AddComponent<SusaPlaySdkSmokeTester>();

            if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
            {
                AssetDatabase.CreateFolder("Assets", "Scenes");
            }

            const string scenePath = "Assets/Scenes/SusaPlaySmokeTest.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Debug.Log("[SusaPlay SmokeTest] Scene created at " + scenePath);
            Debug.Log("[SusaPlay SmokeTest] Next steps:");
            Debug.Log("[SusaPlay SmokeTest] 1) Set Game Key via SusaPlay/Setup");
            Debug.Log("[SusaPlay SmokeTest] 2) Build WebGL and upload to platform");
        }
    }
}
