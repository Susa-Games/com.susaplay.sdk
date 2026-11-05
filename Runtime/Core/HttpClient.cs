using System;
using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;

namespace susaplay.SDK
{
    public class HttpClient
    {
        private SDKConfig _config;
        private TokenManager _tokenManager;
        private AppCheckManager _appCheckManager;

        public HttpClient(SDKConfig config, TokenManager tokenManager, AppCheckManager appCheckManager = null)
        {
            _config = config;
            _tokenManager = tokenManager;
            _appCheckManager = appCheckManager;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Answers requests in the Editor in place of the network. Set by the Editor Simulator;
        /// while null, requests go to <see cref="SDKConfig.ApiBaseUrl"/> as they do in a build.
        /// Like <see cref="WebGLBridge.EditorOutbound"/>, it outlives a Play session.
        /// </summary>
        internal static Func<EditorHttpRequest, Task<EditorHttpAnswer>> EditorTransport;
#endif

        // A stalled mobile request would otherwise never finish, and every caller awaiting it
        // (a cloud-save load the game waits on, say) would hang with it.
        private const int RequestTimeoutSeconds = 20;

        public Task<HttpResponse> Post(string endpoint, string body)
        {
            return Send("POST", endpoint, body);
        }

        public Task<HttpResponse> Get(string endpoint)
        {
            return Send("GET", endpoint, null);
        }

        private async Task<HttpResponse> Send(string method, string endpoint, string body)
        {
            var tokenTask = _tokenManager.GetTokenAsync();
#if UNITY_EDITOR
            if (EditorTransport != null)
            {
                return await SendThroughEditor(method, endpoint, body, await tokenTask);
            }
#endif
            // Asked together, not one after the other: each is a round trip to the shell, and on
            // a cold start the second used to wait for the first before the request could leave.
            var appCheckTask = _appCheckManager != null ? _appCheckManager.GetTokenAsync() : Task.FromResult<string>(null);
            var token = await tokenTask;
            var appCheckToken = await appCheckTask;

            var url = _config.ApiBaseUrl + endpoint;
            // Disposed so each request's native buffers are released; a session of regular
            // cloud saves otherwise holds on to every one of them.
            using (var request = new UnityWebRequest(url, method))
            {
                if (body != null)
                {
                    request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.downloadHandler = new DownloadHandlerBuffer();
                request.timeout = RequestTimeoutSeconds;
                request.SetRequestHeader("Authorization", "Bearer " + token);
                if (!string.IsNullOrEmpty(appCheckToken))
                    request.SetRequestHeader("X-Firebase-AppCheck", appCheckToken);

                await request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    return new HttpResponse { Success = true, Data = request.downloadHandler.text, StatusCode = request.responseCode };
                }
                return HttpResponse.Fail(DescribeError(request), request.responseCode);
            }
        }

        /// <summary>Combines the transport error with the response body. On an HTTP error
        /// UnityWebRequest.error is only "HTTP/1.1 409 Conflict", which hides the backend's
        /// error code (VERSION_CONFLICT and friends) and makes save failures unreadable in logs.
        /// </summary>
        private static string DescribeError(UnityWebRequest request)
        {
            return DescribeError(request.error, request.downloadHandler != null ? request.downloadHandler.text : null);
        }

        private static string DescribeError(string error, string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return error;
            }
            const int maxBodyChars = 512;
            if (body.Length > maxBodyChars)
            {
                body = body.Substring(0, maxBodyChars) + "…";
            }
            return error + " — " + body;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Builds the HttpResponse a UnityWebRequest would have produced for the same answer, so
        /// a module cannot tell the simulator from the platform: only 2xx succeeds, an HTTP error
        /// reads "HTTP/1.1 409 Conflict — {body}", and a network failure has status 0.
        ///
        /// No App Check token is requested. The simulator does not verify one, and App Check is
        /// among what only a WebGL build on the real platform can test.
        /// </summary>
        private static async Task<HttpResponse> SendThroughEditor(string method, string endpoint, string body, string token)
        {
            EditorHttpAnswer answer;
            try
            {
                answer = await EditorTransport(new EditorHttpRequest
                {
                    Method = method,
                    Endpoint = endpoint,
                    Body = body,
                    Authorization = "Bearer " + token,
                });
            }
            catch (Exception exception)
            {
                // A bug in the simulator must reach the game as a failed request, the way any
                // transport failure does, not as an exception no real build would ever throw.
                Logger.Error("SusaPlay Simulator failed to answer " + method + " " + endpoint + ": " + exception);
                return HttpResponse.Fail("Simulator error: " + exception.Message, 0);
            }

            if (answer == null)
            {
                return HttpResponse.Fail("Simulator returned no answer", 0);
            }
            if (!string.IsNullOrEmpty(answer.NetworkError))
            {
                return HttpResponse.Fail(answer.NetworkError, 0);
            }
            if (answer.StatusCode >= 200 && answer.StatusCode < 300)
            {
                return new HttpResponse { Success = true, Data = answer.Body, StatusCode = answer.StatusCode };
            }
            return HttpResponse.Fail(DescribeError(StatusLine(answer.StatusCode), answer.Body), answer.StatusCode);
        }

        private static string StatusLine(long statusCode)
        {
            string reason;
            switch (statusCode)
            {
                case 400: reason = "Bad Request"; break;
                case 401: reason = "Unauthorized"; break;
                case 403: reason = "Forbidden"; break;
                case 404: reason = "Not Found"; break;
                case 409: reason = "Conflict"; break;
                case 413: reason = "Payload Too Large"; break;
                case 429: reason = "Too Many Requests"; break;
                case 500: reason = "Internal Server Error"; break;
                case 502: reason = "Bad Gateway"; break;
                case 503: reason = "Service Unavailable"; break;
                default: return "HTTP/1.1 " + statusCode;
            }
            return "HTTP/1.1 " + statusCode + " " + reason;
        }
#endif
    }

#if UNITY_EDITOR
    /// <summary>A request the SDK hands to <see cref="HttpClient.EditorTransport"/>.</summary>
    internal sealed class EditorHttpRequest
    {
        public string Method;
        /// <summary>The path the SDK appends to ApiBaseUrl, such as "/save/write".</summary>
        public string Endpoint;
        /// <summary>Null for GET.</summary>
        public string Body;
        /// <summary>"Bearer {token}", exactly what the platform would receive.</summary>
        public string Authorization;
    }

    /// <summary>What the Editor Simulator answers. Set NetworkError for a request that never got a response.</summary>
    internal sealed class EditorHttpAnswer
    {
        public long StatusCode;
        public string Body;
        public string NetworkError;
    }
#endif
}
