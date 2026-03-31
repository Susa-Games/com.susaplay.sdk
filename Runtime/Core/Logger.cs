using UnityEngine;
namespace susaplay.SDK
{

    public static class Logger
    {
        public static bool IsEnabled = true;

        public static void Log(string message)
        {
            if (IsEnabled)
            {
                Debug.Log("[susaplay] " + message);
            }
            return;
        }

        public static void Warn(string message)
        {
            if (IsEnabled)
            {
                Debug.LogWarning("[susaplay] " + message);
            }
            return;
        }

        public static void Error(string message)
        {

            Debug.LogError("[susaplay] " + message);

        }
    }
}
