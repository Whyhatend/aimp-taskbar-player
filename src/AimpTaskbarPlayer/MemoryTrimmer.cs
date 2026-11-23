using System;
using System.Threading.Tasks;

namespace AimpTaskbarPlayer;

/// <summary>
/// The app is idle almost all the time. Collect managed garbage and return unused pages
/// to the OS so the taskbar widget does not sit on tens of megabytes of working set.
/// </summary>
internal static class MemoryTrimmer
{
    public static Task TrimAsync() => Task.Run(() =>
    {
        GC.Collect(2, GCCollectionMode.Optimized, blocking: true, compacting: false);
        Native.EmptyWorkingSet(Native.GetCurrentProcess());
    });
}
