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

        public static HttpResponse Fail(string error)
        {
            return new HttpResponse
            {
                Success = false,
                Error = error
            };
        }
    }
}