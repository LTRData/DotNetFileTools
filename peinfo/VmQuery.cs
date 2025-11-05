using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace peinfo;

public static partial class VmQuery
{
    // Constants from WinNT.h
    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_FREE = 0x10000;

    private const uint MEM_PRIVATE = 0x20000;
    private const uint MEM_MAPPED = 0x40000;
    private const uint MEM_IMAGE = 0x1000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORY_BASIC_INFORMATION
    {
        public nint BaseAddress;          // PVOID
        public nint AllocationBase;       // PVOID
        public uint AllocationProtect;    // DWORD
        public nint RegionSize;           // SIZE_T
        public uint State;                // DWORD
        public uint Protect;              // DWORD
        public uint Type;                 // DWORD
    }

    // Queries the *current process*. For another process, use VirtualQueryEx.
#if NET7_0_OR_GREATER
    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint VirtualQuery(nint address, out MEMORY_BASIC_INFORMATION info, nuint length);
#else
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint VirtualQuery(nint address, out MEMORY_BASIC_INFORMATION info, nuint length);
#endif

    public sealed record Range(nint AllocationBase, nint Size, uint Type);

    /// <summary>
    /// Returns the contiguous allocation range (base + size) that contains the given address.
    /// This matches a mapped view/section boundary (AllocationBase) rather than just a single protection region.
    /// </summary>
    public static bool TryGetAllocationRange(nint anyAddressInside, [NotNullWhen(true)] out Range? range)
    {
        range = default;

        if (VirtualQuery(anyAddressInside, out var mbi0, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
        {
            return false;
        }

        // Optional: ensure it's actually mapped (MEM_MAPPED or MEM_IMAGE). Remove this check if you also want MEM_PRIVATE.
        if ((mbi0.Type != MEM_MAPPED) && (mbi0.Type != MEM_IMAGE) && (mbi0.State != MEM_COMMIT && mbi0.State != MEM_RESERVE))
        {
            return false;
        }

        nint allocBase = mbi0.AllocationBase;
        uint memType = mbi0.Type;

        // Walk forward from the allocation base and accumulate contiguous regions
        nint cursor = allocBase;
        nint total = 0;

        for (; ; )
        {
            if (VirtualQuery(cursor, out var mbi, (nuint)Marshal.SizeOf<MEMORY_BASIC_INFORMATION>()) == 0)
            {
                break;
            }

            // Stop when we leave this allocation (AllocationBase changes) or hit free memory
            if (mbi.AllocationBase != allocBase || mbi.State == MEM_FREE)
            {
                break;
            }

            // For mapped sections, regions can differ in Protect/State; that’s fine. We just sum RegionSize.
            total += mbi.RegionSize;
            cursor += (nint)mbi.RegionSize;
        }

        if (total == 0)
        {
            return false;
        }

        range = new Range(allocBase, total, memType);
        return true;
    }
}
