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
