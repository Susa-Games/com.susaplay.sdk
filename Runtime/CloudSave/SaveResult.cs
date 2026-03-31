using System;
using UnityEngine;


namespace susaplay.SDK
{
    [Serializable]
    public class SaveResult
    {
        public bool Success;
        public int Version;
        public string Error;
        public static SaveResult Fail(string error)
        {
            return new SaveResult
            {
                Success = false,
                Error = error
            };
        }
    }
}