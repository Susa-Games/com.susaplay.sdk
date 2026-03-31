using UnityEngine;
using UnityEngine.Networking;
using System.Threading.Tasks;

namespace susaplay.SDK
{
    public class HttpClient
    {
        private SDKConfig _config;
        private TokenManager _tokenManager;
        public HttpClient(SDKConfig config, TokenManager tokenManager)
        {
            _config = config;
            _tokenManager = tokenManager;
        }

        public async Task<HttpResponse> Post(string endpoint, string body)
        {
            var token = await _tokenManager.GetTokenAsync();
            var url = _config.ApiBaseUrl + endpoint;
            var request = new UnityWebRequest(url, "POST");
            var bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + token);
            await request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                return new HttpResponse { Success = true, Data = request.downloadHandler.text };
            }
            else
            {
                return HttpResponse.Fail(request.error);
            }
        }

        public async Task<HttpResponse> Get(string endpoint)
        {
            var token = await _tokenManager.GetTokenAsync();
            var url = _config.ApiBaseUrl + endpoint;
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("Authorization", "Bearer " + token);
            await request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                return new HttpResponse { Success = true, Data = request.downloadHandler.text };
            }
            else
            {
                return HttpResponse.Fail(request.error);
            }
        }
    }
}