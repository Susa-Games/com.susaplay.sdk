using System;
using UnityEngine;

namespace susaplay.SDK
{
    [Serializable]
    public class HttpResponse
    {
        public bool Success;
        public string Data;
        public string Error;
        public long StatusCode;

        public static HttpResponse Fail(string error, long statusCode = 0)
        {
            return new HttpResponse
            {
                Success = false,
                Error = error,
                StatusCode = statusCode
            };
        }
    }
}