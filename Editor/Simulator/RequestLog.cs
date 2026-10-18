using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace susaplay.SDK.Editor.Simulator
{
    internal sealed class RequestLogEntry
    {
        public DateTime Time;
        /// <summary>"→ SDK_INIT" for a message from the game, "← SDK_READY" for an answer, or "POST /save/write".</summary>
        public string Name;
        /// <summary>The HTTP status, or 0 for a bridge message.</summary>
        public long Status;
        public double DurationMs;
        public string ErrorCode;
        public string ErrorMessage;
        public bool IsError;
        /// <summary>The request or message body, for the detail view.</summary>
        public string Detail;

        public string Describe()
        {
            var sb = new StringBuilder();
            sb.Append(Time.ToLocalTime().ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append("  ").Append(Name);
            if (Status != 0)
            {
                sb.Append("  ").Append(Status);
            }
            if (DurationMs > 0)
            {
                sb.Append("  ").Append(DurationMs.ToString("0", CultureInfo.InvariantCulture)).Append(" ms");
            }
            if (!string.IsNullOrEmpty(ErrorCode))
            {
                sb.Append("  ").Append(ErrorCode);
            }
            if (!string.IsNullOrEmpty(ErrorMessage))
            {
                sb.Append(" — ").Append(ErrorMessage);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Every message and request of the session, with the platform's verdict. It stands in for
    /// every server-side record: a developer checks an integration here instead of a dashboard.
    /// </summary>
    internal sealed class RequestLog
    {
        public const int Capacity = 500;

        private readonly List<RequestLogEntry> _entries = new List<RequestLogEntry>();

        public event Action Changed;

        public IReadOnlyList<RequestLogEntry> Entries => _entries;

        public void Add(RequestLogEntry entry)
        {
            _entries.Add(entry);
            if (_entries.Count > Capacity)
            {
                _entries.RemoveRange(0, _entries.Count - Capacity);
            }
            Changed?.Invoke();
        }

        public void Clear()
        {
            _entries.Clear();
            Changed?.Invoke();
        }

        public string ToText(bool errorsOnly)
        {
            var sb = new StringBuilder();
            foreach (var entry in _entries)
            {
                if (errorsOnly && !entry.IsError) continue;
                sb.AppendLine(entry.Describe());
            }
            return sb.ToString();
        }
    }
}
