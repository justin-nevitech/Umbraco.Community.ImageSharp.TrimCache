namespace Umbraco.Community.ImageSharp.TrimCache.Core;

/// <summary>
/// Storage-agnostic representation of a single cached image variant.
/// Deliberately contains no Azure (or any other provider) types so the trim
/// logic can be tested without a storage backend.
/// </summary>
public sealed record CacheEntry(
    string Name,
    DateTimeOffset LastModified,
    long SizeBytes);
