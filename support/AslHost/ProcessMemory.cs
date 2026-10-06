using System.Diagnostics;
using System.Dynamic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Rivage.AslHost;

public sealed class ProcessMemory : IDisposable
{
    [DllImport("kernel32.dll", SetLastError = true)] private static extern SafeProcessHandle OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ReadProcessMemory(SafeProcessHandle process, nint address, byte[] buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool IsWow64Process(SafeProcessHandle process, out bool wow64);
    private readonly SafeProcessHandle handle;
    private readonly bool is32Bit;
    public ProcessMemory(Process process)
    {
        handle = OpenProcess(0x0010 | 0x1000, false, process.Id);
        if (handle.IsInvalid) throw new Exception("Cannot read the game process. Check that both applications use the same privilege level.");
        if (!IsWow64Process(handle, out is32Bit)) { handle.Dispose(); throw new Exception("Cannot determine the game pointer size."); }
    }
    internal byte[] Read(long address, int size)
    {
        var buffer = new byte[size];
        if (!ReadProcessMemory(handle, (nint)address, buffer, (nuint)size, out var read) || read != (nuint)size)
            throw new IOException("Game memory is not readable yet. Waiting for valid pointers.");
        return buffer;
    }
    public ExpandoObject Snapshot(StateDescriptor state, List<ProcessModule> modules)
    {
        IDictionary<string, object?> result = new ExpandoObject();
        foreach (var field in state.Fields)
        {
            var module = field.Module == null ? modules[0] : modules.FirstOrDefault(m => string.Equals(m.ModuleName, field.Module, StringComparison.OrdinalIgnoreCase))
                ?? throw new IOException($"Game module is unavailable: {field.Module}.");
            var address = module.BaseAddress.ToInt64();
            for (var i = 0; i < field.Offsets.Length; i++)
            {
                address = checked(address + field.Offsets[i]);
                if (i + 1 < field.Offsets.Length) address = is32Bit ? BitConverter.ToUInt32(Read(address, 4)) : BitConverter.ToInt64(Read(address, 8));
            }
            result[field.Name] = field.Type switch
            {
                "byte" => Read(address, 1)[0],
                "sbyte" => (sbyte)Read(address, 1)[0],
                "bool" => Read(address, 1)[0] != 0,
                "short" => BitConverter.ToInt16(Read(address, 2)),
                "ushort" => BitConverter.ToUInt16(Read(address, 2)),
                "int" => BitConverter.ToInt32(Read(address, 4)),
                "uint" => BitConverter.ToUInt32(Read(address, 4)),
                "long" => BitConverter.ToInt64(Read(address, 8)),
                "ulong" => BitConverter.ToUInt64(Read(address, 8)),
                "float" => BitConverter.ToSingle(Read(address, 4)),
                "double" => BitConverter.ToDouble(Read(address, 8)),
                _ => throw new Exception($"Unsupported state type: {field.Type}.")
            };
        }
        return (ExpandoObject)result;
    }
    public void Dispose() => handle.Dispose();
}
