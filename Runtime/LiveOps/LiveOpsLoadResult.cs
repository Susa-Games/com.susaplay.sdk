namespace susaplay.SDK
{
    public enum LiveOpsLoadStatus
    {
        Success,
        Cached,
        Degraded,
        NotConfigured,
        Failed
    }

    public sealed class LiveOpsLoadResult
    {
        internal LiveOpsLoadResult(
            LiveOpsLoadStatus status,
            LiveOpsSnapshot snapshot,
            string error,
            bool isFromCache)
        {
            Status = status;
            Snapshot = snapshot ?? LiveOpsSnapshot.Empty();
            Error = error ?? string.Empty;
            IsFromCache = isFromCache;
        }

        public LiveOpsLoadStatus Status { get; }
        public LiveOpsSnapshot Snapshot { get; }
        public string Error { get; }
        public bool IsFromCache { get; }
        public bool IsDegraded => Status == LiveOpsLoadStatus.Degraded;
        public bool Success => Status != LiveOpsLoadStatus.Failed;
    }
}
