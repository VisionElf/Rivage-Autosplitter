using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Rivage.AslHost;

internal static class RivageTests
{
    public static void Run()
    {
        var source = File.ReadAllText(RivageReaderFixture.ScriptPath);
        int checks = 0;
        void Check(bool value, string name)
        {
            if (!value) throw new Exception(name);
            checks++;
        }
        using var runtime = new AslRuntime(new() { Source = source, TimingMethod = "gameTime" });
        var script = runtime.Script;
        Tuple<long, bool, bool, bool>? state = Tuple.Create(1L, true, false, false);
        Tuple<long, bool, bool>? ending = Tuple.Create(10L, false, false);
        script.vars.readRunState = (Func<Tuple<long, bool, bool, bool>?>)(() => state);
        bool? loadingOverride = null;
        script.vars.readLoadingScreen = (Func<bool?>)(() => loadingOverride ?? state?.Item3);
        script.vars.readEnding = (Func<long, Tuple<long, bool, bool>?>)(_ => ending);
        HostReply Tick(string phase = "running", bool first = false, bool enabled = true)
        {
            script.update();
            var reply = new HostReply();
            runtime.Evaluate(new() { Phase = phase, AutoStart = enabled, AutoSplit = enabled }, reply, first);
            return reply;
        }
        void Clear() => script.vars.initializeTracking();
        Check(Tick("notRunning", true).Action == null, "Menu attachment does not start");
        state = Tuple.Create(1L, false, true, true);
        Check(Tick("notRunning").Action == null, "Fresh New Game waits for loading completion");
        Check(Tick().IsLoading == true, "Initial loading is removed");
        Check(Tick("notRunning").Action == null, "Start is consumed once");
        state = Tuple.Create(1L, false, false, false);
        Check(Tick("notRunning").Action == "start", "Fresh New Game starts at initial loading completion");
        Check(Tick("notRunning").Action == null, "Loading-end start is consumed once");
        Check(Tick().IsLoading == false, "Intro and gameplay are timed outside loading");
        state = Tuple.Create(1L, false, true, false);
        Check(Tick().IsLoading == true && Tick().Action == null, "Intro skip load pauses without splitting");
        state = Tuple.Create(1L, true, false, false);
        Check(Tick().Action == null && Tick().IsLoading == false, "Menu navigation remains timed without resetting");
        state = Tuple.Create(1L, true, true, false);
        Check(Tick().IsLoading == true, "Actual loading is removed even while the menu flag is set");
        state = Tuple.Create(1L, true, false, false);
        Check(Tick().IsLoading == false, "Game Time resumes when loading ends even if the menu stays open");
        state = Tuple.Create(1L, false, false, false);
        Check(Tick().IsLoading == false, "Closing the menu does not change load removal");
        Clear(); state = Tuple.Create(1L, true, true, false); loadingOverride = true;
        Check(Tick().IsLoading == true, "Actual travel to the menu still pauses Game Time");
        loadingOverride = false;
        Check(Tick().IsLoading == false, "First warning/logo sample resumes despite the lingering native flag");
        Check(Tick().IsLoading == false, "Skippable menu presentation remains timed");
        state = Tuple.Create(1L, false, true, true);
        Check(Tick("notRunning").Action == null, "New Game after skipping the warning waits for its loading screen");
        Check(Tick().IsLoading == false, "Start detection is independent of the visible loading screen");
        loadingOverride = true;
        Check(Tick().IsLoading == true, "The actual New Game loading screen pauses time");
        loadingOverride = false;
        Check(Tick("notRunning").Action == "start", "Visible loading completion starts despite the native tail");
        Check(Tick().IsLoading == false && Tick().Action == null, "Gameplay resumes immediately, without waiting for the two-second native tail");
        state = Tuple.Create(1L, false, false, false); loadingOverride = true;
        Check(Tick().IsLoading == true, "A real loading screen is removed even if the native flag is not set yet");
        script.vars.readLoadingScreen = (Func<bool?>)(() => null);
        try { Tick(); throw new Exception("Unreadable loading screen accepted"); }
        catch (IOException) { checks++; }
        Check(script.isLoading() == true, "Unknown loading state fails closed");
        loadingOverride = false;
        script.vars.readLoadingScreen = (Func<bool?>)(() => loadingOverride ?? state?.Item3);
        Check(Tick(first: true).IsLoading == false, "Host recovery resumes Game Time on its first readable sample");
        loadingOverride = null;
        state = Tuple.Create(1L, true, false, false); Tick();
        state = Tuple.Create(1L, false, true, false);
        Check(Tick("notRunning").Action == null, "Continue does not start a fresh attempt");
        Clear();
        state = Tuple.Create(1L, false, true, true);
        Check(Tick("notRunning", true).Action == null && Tick("notRunning").Action == null,
            "Attaching during a New Game load does not start retrospectively");
        state = Tuple.Create(1L, true, false, false); Tick("notRunning");
        state = Tuple.Create(1L, false, true, true);
        Check(Tick("notRunning").Action == null, "Loading onset does not produce a start");
        state = Tuple.Create(1L, false, false, false);
        Check(Tick("notRunning", true).Action == null && Tick("notRunning").Action == "start",
            "Observed start survives the host recovery seed");
        state = Tuple.Create(1L, true, false, false); Tick("notRunning");
        state = Tuple.Create(1L, true, true, false);
        Check(Tick("notRunning").Action == null, "Loading before the menu exit does not start early");
        state = Tuple.Create(1L, false, true, true);
        Check(Tick("notRunning").Action == null, "Loading and menu flags may change in separate samples without starting early");
        state = Tuple.Create(1L, false, false, false);
        Check(Tick("notRunning").Action == "start", "Separately observed menu exit and loading completion start");
        state = Tuple.Create(1L, true, false, false); Tick("notRunning");
        state = Tuple.Create(1L, false, true, true);
        Tick("notRunning", enabled: false);
        state = Tuple.Create(1L, false, false, false);
        Check(Tick("notRunning", enabled: false).Action == null && Tick("notRunning").Action == null,
            "Disabled Auto Start consumes the event");
        state = Tuple.Create(1L, true, false, false); Tick("notRunning");
        state = Tuple.Create(2L, false, true, true);
        Check(Tick("notRunning").Action == null, "Replacing GameInstance disarms the old menu");
        state = null;
        try { Tick(); throw new Exception("Unreadable state accepted"); }
        catch (IOException) { checks++; }
        Check(script.isLoading() == true, "Unreadable state pauses Game Time");

        var startSetting = runtime.Describe().Settings!.First();
        Check(startSetting.Id == "start_after_cutscene" && startSetting.Label == "Start: After Cutscene"
            && !startSetting.Value, "After-cutscene start is the first setting and defaults off");
        Clear(); state = Tuple.Create(1L, true, false, false);
        script.settings.Entries["start_after_cutscene"].Value = true;
        Tick("notRunning");
        state = Tuple.Create(1L, false, true, true);
        Check(Tick("notRunning").Action == null && Tick("notRunning").Action == null,
            "Repeated samples of the first load do not start after-cutscene mode");
        state = Tuple.Create(1L, false, false, false);
        Check(Tick("notRunning").Action == null && Tick("notRunning").Action == null,
            "First completion and repeated introduction samples do not count twice");
        script.settings.Entries["start_after_cutscene"].Value = false;
        state = Tuple.Create(1L, false, true, false);
        Check(Tick("notRunning").Action == null, "Second load waits for completion using the latched setting");
        state = Tuple.Create(1L, false, false, false);
        Check(Tick("notRunning").Action == "start" && Tick("notRunning").Action == null,
            "Second loading completion starts once even after changing the option");

        Clear(); state = Tuple.Create(1L, false, false, false);
        ending = Tuple.Create(10L, false, false);
        Tick(first: true);
        Check(Tick().Action == null, "Ordinary play and Pod unlock do not split");
        ending = Tuple.Create(10L, true, false);
        Check(Tick().Action == null, "Preparing the final sequence is not playback");
        ending = Tuple.Create(10L, true, true);
        Check(Tick().Action == "split" && Tick().Action == null, "Opening splits exactly once");
        ending = Tuple.Create(10L, false, false); Tick();
        ending = Tuple.Create(10L, true, true);
        Check(Tick().Action == null, "Repeated endpoint stays latched for this attempt");
        Clear();
        Check(Tick(first: true).Action == null && Tick().Action == null, "Attaching during ending never splits");
        Clear(); ending = Tuple.Create(10L, true, false); Tick(first: true);
        ending = Tuple.Create(10L, true, true);
        Check(Tick().Action == null, "Attaching to a paused ending then resuming never splits");
        Clear(); ending = Tuple.Create(10L, false, false); Tick(first: true);
        ending = Tuple.Create(20L, true, true);
        Check(Tick().Action == null, "A replaced pawn cannot reuse the previous arming");
        Clear(); ending = Tuple.Create(10L, false, false); Tick();
        ending = null; Check(Tick().Action == null, "Failed endpoint reads do not split");
        ending = Tuple.Create(10L, true, true);
        Check(Tick().Action == null, "Recovery into an active ending does not manufacture an edge");
        Clear(); ending = Tuple.Create(10L, false, false); Tick();
        state = Tuple.Create(1L, false, true, false); Tick();
        state = Tuple.Create(1L, false, false, false); ending = Tuple.Create(10L, true, true);
        Check(Tick().Action == null, "Loading disarms the previous pawn");
        Clear(); ending = Tuple.Create(10L, false, false); Tick();
        state = Tuple.Create(1L, true, false, false);
        ending = Tuple.Create(10L, true, true);
        var menuReply = Tick();
        Check(menuReply.IsLoading == false && menuReply.Action == null, "A timed menu still suppresses the final signal");
        state = Tuple.Create(1L, false, false, false);
        Check(Tick().Action == null, "Leaving the menu cannot reuse a stale ending edge");
        Clear(); ending = Tuple.Create(10L, false, false); Tick();
        ending = Tuple.Create(10L, true, true);
        Check(Tick(first: true).Action == null && Tick().Action == "split", "Final action survives host recovery seed");
        state = Tuple.Create(1L, true, false, false); Tick();
        state = Tuple.Create(1L, false, false, false); ending = Tuple.Create(10L, false, false); Tick();
        ending = Tuple.Create(10L, true, true);
        Check(Tick(enabled: false).Action == null && Tick().Action == null, "Disabled Auto Split consumes the event");

        // Exercise the shipped readers against real memory owned by this test process.
        var reader = RivageReaderFixture.Create(source);
        var allocations = new List<IntPtr>();
        long Allocate(int size)
        {
            var p = Marshal.AllocHGlobal(size);
            allocations.Add(p); Marshal.Copy(new byte[size], 0, p, size); return p.ToInt64();
        }
        void Long(long p, long value) => Marshal.WriteInt64((IntPtr)p, value);
        void Int(long p, int value) => Marshal.WriteInt32((IntPtr)p, value);
        void Byte(long p, byte value) => Marshal.WriteByte((IntPtr)p, value);
        try
        {
            long pool = Allocate(32), block = Allocate(65536); Long(pool + 16, block);
            reader.vars.namesBase = pool;
            int nextName = 2;
            int Name(string text)
            {
                int id = nextName / 2; byte[] bytes = Encoding.UTF8.GetBytes(text);
                Marshal.WriteInt16((IntPtr)(block + nextName), (short)(bytes.Length << 6));
                Marshal.Copy(bytes, 0, (IntPtr)(block + nextName + 2), bytes.Length);
                nextName = (nextName + bytes.Length + 3) & ~1; return id;
            }
            long Object(string type, int size)
            {
                long p = Allocate(size), c = Allocate(0x28);
                Int(c + 0x18, Name(type)); Long(p + 0x10, c); return p;
            }
            long global = Allocate(8), engine = Allocate(0x12D0);
            reader.vars.moduleBase = global - 0x939A830; Long(global, engine);
            long instance = Object("GlobalInstance_C", 0x500); Long(engine + 0x12C8, instance);
            long save = Object("BP_SaveGame_C", 0x100); Long(instance + 0x2E8, save);
            Byte(instance + 0x3C0, 1);
            var sampled = (Tuple<long, bool, bool, bool>)reader.vars.readRunState();
            Check(sampled.Item2 && !sampled.Item3 && !sampled.Item4, "Reader resolves menu through GameEngine");
            Byte(instance + 0x3C0, 0); Byte(instance + 0x263, 1); Int(save + 0x28, -1);
            sampled = reader.vars.readRunState();
            Check(sampled.Item3 && sampled.Item4, "Reader recognizes a fresh save during loading");
            Byte(save + 0x40, 1); sampled = reader.vars.readRunState();
            Check(!sampled.Item4, "An existing save is not New Game");
            Long(instance + 0x2E8, 0); sampled = reader.vars.readRunState();
            Check(sampled.Item3 && !sampled.Item4, "Unavailable save data only disables start, not readable timing state");
            Long(instance + 0x2E8, save); Byte(save + 0x40, 7); sampled = reader.vars.readRunState();
            Check(!sampled.Item4, "Invalid save flags cannot manufacture a New Game");
            Byte(instance + 0x263, 7);
            Check(reader.vars.readRunState() == null, "Invalid loading byte is rejected");
            Byte(instance + 0x263, 0);
            long players = Allocate(8), player = Allocate(0x38), controller = Allocate(0x300);
            long pawn = Object("CharacterFPS_C", 0x830);
            Long(instance + 0x38, players); Long(players, player); Long(player + 0x30, controller); Long(controller + 0x2F0, pawn);
            var endpoint = (Tuple<long, bool, bool>)reader.vars.readEnding(instance);
            Check(endpoint.Item1 == pawn && !endpoint.Item2 && !endpoint.Item3, "Reader resolves the local pawn");
            Byte(instance + 0x3C3, 1); endpoint = reader.vars.readEnding(instance);
            Check(!endpoint.Item3, "Pod unlock flag is not an opening signal");
            Byte(pawn + 0x81B, 2); endpoint = reader.vars.readEnding(instance);
            Check(endpoint.Item2 && !endpoint.Item3, "Selected cinematic without actor is not playing");
            long actor = Object("LevelSequenceActor", 0x340), sequence = Allocate(0x28), sequencePlayer = Allocate(0x298);
            Long(pawn + 0x778, actor); Long(actor + 0x2F8, sequence);
            endpoint = reader.vars.readEnding(instance);
            Check(endpoint.Item2 && !endpoint.Item3, "Sequence actor awaiting its player is still being prepared");
            Long(actor + 0x2F0, sequencePlayer);
            Int(sequence + 0x18, Name("Cinematic_Intro")); Byte(sequencePlayer + 0x290, 1);
            endpoint = reader.vars.readEnding(instance);
            Check(!endpoint.Item3, "Wrong sequence is rejected even when playing");
            Int(sequence + 0x18, Name("Cinematic_End")); endpoint = reader.vars.readEnding(instance);
            Check(endpoint.Item3, "Cinematic_End playback is the opening signal");
            foreach (byte status in new byte[] { 0, 2, 3, 4, 5, 6 })
            {
                Byte(sequencePlayer + 0x290, status); endpoint = reader.vars.readEnding(instance);
                Check(!endpoint.Item3, "Only Playing counts as sequence activation");
            }
            Long(actor + 0x2F8, 0);
            Check(reader.vars.readEnding(instance) == null, "Unreadable sequence is not interpreted as inactive");

            long movieGlobal = Allocate(8), movie = Allocate(0xB1);
            reader.vars.moduleBase = movieGlobal - 0x93533E8;
            long movieType = (long)reader.vars.moduleBase + 0x7709050;
            Long(movieGlobal, movie); Long(movie + 0x10, movieType);
            Check(reader.vars.readLoadingScreen() == false, "Idle MoviePlayer keeps menus, warnings and gameplay timed");
            Long(movie + 0xA0, 123);
            Check(reader.vars.readLoadingScreen() == true, "Active loading thread identifies an actual loading screen");
            Byte(movie + 0xB0, 1);
            Int(movie + 0xAC, 1);
            Check(reader.vars.readLoadingScreen() == true, "Completed IO does not resume before the loading screen closes");
            Long(movie + 0xA0, 0);
            Check(reader.vars.readLoadingScreen() == true, "Destroying the loading thread does not end the final on-screen wait");
            Check(reader.vars.readLoadingScreen() == true, "The final wait remains loading on subsequent samples without a fixed timeout");
            script.vars.readLoadingScreen = (Func<bool?>)(() => reader.vars.readLoadingScreen());
            Clear(); state = Tuple.Create(1L, false, true, false);
            Check(Tick(first: true).IsLoading == true, "Attaching during the threadless final wait pauses Game Time");
            Check(Tick().IsLoading == true, "Game Time stays paused throughout the threadless final wait");
            Byte(movie + 0xB0, 0);
            Check(reader.vars.readLoadingScreen() == false, "Closing the loading screen resumes immediately");
            Check(Tick().IsLoading == false, "The shipped reader resumes Game Time on viewport restoration despite the native flag tail");
            Byte(movie + 0xB0, 1);
            Check(reader.vars.readLoadingScreen() == true, "Playback activation before thread creation is already loading");
            Byte(movie + 0xB0, 7);
            Check(reader.vars.readLoadingScreen() == null, "An invalid playback-active byte is rejected");
            Long(movie + 0xA0, 123);
            Check(reader.vars.readLoadingScreen() == null, "A loading thread cannot mask an invalid playback-active byte");
            Byte(movie + 0xB0, 0); Long(movie + 0xA0, 0);
            Long(movie + 0x10, movieType + 8);
            Check(reader.vars.readLoadingScreen() == null, "Unexpected MoviePlayer implementation is rejected");
            Long(movieGlobal, 0);
            Check(reader.vars.readLoadingScreen() == null, "Missing MoviePlayer is not assumed to mean gameplay");
            Long(movieGlobal, 1);
            Check(reader.vars.readLoadingScreen() == null, "Unreadable MoviePlayer is rejected");
        }
        finally
        {
            reader.game.Dispose();
            foreach (var allocation in allocations) Marshal.FreeHGlobal(allocation);
        }
        Console.WriteLine($"Rivage ASL: {checks} checks passed.");
    }
}
