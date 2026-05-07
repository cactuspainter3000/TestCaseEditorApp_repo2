using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TestCaseEditorApp.MVVM.Models;

namespace TestCaseEditorApp.Services
{
    /// <summary>
    /// Intelligent caching system for requirement analysis results.
    /// Caches analyses based on content hash and automatically invalidates when content changes.
    /// </summary>
    public sealed class RequirementAnalysisCache : IDisposable
    {
        public sealed class CacheEntry
        {
            public RequirementAnalysis Analysis { get; init; } = new();
            public string ContentHash { get; init; } = string.Empty;
            public DateTime CreatedAt { get; init; }
            public DateTime LastAccessedAt { get; set; }
            public int AccessCount { get; set; }
            public TimeSpan AnalysisDuration { get; init; }
        }

        public sealed class CacheStatistics
        {
            public int TotalEntries { get; init; }
            public int CacheHits { get; init; }
            public int CacheMisses { get; init; }
            public double HitRate => TotalRequests == 0 ? 0 : (double)CacheHits / TotalRequests * 100;
            public int TotalRequests => CacheHits + CacheMisses;
            public TimeSpan TotalTimeSaved { get; init; }
            public long MemoryUsageBytes { get; init; }
            public DateTime LastCleanup { get; init; }
        }

        private readonly ConcurrentDictionary<string, CacheEntry> _cache;
        private readonly ILogger<RequirementAnalysisCache> _logger;
        private readonly int _maxCacheSize;
        private readonly TimeSpan _maxAge;
        private readonly Timer _cleanupTimer;

        private int _cacheHits = 0;
        private int _cacheMisses = 0;
        private TimeSpan _totalTimeSaved = TimeSpan.Zero;
        private DateTime _lastCleanup = DateTime.UtcNow;

        public RequirementAnalysisCache(
            ILogger<RequirementAnalysisCache> logger,
            int maxCacheSize = 1000,
            TimeSpan? maxAge = null,
            TimeSpan? cleanupInterval = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _maxCacheSize = maxCacheSize;
            _maxAge = maxAge ?? TimeSpan.FromHours(24); // Default: 24 hours
            _cache = new ConcurrentDictionary<string, CacheEntry>();

            // Setup periodic cleanup (default: every hour)
            var interval = cleanupInterval ?? TimeSpan.FromHours(1);
            _cleanupTimer = new Timer(
                _ => PerformCleanup(), 
                null, 
                interval, 
                interval);

            _logger.LogInformation("[AnalysisCache] Initialized with maxSize={MaxSize}, maxAge={MaxAge}", 
                maxCacheSize, _maxAge);
        }

        /// <summary>
        /// Try to get a cached analysis result for the given requirement.
        /// Returns true if found and still valid, false otherwise.
        /// </summary>
        public bool TryGet(Requirement requirement, out RequirementAnalysis? analysis)
        {
            analysis = null;

            try
            {
                var contentHash = ComputeContentHash(requirement);
                var cacheKey = GetCacheKey(requirement.GlobalId, contentHash);

                if (_cache.TryGetValue(cacheKey, out var entry))
                {
                    // Check if entry is still valid
                    if (IsEntryValid(entry, contentHash))
                    {
                        // Update access statistics
                        entry.LastAccessedAt = DateTime.UtcNow;
                        entry.AccessCount++;
                        
                        analysis = CloneAnalysis(entry.Analysis);
                        Interlocked.Increment(ref _cacheHits);
                        
                        // Estimate time saved (assuming average analysis takes 3-10 seconds)
                        var timeSaved = entry.AnalysisDuration > TimeSpan.Zero ? 
                            entry.AnalysisDuration : TimeSpan.FromSeconds(5);
                        _totalTimeSaved = _totalTimeSaved.Add(timeSaved);

                        _logger.LogDebug("[AnalysisCache] Cache HIT for requirement {RequirementId}", 
                            requirement.GlobalId);
                        return true;
                    }
                    else
                    {
                        // Remove invalid entry
                        _cache.TryRemove(cacheKey, out _);
                        _logger.LogDebug("[AnalysisCache] Removed stale cache entry for requirement {RequirementId}", 
                            requirement.GlobalId);
                    }
                }

                Interlocked.Increment(ref _cacheMisses);
                _logger.LogDebug("[AnalysisCache] Cache MISS for requirement {RequirementId}", 
                    requirement.GlobalId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisCache] Error retrieving cache entry for requirement {RequirementId}", 
                    requirement?.GlobalId ?? "unknown");
                Interlocked.Increment(ref _cacheMisses);
                return false;
            }
        }

