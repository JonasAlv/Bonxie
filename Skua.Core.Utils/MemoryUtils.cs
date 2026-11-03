using System;

namespace Skua.Core.Utils;

public static class MemoryUtils
{
    /// <summary>
    /// Forces a compacting Garbage Collection pass to reduce managed heap footprint.
    /// </summary>
    public static void TrimWorkingSet()
    {
        try
        {
            System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: false, compacting: true);
        }
        catch
        {
        }
    }
}
