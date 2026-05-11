using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace susaplay.SDK
{
    public class ApiModule
    {
        private const int RequestTimeoutMs = 10000;
        private readonly Dictionary<string, TaskCompletionSource<ApiResult>> _pending =
            new Dictionary<string, TaskCompletionSource<ApiResult>>();

        public ApiModule()
        {
            WebGLBridge.OnMessageReceived += HandleBridgeMessage;
        }

        public Task<ApiResult> Get(string endpoint)
        {
            return Request("GET", endpoint, null);
        }

        public Task<ApiResult> Get(string endpoint, Dictionary<string, string> data)
        {
            return Request("GET", endpoint, BuildStringDictionaryJson(data));
        }

        public Task<ApiResult> Post(string endpoint, string dataJson)
        {
            return Request("POST", endpoint, dataJson);
        }

        public async Task<ApiResult> Request(string method, string endpoint, string dataJson = null)
        {
            if (string.IsNullOrWhiteSpace(endpoint) || !endpoint.StartsWith("/"))
            {
                return ApiResult.Fail("INVALID_ARGUMENT", "Endpoint must be a relative path.");
            }

            var requestId = Guid.NewGuid().ToString("N");
            var tcs = new TaskCompletionSource<ApiResult>();
            _pending[requestId] = tcs;

            var payload = BuildBridgePayload(requestId, method, endpoint, dataJson);
            WebGLBridge.SendMessage(new BridgeMessage
            {
                type = "SDK_API_REQUEST",
                payload = payload
            });

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(RequestTimeoutMs));
            if (completed != tcs.Task)
            {
                _pending.Remove(requestId);
                return ApiResult.Fail("TIMEOUT", "Partner API request timed out.");
            }

            return await tcs.Task;
        }

        private void HandleBridgeMessage(string json)
        {
            if (string.IsNullOrEmpty(json) || !json.Contains("\"SDK_API_RESPONSE\""))
            {
                return;
            }

            var requestId = ExtractJsonString(json, "requestId");
            if (string.IsNullOrEmpty(requestId) || !_pending.TryGetValue(requestId, out var tcs))
            {
                return;
            }

            _pending.Remove(requestId);
            var success = ExtractJsonBool(json, "success");
            if (success)
            {
                tcs.SetResult(new ApiResult
                {
                    Success = true,
                    Data = ExtractJsonValue(json, "data"),
                    ErrorCode = null,
                    ErrorMessage = null
                });
                return;
            }

            var errorJson = ExtractJsonValue(json, "error");
            tcs.SetResult(ApiResult.Fail(
                ExtractJsonString(errorJson, "code") ?? "REQUEST_FAILED",
                ExtractJsonString(errorJson, "message") ?? errorJson ?? "Partner API request failed."));
        }

        private static string BuildBridgePayload(string requestId, string method, string endpoint, string dataJson)
        {
            var sb = new StringBuilder();
            sb.Append("{\"target\":\"partner\",\"requestId\":");
            AppendJsonString(sb, requestId);
            sb.Append(",\"method\":");
            AppendJsonString(sb, string.IsNullOrEmpty(method) ? "GET" : method.ToUpperInvariant());
            sb.Append(",\"endpoint\":");
            AppendJsonString(sb, endpoint);
            if (!string.IsNullOrWhiteSpace(dataJson))
            {
                sb.Append(",\"body\":");
                sb.Append(dataJson);
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static string BuildStringDictionaryJson(Dictionary<string, string> data)
        {
            if (data == null || data.Count == 0)
            {
                return null;
            }

            var sb = new StringBuilder();
            sb.Append("{");
            var first = true;
            foreach (var item in data)
            {
                if (!first)
                {
                    sb.Append(",");
                }
                first = false;
                AppendJsonString(sb, item.Key);
                sb.Append(":");
                AppendJsonString(sb, item.Value);
            }
            sb.Append("}");
            return sb.ToString();
        }

        private static void AppendJsonString(StringBuilder sb, string value)
        {
            sb.Append("\"");
            var raw = value ?? "";
            for (var i = 0; i < raw.Length; i++)
            {
                var ch = raw[i];
                switch (ch)
                {
                    case '\\':
                        sb.Append("\\\\");
                        break;
                    case '"':
                        sb.Append("\\\"");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    default:
                        sb.Append(ch);
                        break;
                }
            }
            sb.Append("\"");
        }

        private static string ExtractJsonString(string json, string key)
        {
            var value = ExtractJsonValue(json, key);
            if (string.IsNullOrEmpty(value) || value.Length < 2 || value[0] != '"')
            {
                return null;
            }
            return value.Substring(1, value.Length - 2)
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }

        private static bool ExtractJsonBool(string json, string key)
        {
            var value = ExtractJsonValue(json, key);
            return value == "true";
        }

        private static string ExtractJsonValue(string json, string key)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            var marker = "\"" + key + "\":";
            var start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            start += marker.Length;
            while (start < json.Length && char.IsWhiteSpace(json[start]))
            {
                start++;
            }

            var end = FindJsonValueEnd(json, start);
            return end > start ? json.Substring(start, end - start) : null;
        }

        private static int FindJsonValueEnd(string json, int start)
        {
            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = start; i < json.Length; i++)
            {
                var ch = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                    continue;
                }
                if (ch == '{' || ch == '[')
                {
                    depth++;
                    continue;
                }
                if (ch == '}' || ch == ']')
                {
                    if (depth == 0)
                    {
                        return i;
                    }
                    depth--;
                    continue;
                }
                if (depth == 0 && ch == ',')
                {
                    return i;
                }
            }
            return json.Length;
        }

    }

    [Serializable]
    public class ApiResult
    {
        public bool Success;
        public string Data;
        public string ErrorCode;
        public string ErrorMessage;

        public static ApiResult Fail(string code, string message)
        {
            return new ApiResult
            {
                Success = false,
                ErrorCode = code,
                ErrorMessage = message
            };
        }
    }
}
