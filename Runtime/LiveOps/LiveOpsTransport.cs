using System;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace susaplay.SDK
{
    internal interface ILiveOpsTransport
    {
        Task<LiveOpsTransportResponse> GetAsync(string url);
    }

    internal sealed class LiveOpsTransportResponse
    {
        internal bool Success;
        internal long StatusCode;
        internal string Data;
        internal string Error;
    }

    internal sealed class UnityLiveOpsTransport : ILiveOpsTransport
    {
        public async Task<LiveOpsTransportResponse> GetAsync(string url)
        {
            try
            {
                using (var request = UnityWebRequest.Get(url))
                {
                    var operation = request.SendWebRequest();
                    var completion = new TaskCompletionSource<bool>();
                    operation.completed += _ => completion.TrySetResult(true);
                    if (operation.isDone)
                    {
                        completion.TrySetResult(true);
                    }
                    await completion.Task;
                    return new LiveOpsTransportResponse
                    {
                        Success = request.result == UnityWebRequest.Result.Success,
                        StatusCode = request.responseCode,
                        Data = request.downloadHandler == null ? string.Empty : request.downloadHandler.text,
                        Error = request.error ?? string.Empty
                    };
                }
            }
            catch (Exception exception)
            {
                return new LiveOpsTransportResponse
                {
                    Success = false,
                    StatusCode = 0,
                    Data = string.Empty,
                    Error = exception.Message
                };
            }
        }
    }
}
