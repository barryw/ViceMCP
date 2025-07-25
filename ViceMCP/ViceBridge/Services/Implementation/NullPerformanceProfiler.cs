﻿using System.Collections.Immutable;
using ViceMCP.ViceBridge.Services.Abstract;

namespace ViceMCP.ViceBridge.Services.Implementation
{
    /// <summary>
    /// Non logging performance profiler. Typically used in Relase version.
    /// </summary>
    /// <threadsafety>Thread safe.</threadsafety>
    internal class NullPerformanceProfiler : IPerformanceProfiler
    {
        /// <inheritdoc/>
        public bool IsEnabled => false;
        /// <inheritdoc/>
        public long Ticks => 0;
        /// <inheritdoc/>
        public ImmutableArray<PerformanceEvent> Events => ImmutableArray<PerformanceEvent>.Empty;
        /// <inheritdoc/>
        public void Add(PerformanceEvent e)
        { }
        /// <inheritdoc/>
        public void Clear()
        { }
    }
}
