namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class SystemCacheMetrics
{
    private long _l1Hits;
    private long _l2Hits;
    private long _misses;
    private long _sets;
    private long _removes;
    private long _prefixInvalidations;
    private long _prefixInvalidatedKeys;
    private long _tagInvalidations;
    private long _tagInvalidatedKeys;
    private long _stampedeWaits;
    private long _stampedePrevented;
    private long _stampedeTimeouts;
    private long _factoryFailures;
    private long _cacheErrors;

    public void RecordL1Hit() => Interlocked.Increment(ref _l1Hits);

    public void RecordL2Hit() => Interlocked.Increment(ref _l2Hits);

    public void RecordMiss() => Interlocked.Increment(ref _misses);

    public void RecordSet() => Interlocked.Increment(ref _sets);

    public void RecordRemove() => Interlocked.Increment(ref _removes);

    public void RecordPrefixInvalidation(int removedKeys)
    {
        Interlocked.Increment(ref _prefixInvalidations);
        Interlocked.Add(ref _prefixInvalidatedKeys, Math.Max(0, removedKeys));
    }

    public void RecordTagInvalidation(int removedKeys)
    {
        Interlocked.Increment(ref _tagInvalidations);
        Interlocked.Add(ref _tagInvalidatedKeys, Math.Max(0, removedKeys));
    }

    public void RecordStampedeWait() => Interlocked.Increment(ref _stampedeWaits);

    public void RecordStampedePrevented() => Interlocked.Increment(ref _stampedePrevented);

    public void RecordStampedeTimeout() => Interlocked.Increment(ref _stampedeTimeouts);

    public void RecordFactoryFailure() => Interlocked.Increment(ref _factoryFailures);

    public void RecordCacheError() => Interlocked.Increment(ref _cacheErrors);

    public SystemCacheMetricsSnapshot Snapshot() => new(
        L1Hits: Interlocked.Read(ref _l1Hits),
        L2Hits: Interlocked.Read(ref _l2Hits),
        Misses: Interlocked.Read(ref _misses),
        Sets: Interlocked.Read(ref _sets),
        Removes: Interlocked.Read(ref _removes),
        PrefixInvalidations: Interlocked.Read(ref _prefixInvalidations),
        PrefixInvalidatedKeys: Interlocked.Read(ref _prefixInvalidatedKeys),
        TagInvalidations: Interlocked.Read(ref _tagInvalidations),
        TagInvalidatedKeys: Interlocked.Read(ref _tagInvalidatedKeys),
        StampedeWaits: Interlocked.Read(ref _stampedeWaits),
        StampedePrevented: Interlocked.Read(ref _stampedePrevented),
        StampedeTimeouts: Interlocked.Read(ref _stampedeTimeouts),
        FactoryFailures: Interlocked.Read(ref _factoryFailures),
        CacheErrors: Interlocked.Read(ref _cacheErrors));
}

public sealed record SystemCacheMetricsSnapshot(
    long L1Hits,
    long L2Hits,
    long Misses,
    long Sets,
    long Removes,
    long PrefixInvalidations,
    long PrefixInvalidatedKeys,
    long TagInvalidations,
    long TagInvalidatedKeys,
    long StampedeWaits,
    long StampedePrevented,
    long StampedeTimeouts,
    long FactoryFailures,
    long CacheErrors);
