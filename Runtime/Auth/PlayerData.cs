using System;
using UnityEngine;
namespace susaplay.SDK
{
    [Serializable]
    public class PlayerData
    {
        public string mode;
        public string uid;
        public string displayName;
        public string avatarUrl;
        public string playerId;
        public string playerID;
        public string playerid;
        public string gameId;
        public string sessionId;
        /// <summary>True when the page that runs the game can close it and return the player
        /// to the app that opened it — a partner's embed. See SusaPlaySDK.ExitToHost.</summary>
        public bool canExitToHost;

        public string PlayerIdOrUid()
        {
            if (!string.IsNullOrEmpty(playerID))
                return playerID;
            if (!string.IsNullOrEmpty(playerId))
                return playerId;
            if (!string.IsNullOrEmpty(playerid))
                return playerid;
            return uid;
        }
    }
}
