using UnityEngine;
namespace susaplay.SDK
{
    public class AuthModule
    {
        public bool IsGuest => _playerData?.mode == "guest";
        public bool IsAuthenticated => _playerData?.mode == "authenticated";
        public string Uid => _playerData?.uid;
        public string DisplayName => _playerData?.displayName;
        private PlayerData _playerData;

        // Initialize method
        public void Initialize(PlayerData playerData)
        {
            _playerData = playerData;
            WebGLBridge.OnMessageReceived += HandleMessage;
        }
        private void HandleMessage(string json)
        {
            var message = JsonUtility.FromJson<BridgeMessage>(json);
            if (message.type != "SDK_AUTH_COMPLETE")
            {
                return;
            }
            _playerData = JsonUtility.FromJson<PlayerData>(message.payload);
            Logger.Log("Player signed in: " + _playerData.displayName);
        }
    }
}