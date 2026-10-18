using System.Threading.Tasks;

namespace susaplay.SDK
{
    /// <summary>
    /// Reports achievement progress to SusaPlay.
    ///
    /// A game sends only the achievement id. Name, icon and point value come from
    /// the definition the developer registered in the SusaPlay developer portal,
    /// because achievement points feed the player's platform-wide level and a game
    /// that set its own point values could mint level progress at will.
    ///
    /// An id must be registered in the developer portal first. Anything else is
    /// refused with 404 NOT_FOUND and nothing is recorded, so a buggy or hostile
    /// build cannot create unbounded achievement records.
    /// </summary>
    public class AchievementsModule
    {
        private readonly HttpClient _httpClient;
        private readonly string _gameId;

        public AchievementsModule(HttpClient httpClient, string gameId)
        {
            _httpClient = httpClient;
            _gameId = gameId;
        }

        /// <summary>
        /// Unlocks a one-shot achievement. Safe to call again — a second unlock of
        /// the same achievement awards nothing and overwrites nothing.
        /// </summary>
        public async Task<HttpResponse> Unlock(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId))
            {
                return HttpResponse.Fail("achievementId must not be empty", 0);
            }

            var body = "{\"gameId\":" + JsonString(_gameId)
                + ",\"achievementId\":" + JsonString(achievementId) + "}";
            return await _httpClient.Post("/engagement/achievements/unlock", body);
        }

        /// <summary>
        /// Advances an incremental achievement by <paramref name="amount"/>. The
        /// server owns the target and unlocks the achievement when the counter
        /// reaches it, so the game never has to track the threshold itself.
        /// </summary>
        public async Task<HttpResponse> Increment(string achievementId, int amount = 1)
        {
            if (string.IsNullOrEmpty(achievementId))
            {
                return HttpResponse.Fail("achievementId must not be empty", 0);
            }
            if (amount <= 0)
            {
                return HttpResponse.Fail("amount must be greater than zero", 0);
            }

            var body = "{\"gameId\":" + JsonString(_gameId)
                + ",\"achievementId\":" + JsonString(achievementId)
                + ",\"incrementBy\":" + amount + "}";
            return await _httpClient.Post("/engagement/achievements/increment", body);
        }

        /// <summary>Every achievement for this game with the player's progress on it.</summary>
        public async Task<HttpResponse> List()
        {
            return await _httpClient.Get(
                "/engagement/achievements/list?gameId=" + UnityEngine.Networking.UnityWebRequest.EscapeURL(_gameId));
        }

        private static string JsonString(string value)
        {
            if (value == null)
            {
                return "null";
            }

            var sb = new System.Text.StringBuilder("\"");
            foreach (var c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ')
                        {
                            sb.Append("\\u").Append(((int)c).ToString("x4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }
}
