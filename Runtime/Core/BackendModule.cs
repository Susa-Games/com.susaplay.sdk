using System;
using System.Threading.Tasks;

namespace susaplay.SDK
{
    /// <summary>
    /// Authenticated access to the platform's own API from inside a game.
    /// <para>
    /// This is a thin pass-through over <see cref="HttpClient"/>, which already attaches the
    /// player's bearer token and the App Check header. It exists because neither of the SDK's
    /// existing surfaces fits: <see cref="ApiModule"/> routes through the shell's partner bridge
    /// and needs a partner key, and the token manager is private, so a game had no way to make an
    /// authenticated platform call at all.
    /// </para>
    /// <para>
    /// Deliberately generic — no knowledge of any particular route. A game calls whichever platform
    /// endpoint it needs and the platform's own authorization decides what it may do. Anything
    /// endpoint-specific belongs in the game, or in a dedicated module once a second game needs the
    /// same thing.
    /// </para>
    /// </summary>
    public class BackendModule
    {
        private readonly HttpClient _httpClient;

        public BackendModule(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// POSTs a JSON body to a platform route. <paramref name="path"/> is relative and must start
        /// with '/', e.g. "/ai/complete".
        /// </summary>
        public Task<HttpResponse> Post(string path, string jsonBody)
        {
            if (!IsRelativePath(path))
            {
                return Task.FromResult(HttpResponse.Fail(
                    "Endpoint must be a relative path starting with '/'.", 0));
            }

            return _httpClient.Post(path, jsonBody ?? "{}");
        }

        public Task<HttpResponse> Get(string path)
        {
            if (!IsRelativePath(path))
            {
                return Task.FromResult(HttpResponse.Fail(
                    "Endpoint must be a relative path starting with '/'.", 0));
            }

            return _httpClient.Get(path);
        }

        /// <summary>
        /// Rejects absolute URLs as well as empty paths. The base URL comes from the SDK config, so
        /// letting a caller pass a full URL would send the player's token to a host the platform
        /// never approved.
        /// </summary>
        private static bool IsRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/", StringComparison.Ordinal))
            {
                return false;
            }

            // "//host/path" is protocol-relative and resolves to a different origin.
            return !path.StartsWith("//", StringComparison.Ordinal);
        }
    }
}
