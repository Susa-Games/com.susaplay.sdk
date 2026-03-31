using System;
using UnityEngine;

namespace susaplay.SDK
{
    [Serializable]
    public class LoadResult
    {
        public bool Success;
        public string Data;
        public int Version;
        public string Error;
        public static LoadResult Fail(string error)
        {
            return new LoadResult
            {
                Success = false,
                Error = error
            };
        }
    }
}