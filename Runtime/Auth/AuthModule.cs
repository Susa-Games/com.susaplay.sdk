using System.Threading.Tasks;
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
        private TokenManager _tokenManager;

        /// <summary>
        /// The player's short-lived, game-scoped session token, or null when there is none.
        /// </summary>
        /// <remarks>
        /// For a game whose own backend runs outside this platform and has to know which player is
        /// calling it. Send the result as <c>Authorization: Bearer {token}</c>; that backend then
        /// asks the platform who the token belongs to, and the platform answers with a uid. The
        /// token is scoped to this one game and expires, so it cannot be replayed against another
        /// game or kept forever.
        ///
        /// <para>
        /// Returns null for a guest. A guest has no platform account and therefore no identity to
        /// mint a token for, so a feature that depends on this must degrade rather than fail — the
        /// game is still perfectly playable, it simply cannot prove who is playing.
        /// </para>
        ///
        /// <para>
        /// Call it when you need it rather than holding the value. The SDK already caches, and the
        /// token behind it is refreshed as it ages; a copy kept in a field will one day be the stale
        /// one. Every call before <see cref="SusaPlaySDK.Initialize"/> finishes returns null.
        /// </para>
        /// </remarks>
        public Task<string> GetSessionTokenAsync()
        {
            if (_tokenManager == null)
            {
                Logger.Warn("GetSessionTokenAsync called before the SDK finished initializing.");
                return Task.FromResult<string>(null);
            }

            return _tokenManager.GetTokenAsync();
        }

        // Initialize method
        public void Initialize(PlayerData playerData, TokenManager tokenManager = null)
        {
            _playerData = playerData;
            _tokenManager = tokenManager;
            // Unsubscribe first — a retried SDK init constructs a new module, and the old one
            // would otherwise stay subscribed to the bridge for the life of the page.
            WebGLBridge.OnMessageReceived -= HandleMessage;
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