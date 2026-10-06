using System.Diagnostics;
using System.Dynamic;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace SpeedTimer.AslHost;

public abstract class ScriptBase
{
    public dynamic current = new ExpandoObject();
    public dynamic old = new ExpandoObject();
    public dynamic vars = new ExpandoObject();
    public ScriptSettings settings = new([]);
    public ScriptDialogs MessageBox = new([]);
    public TimerBridge timer = new();
    public Process game = null!;
    public List<ProcessModule> modules = [];
    public string version = "";
    public double refreshRate = 60;
    public void print(object? value) => Console.Error.WriteLine(value);
    public virtual void startup() { }
    public virtual void init() { }
    public virtual void exit() { }
    public virtual void shutdown() { }
    public virtual bool? update() => null;
    public virtual bool? start() => null;
    public virtual bool? split() => null;
    public virtual bool? reset() => null;
    public virtual bool? isLoading() => null;
    public virtual TimeSpan? gameTime() => null;
}

public static class ScriptCompiler
{
    public static ScriptBase Compile(AslDocument document)
    {
        var code = "using System; using System.Linq; using System.Collections.Generic; using System.Diagnostics; using System.Dynamic; using SpeedTimer.AslHost; public sealed class LoadedScript : ScriptBase {\n";
        foreach (var (name, body) in document.Actions)
        {
            var result = new[] { "startup", "init", "exit", "shutdown" }.Contains(name) ? "void" : name == "gameTime" ? "TimeSpan?" : "bool?";
            code += $"public override {result} {name}() {{\n#line 1 \"{name}\"\n{body}\n#line default\n";
            if (result != "void") code += "return null;\n";
            code += "}\n";
        }
        code += "}";
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        var references = paths.Append(typeof(ScriptBase).Assembly.Location).Distinct().Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("Asl_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText(code)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release));
        using var stream = new MemoryStream();
        var resultEmit = compilation.Emit(stream);
        if (!resultEmit.Success) throw new Exception("ASL compilation failed:\n" + string.Join("\n", resultEmit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Take(12)));
        return (ScriptBase)Activator.CreateInstance(Assembly.Load(stream.ToArray()).GetType("LoadedScript")!)!;
    }
}
