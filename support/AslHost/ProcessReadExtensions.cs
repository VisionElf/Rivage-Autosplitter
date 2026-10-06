using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Rivage.AslHost;

// Read-only subset of LiveSplit.ComponentUtil's Process extension API.
public static class ProcessReadExtensions
{
    private static readonly ConditionalWeakTable<Process, ProcessMemory> Readers = new();
    public static bool ReadBytes(this Process process, IntPtr address, int count, out byte[]? value)
    {
        if (count < 0 || count > 1024 * 1024) throw new ArgumentOutOfRangeException(nameof(count), "Direct memory reads are limited to 1 MB.");
        value = null;
        if (count == 0) { value = []; return true; }
        try { value = Readers.GetValue(process, p => new ProcessMemory(p)).Read(address.ToInt64(), count); return true; }
        catch (IOException) { return false; }
    }
    public static byte[]? ReadBytes(this Process process, IntPtr address, int count)
        => process.ReadBytes(address, count, out var value) ? value : null;
    public static bool ReadValue<T>(this Process process, IntPtr address, out T value) where T : struct
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>()) throw new NotSupportedException("Memory values cannot contain managed references.");
        value = default;
        if (!process.ReadBytes(address, Unsafe.SizeOf<T>(), out var bytes)) return false;
        value = MemoryMarshal.Read<T>(bytes!);
        return true;
    }
    public static T ReadValue<T>(this Process process, IntPtr address, T default_ = default) where T : struct
        => process.ReadValue<T>(address, out var value) ? value : default_;
    internal static void Release(Process process)
    {
        if (!Readers.TryGetValue(process, out var reader)) return;
        Readers.Remove(process);
        reader.Dispose();
    }
}
