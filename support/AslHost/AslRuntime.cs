using System.Diagnostics;

namespace Rivage.AslHost;

public sealed class HostRequest
{
    public string Command { get; set; } = "tick";
    public string Source { get; set; } = "";
    public Dictionary<string, bool> Values { get; set; } = [];
    public Dictionary<string, bool> Answers { get; set; } = [];
    public string TimingMethod { get; set; } = "realTime";
    public string Phase { get; set; } = "notRunning";
    public bool AutoStart { get; set; } = true;
    public bool AutoSplit { get; set; } = true;
    public bool AutoReset { get; set; } = true;
}
public sealed class HostReply
{
    public string Status { get; set; } = "Waiting for game";
    public string? Error { get; set; }
    public string? Action { get; set; }
    public bool? IsLoading { get; set; }
    public double? GameTime { get; set; }
    public string? Process { get; set; }
    public string? Version { get; set; }
    public string? TimingMethod { get; set; }
    public double RefreshRate { get; set; } = 60;
    public List<Setting>? Settings { get; set; }
    public List<ScriptQuestion>? Questions { get; set; }
    public string[]? Targets { get; set; }
}
public sealed class AslRuntime : IDisposable
{
    private readonly AslDocument document;
    public ScriptBase Script { get; }
    private Process? process;
    private ProcessMemory? memory;
    private StateDescriptor? state;
    private bool sampled;
    private DateTime nextAttach;
    public AslRuntime(HostRequest request)
    {
        document = new AslParser(request.Source).Parse();
        Script = ScriptCompiler.Compile(document);
        Script.settings = new(request.Values) { StartEnabled = request.AutoStart, SplitEnabled = request.AutoSplit, ResetEnabled = request.AutoReset };
        Script.MessageBox = new(request.Answers);
        Script.timer.CurrentTimingMethod = request.TimingMethod == "gameTime" ? TimingMethod.GameTime : TimingMethod.RealTime;
        Script.startup();
        Script.MessageBox.Enabled = false;
    }
    public HostReply Describe() => new()
    {
        Status = Script.MessageBox.Questions.Any(q => q.Answer == null) ? "Waiting for script answers" : "Ready",
        Settings = Script.settings.Entries.Values.ToList(),
        Questions = Script.MessageBox.Questions,
        Targets = document.States.Select(s => s.Process).Distinct().ToArray(),
        TimingMethod = Script.timer.CurrentTimingMethod == TimingMethod.GameTime ? "gameTime" : "realTime"
    };
    public HostReply Tick(HostRequest request)
    {
        Script.timer.CurrentTimingMethod = request.TimingMethod == "gameTime" ? TimingMethod.GameTime : TimingMethod.RealTime;
        if (Script.MessageBox.Questions.Any(q => q.Answer == null)) return new() { Status = "Waiting for script answers" };
        if (process != null && process.HasExited) Disconnect();
        if (process == null)
        {
            if (DateTime.UtcNow < nextAttach) return new();
            nextAttach = DateTime.UtcNow.AddSeconds(1);
            foreach (var target in document.States.Select(s => s.Process).Distinct())
            {
                var candidates = Process.GetProcessesByName(target);
                process = candidates.FirstOrDefault();
                foreach (var extra in candidates.Skip(1)) extra.Dispose();
                if (process != null) break;
            }
            if (process == null) return new();
            try
            {
                Script.game = process;
                Script.modules = process.Modules.Cast<ProcessModule>().ToList();
                Script.version = "";
                Script.init();
                state = document.States.FirstOrDefault(s => s.Process == process.ProcessName && s.Version == Script.version)
                    ?? document.States.FirstOrDefault(s => s.Process == process.ProcessName && s.Version == "")
                    ?? document.States.First(s => s.Process == process.ProcessName);
                memory = new(process);
            }
            catch
            {
                Disconnect(runExit: false);
                throw;
            }
        }
        try
        {
            var current = memory!.Snapshot(state!, Script.modules);
            Script.old = sampled ? Script.current : current;
            Script.current = current;
            var first = !sampled; sampled = true;
            var reply = new HostReply
            {
                Status = "Connected",
                Process = process.ProcessName,
                Version = Script.version,
                RefreshRate = double.IsFinite(Script.refreshRate) ? Math.Clamp(Script.refreshRate, 1, 240) : 60
            };
            Script.settings.StartEnabled = request.AutoStart;
            Script.settings.SplitEnabled = request.AutoSplit;
            Script.settings.ResetEnabled = request.AutoReset;
            if (Script.update() == false) return reply;
            Evaluate(request, reply, first);
            var method = Script.timer.CurrentTimingMethod == TimingMethod.GameTime ? "gameTime" : "realTime";
            if (method != request.TimingMethod) reply.TimingMethod = method;
            return reply;
        }
        catch (IOException error) { sampled = false; return new() { Status = error.Message, Process = process.ProcessName, Version = Script.version, IsLoading = true }; }
    }
    public void Evaluate(HostRequest request, HostReply reply, bool first = false)
    {
        Script.settings.StartEnabled = request.AutoStart;
        Script.settings.SplitEnabled = request.AutoSplit;
        Script.settings.ResetEnabled = request.AutoReset;
        if (request.Phase is "running" or "paused")
        {
            reply.IsLoading = Script.isLoading();
            reply.GameTime = Script.gameTime()?.TotalSeconds;
            if (first) return;
            var reset = Script.reset() == true;
            if (reset && request.AutoReset) reply.Action = "reset";
            else if (!reset && Script.split() == true && request.AutoSplit && request.Phase == "running") reply.Action = "split";
        }
        else if (!first && request.Phase == "notRunning" && Script.start() == true && request.AutoStart) reply.Action = "start";
    }
    private void Disconnect(bool runExit = true)
    {
        if (process != null && runExit) Script.exit();
        if (process != null) ProcessReadExtensions.Release(process);
        memory?.Dispose(); process?.Dispose(); memory = null; process = null; state = null; sampled = false;
    }
    public void Dispose() { Disconnect(); Script.shutdown(); }
}
