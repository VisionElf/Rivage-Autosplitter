using System.Diagnostics;
using System.Runtime.InteropServices;
using SpeedTimer.AslHost;

internal static class RivageLifecycleTests
{
    public static void Run()
    {
        var source = File.ReadAllText(RivageReaderFixture.ScriptPath);
        var reader = RivageReaderFixture.Create(source);
        var address = Marshal.AllocHGlobal(0x28);
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
            checks++;
        }
        try
        {
            Marshal.WriteInt32(address + 0x18, 42);
            reader.vars.readName = (Func<uint, string>)(id => id == 42 ? "ObservedObject" : "OtherObject");
            Check(reader.vars.objectName(address.ToInt64()) == "ObservedObject",
                "Readers use the attached process despite LiveSplit's null startup game");

            using var replacement = Process.GetCurrentProcess();
            reader.game.Dispose();
            reader.vars.initializeReaders(replacement);
            reader.vars.readName = (Func<uint, string>)(id => id == 42 ? "ReconnectedObject" : "OtherObject");
            Check(reader.vars.objectName(address.ToInt64()) == "ReconnectedObject",
                "Reattachment binds readers to the replacement instead of the disposed process");
        }
        finally
        {
            reader.game.Dispose();
            Marshal.FreeHGlobal(address);
        }
        Console.WriteLine($"Rivage ASL lifecycle: {checks} checks passed.");
    }
}
