namespace SpeedTimer.AslHost;

public sealed class Setting(string id, bool value, string label, string? parent)
{
    public string Id { get; } = id;
    public bool Value { get; set; } = value;
    public string Label { get; } = label;
    public string? Parent { get; } = parent;
    public string? ToolTip { get; set; }
}

public sealed class ScriptSettings(Dictionary<string, bool> values)
{
    public bool StartEnabled { get; set; } = true;
    public bool SplitEnabled { get; set; } = true;
    public bool ResetEnabled { get; set; } = true;
    public string? CurrentDefaultParent { get; set; }
    public Dictionary<string, Setting> Entries { get; } = [];
    public void Add(string id, bool defaultValue = true, string? description = null, string? parent = null)
    {
        parent ??= CurrentDefaultParent;
        if (parent != null && !Entries.ContainsKey(parent)) throw new Exception($"Unknown setting parent: {parent}.");
        Entries.Add(id, new(id, values.GetValueOrDefault(id, defaultValue), description ?? id, parent));
    }
    public bool this[string id] => Entries.TryGetValue(id, out var entry)
        ? entry.Value && (entry.Parent == null || this[entry.Parent]) : throw new Exception($"Unknown setting: {id}.");
    public void SetToolTip(string id, string text) => Entries[id].ToolTip = text;
}

public enum TimingMethod { RealTime, GameTime }
public enum DialogResult { Yes, No }
public enum MessageBoxButtons { YesNo }
public enum MessageBoxIcon { Question }
public record ScriptQuestion(string Id, string Text, string Title, bool? Answer);
public sealed class ScriptDialogs(Dictionary<string, bool> answers)
{
    public bool Enabled { get; set; } = true;
    public List<ScriptQuestion> Questions { get; } = [];
    public DialogResult Show(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        if (!Enabled) throw new Exception("Script questions are only supported in startup.");
        var id = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(title + "\n" + text)));
        bool? answer = answers.TryGetValue(id, out var value) ? value : null;
        Questions.Add(new(id, text, title, answer));
        return answer == true ? DialogResult.Yes : DialogResult.No;
    }
}
public sealed class TimerBridge { public TimingMethod CurrentTimingMethod { get; set; } }
