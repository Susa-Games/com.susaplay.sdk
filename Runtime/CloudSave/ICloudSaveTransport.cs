using System.Threading.Tasks;

namespace susaplay.SDK
{
    /// <summary>Request seam for cloud save calls, mirroring ILiveOpsTransport. Exists so the
    /// write-serialisation logic in CloudSaveModule can be tested without a live backend.</summary>
    internal interface ICloudSaveTransport
    {
        Task<HttpResponse> Post(string endpoint, string body);
        Task<HttpResponse> Get(string endpoint);
    }

    internal sealed class HttpClientCloudSaveTransport : ICloudSaveTransport
    {
        private readonly HttpClient _httpClient;

        public HttpClientCloudSaveTransport(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public Task<HttpResponse> Post(string endpoint, string body)
        {
            return _httpClient.Post(endpoint, body);
        }

        public Task<HttpResponse> Get(string endpoint)
        {
            return _httpClient.Get(endpoint);
        }
    }
}
