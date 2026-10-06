using System;
using System.IO;
using UnityEngine;

namespace susaplay.SDK.Editor.Simulator
{
    internal enum PurchaseOutcome
    {
        /// <summary>Wait for the developer to answer in the Simulator window.</summary>
        Ask,
        Paid,
        Canceled,
        /// <summary>The player closed the checkout.</summary>
        Closed,
    }

    internal enum AdOutcome
    {
        /// <summary>Wait for the developer to answer in the Simulator window.</summary>
        Ask,
        Complete,
        Skip,
        NoAd,
    }

    /// <summary>
    /// Per-user choices, in UserSettings/ — Unity's standard ignore file keeps that folder out of
    /// version control, so one developer's Guest switch does not change a teammate's Play.
    /// </summary>
    [Serializable]
    internal sealed class SimulatorSettings
    {
        public const string DefaultPath = "UserSettings/SusaPlaySimulator.json";

        /// <summary>Applied at the next SDK_INIT, as a real session decides it at launch.</summary>
        public bool guest;

        // Answered automatically by default, so a game works in Play Mode without the window open.
        public PurchaseOutcome purchaseOutcome = PurchaseOutcome.Paid;
        public AdOutcome adOutcome = AdOutcome.Complete;

        public static SimulatorSettings Load(string path = DefaultPath)
        {
            try
            {
                if (File.Exists(path))
                {
                    return JsonUtility.FromJson<SimulatorSettings>(File.ReadAllText(path)) ?? new SimulatorSettings();
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[SusaPlay Simulator] Could not read " + path + ": " + exception.Message + ". Using the defaults.");
            }
            return new SimulatorSettings();
        }

        public void Save(string path = DefaultPath)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, JsonUtility.ToJson(this, true));
        }
    }
}