        /// <summary>
        /// Store an analysis result in the cache.
        /// </summary>
        public void Set(Requirement requirement, RequirementAnalysis analysis, TimeSpan analysisDuration)
        {
            try
            {
                if (analysis == null || !analysis.IsAnalyzed)
                {
                    _logger.LogDebug("[AnalysisCache] Skipping cache storage for failed analysis of requirement {RequirementId}",
                        requirement.GlobalId);
                    return;
                }

                var contentHash = ComputeContentHash(requirement);
                var cacheKey = GetCacheKey(requirement.GlobalId, contentHash);

                var entry = new CacheEntry
                {
                    Analysis = CloneAnalysis(analysis),
                    ContentHash = contentHash,
                    CreatedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow,
                    AccessCount = 0,
                    AnalysisDuration = analysisDuration
                };

                // Ensure we don't exceed cache size limits
                EnsureCacheSize();

                _cache[cacheKey] = entry;

                _logger.LogDebug("[AnalysisCache] Stored analysis for requirement {RequirementId} (hash: {Hash})", 
                    requirement.GlobalId, contentHash[..8]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisCache] Error storing cache entry for requirement {RequirementId}", 
                    requirement?.GlobalId ?? "unknown");
            }
        }

        /// <summary>
        /// Invalidate cache entry for a specific requirement (e.g., when content changes).
        /// </summary>
        public void Invalidate(string requirementGlobalId)
        {
            try
            {
                var keysToRemove = _cache.Keys
                    .Where(key => key.StartsWith($"{requirementGlobalId}:"))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _cache.TryRemove(key, out _);
                }

                if (keysToRemove.Any())
                {
                    _logger.LogDebug("[AnalysisCache] Invalidated {Count} cache entries for requirement {RequirementId}", 
                        keysToRemove.Count, requirementGlobalId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisCache] Error invalidating cache for requirement {RequirementId}", 
                    requirementGlobalId);
            }
        }

        /// <summary>
        /// Clear all cache entries.
        /// </summary>
        public void Clear()
        {
            var count = _cache.Count;
            _cache.Clear();
            _logger.LogInformation("[AnalysisCache] Cleared all {Count} cache entries", count);
        }

        /// <summary>
        /// Get current cache statistics.
        /// </summary>
        public CacheStatistics GetStatistics()
        {
            var memoryUsage = EstimateMemoryUsage();

            return new CacheStatistics
            {
                TotalEntries = _cache.Count,
                CacheHits = _cacheHits,
                CacheMisses = _cacheMisses,
                TotalTimeSaved = _totalTimeSaved,
                MemoryUsageBytes = memoryUsage,
                LastCleanup = _lastCleanup
            };
        }

        private string ComputeContentHash(Requirement requirement)
        {
            // Create a normalized representation of requirement content for hashing
            var contentBuilder = new StringBuilder();
            contentBuilder.Append(requirement.Description ?? "");
            contentBuilder.Append(requirement.Name ?? "");
            contentBuilder.Append(requirement.Item ?? "");

            // Include tables content
            if (requirement.Tables != null)
            {
                foreach (var table in requirement.Tables)
                {
                    contentBuilder.Append(table.EditableTitle ?? "");
                    if (table.Table != null)
                    {
                        foreach (var row in table.Table)
                        {
                            if (row != null)
                            {
                                foreach (var cell in row)
                                {
                                    contentBuilder.Append(cell ?? "");
                                }
                            }
                        }
                    }
                }
            }

            // Include loose content paragraphs
            if (requirement.LooseContent?.Paragraphs != null)
            {
                foreach (var paragraph in requirement.LooseContent.Paragraphs)
                {
                    contentBuilder.Append(paragraph ?? "");
                }
            }

            // Include loose content tables
            if (requirement.LooseContent?.Tables != null)
            {
                foreach (var looseTable in requirement.LooseContent.Tables)
                {
                    contentBuilder.Append(looseTable?.EditableTitle ?? "");
                    if (looseTable?.Rows != null)
                    {
                        foreach (var row in looseTable.Rows)
                        {
                            if (row != null)
                            {
                                foreach (var cell in row)
                                {
                                    contentBuilder.Append(cell ?? "");
                                }
                            }
                        }
                    }
                }
            }

            // Compute SHA256 hash
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(contentBuilder.ToString()));
            return Convert.ToBase64String(hashBytes);
        }

        private static string GetCacheKey(string requirementId, string contentHash)
        {
            return $"{requirementId}:{contentHash}";
        }

        private bool IsEntryValid(CacheEntry entry, string currentContentHash)
        {
            // Check content hash match
            if (entry.ContentHash != currentContentHash)
                return false;

            // Check age limit
            if (DateTime.UtcNow - entry.CreatedAt > _maxAge)
                return false;

            return true;
        }

        private RequirementAnalysis CloneAnalysis(RequirementAnalysis original)
        {
            try
            {
                // Deep clone using JSON serialization
                var json = JsonSerializer.Serialize(original);
                return JsonSerializer.Deserialize<RequirementAnalysis>(json) ?? new RequirementAnalysis();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisCache] Error cloning analysis, returning original");
                return original;
            }
        }

        private void EnsureCacheSize()
        {
            if (_cache.Count <= _maxCacheSize)
                return;

            try
            {
                // Remove oldest and least accessed entries
                var entriesToRemove = _cache
                    .OrderBy(kvp => kvp.Value.LastAccessedAt)
                    .ThenBy(kvp => kvp.Value.AccessCount)
                    .Take(_cache.Count - _maxCacheSize + 50) // Remove extra to avoid frequent cleanup
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in entriesToRemove)
                {
                    _cache.TryRemove(key, out _);
                }

                _logger.LogDebug("[AnalysisCache] Removed {Count} old entries to maintain cache size", 
                    entriesToRemove.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisCache] Error during cache size management");
            }
        }

        private void PerformCleanup()
        {
            try
            {
                var expiredEntries = _cache
                    .Where(kvp => DateTime.UtcNow - kvp.Value.CreatedAt > _maxAge)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredEntries)
                {
                    _cache.TryRemove(key, out _);
                }

                _lastCleanup = DateTime.UtcNow;

                if (expiredEntries.Any())
                {
                    _logger.LogDebug("[AnalysisCache] Cleanup removed {Count} expired entries", 
                        expiredEntries.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AnalysisCache] Error during cache cleanup");
            }
        }

        private long EstimateMemoryUsage()
        {
            try
            {
                // Rough estimation: each cache entry is approximately 2-5 KB
                return _cache.Count * 3500; // 3.5 KB average per entry
            }
            catch
            {
                return 0;
            }
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _cache.Clear();
            _logger.LogInformation("[AnalysisCache] Disposed cache with {Hits} hits, {Misses} misses, {TimeSaved}ms saved",
                _cacheHits, _cacheMisses, _totalTimeSaved.TotalMilliseconds);
        }
    }
}