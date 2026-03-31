using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace susaplay.SDK
{
    public class CloudSaveModule
    {
        private HttpClient _httpClient;
        private Dictionary<string, int> _slotVersions = new Dictionary<string, int>();
        private string _gameId;
        public CloudSaveModule(HttpClient httpClient, string gameId)
        {
            _httpClient = httpClient;
            _gameId = gameId;
        }

        public async Task<SaveResult> Save(string slot, string data)
        {
            var version = _slotVersions.ContainsKey(slot) ? _slotVersions[slot] : 0;
            var body = "{\"gameId\":\"" + _gameId + "\",\"slot\":\"" + slot + "\",\"data\":" + data + ",\"version\":" + version + "}";

            var response = await _httpClient.Post("/save/write", body);
            if (!response.Success)
            {
                return SaveResult.Fail(response.Error);
            }
            var envelope = JsonUtility.FromJson<SaveWriteEnvelope>(response.Data);
            if (envelope == null || !envelope.success || envelope.data == null)
            {
                return SaveResult.Fail("Malformed save response");
            }

            var saveResult = new SaveResult
            {
                Success = true,
                Version = envelope.data.version,
                Error = null
            };
            _slotVersions[slot] = saveResult.Version;
            return saveResult;
        }

        public async Task<LoadResult> Load(string slot)
        {
            var response = await _httpClient.Get("/save/read?gameId=" + _gameId + "&slot=" + slot);
            if (!response.Success)
            {
                return LoadResult.Fail(response.Error);
            }
            var envelope = JsonUtility.FromJson<SaveReadEnvelope>(response.Data);
            if (envelope == null || !envelope.success || envelope.data == null)
            {
                return LoadResult.Fail("Malformed load response");
            }

            var loadResult = new LoadResult
            {
                Success = true,
                Data = envelope.data.data,
                Version = envelope.data.version,
                Error = null
            };
            _slotVersions[slot] = loadResult.Version;
            return loadResult;
        }
    }

    [System.Serializable]
    class SaveWriteEnvelope
    {
        public bool success;
        public SaveWriteData data;
    }

    [System.Serializable]
    class SaveWriteData
    {
        public string slot;
        public int version;
    }

    [System.Serializable]
    class SaveReadEnvelope
    {
        public bool success;
        public SaveReadData data;
    }

    [System.Serializable]
    class SaveReadData
    {
        public string slot;
        public string data;
        public int version;
        public string savedAt;
    }
}
