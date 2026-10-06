using System.Diagnostics;
using SpeedTimer.AslHost;

internal static class RivageReaderFixture
{
    public static string ScriptPath => Path.Combine(AppContext.BaseDirectory, "Rivage.asl");

    public static ScriptBase Create(string source)
    {
        var document = new AslParser(source).Parse();
        // LiveSplit supplies game as an action parameter, not an updated host field.
        // Capture the null startup value to expose readers bound before attachment.
        document.Actions["startup"] = "var game = base.game;\n" + document.Actions["startup"];
        var reader = ScriptCompiler.Compile(document);
        reader.startup();
        reader.game = Process.GetCurrentProcess();
        reader.vars.initializeReaders(reader.game);
        return reader;
    }
}
