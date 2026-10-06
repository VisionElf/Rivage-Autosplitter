using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SpeedTimer.AslHost;

internal static class RivageCardTests
{
    public static void Run()
    {
        string[] ids = ["card_miranda", "card_pod_pass", "card_amaan", "card_multipass", "card_jahi", "card_train_jahi", "card_rafael", "card_jonny"];
        string[] rows = ["MirandaCard", "PodPass", "AmaanCard", "Multipass", "JahiCard", "TrainCardJahi", "RafaelCard", "JonnyCard"];
        int checks = 0, reads = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        using var runtime = new AslRuntime(new() { Source = File.ReadAllText(RivageReaderFixture.ScriptPath), TimingMethod = "gameTime" });
        var script = runtime.Script;
        Check(runtime.Describe().Settings!.Skip(1).Take(8).Select(s => s.Id).SequenceEqual(ids), "Eight card settings follow the start option in the requested route order");
        Check(runtime.Describe().Settings!.All(s => !s.Value && s.Parent == null), "Card settings default off and remain independent");
        Tuple<long, bool, bool, bool>? state = Tuple.Create(1L, false, false, false);
        Tuple<long, int>? cards = Tuple.Create(100L, 0);
        var ending = Tuple.Create(10L, false, false);
        script.vars.readRunState = (Func<Tuple<long, bool, bool, bool>?>)(() => state);
        script.vars.readLoadingScreen = (Func<bool?>)(() => state?.Item3);
        script.vars.readCards = (Func<long, Tuple<long, int>?>)(_ => { reads++; return cards; });
        script.vars.readEnding = (Func<long, Tuple<long, bool, bool>>)(_ => ending);
        HostReply Tick(string phase = "running", bool first = false, bool autoSplit = true)
        {
            script.update();
            var reply = new HostReply();
            runtime.Evaluate(new() { Phase = phase, AutoSplit = autoSplit }, reply, first);
            return reply;
        }
        void Reset(int mask = 0, int enabled = 255)
        {
            script.vars.initializeTracking(); script.vars.cardSeen = 0;
            state = Tuple.Create(1L, false, false, false); cards = Tuple.Create(100L, mask);
            ending = Tuple.Create(10L, false, false);
            for (int i = 0; i < ids.Length; i++) script.settings.Entries[ids[i]].Value = (enabled & (1 << i)) != 0;
        }
        Tick(); Check(reads == 0, "Default settings do not poll the inventory");
        for (int selected = 0; selected < 8; selected++)
        {
            int bit = 1 << selected;
            Reset(enabled: bit); Tick(first: true);
            cards = Tuple.Create(100L, 255 & ~bit);
            Check(Tick().Action == null, $"Unselected cards do not split for {ids[selected]}");
            cards = Tuple.Create(100L, 255);
            Check(Tick().Action == "split", $"Only the selected {ids[selected]} pickup splits");
            Check(Tick().Action == null, "Keeping the card does not split twice");
            cards = Tuple.Create(100L, 0); Tick(); cards = Tuple.Create(100L, bit);
            Check(Tick().Action == null, "Removing and reacquiring a card cannot duplicate its split");
        }
        Reset(255); Check(Tick(first: true).Action == null && Tick().Action == null, "Attaching with owned cards never splits");
        Reset(); Tick(); cards = Tuple.Create(200L, 255);
        Check(Tick().Action == null, "Replacing the inventory establishes a baseline");
        Reset(); Tick(); cards = null;
        Check(Tick().Action == null && Tick().IsLoading == false, "Unreadable inventory neither splits nor removes gameplay time");
        cards = Tuple.Create(100L, 255);
        Check(Tick().Action == null, "Recovery does not split cards first seen after a read failure");
        foreach (bool menu in new[] { false, true })
        {
            Reset(); Tick(); state = Tuple.Create(1L, menu, !menu, false); cards = Tuple.Create(100L, 255);
            var reply = Tick();
            Check(reply.Action == null && reply.IsLoading == !menu, "Menu/load restoration is not a pickup and menuing stays timed");
            state = Tuple.Create(1L, false, false, false);
            Check(Tick().Action == null, "Restored inventory after menu/loading seeds without splitting");
        }
        Reset(enabled: 1); Tick(); cards = Tuple.Create(100L, 2); Tick();
        script.settings.Entries[ids[1]].Value = true;
        Check(Tick().Action == null, "Enabling a card already collected does not split retrospectively");
        cards = Tuple.Create(100L, 0); Tick(); cards = Tuple.Create(100L, 2);
        Check(Tick().Action == null, "A pickup consumed while disabled stays consumed");
        Reset(); Tick(); cards = Tuple.Create(100L, 1);
        Check(Tick(autoSplit: false).Action == null && Tick().Action == null, "Global Auto Split remains authoritative");
        Reset(); Tick(); cards = Tuple.Create(100L, 3);
        Check(Tick().Action == "split" && Tick().Action == "split" && Tick().Action == null, "Two simultaneous pickups drain exactly two splits");
        Reset(); Tick(); cards = Tuple.Create(100L, 1);
        Check(Tick(first: true).Action == null && Tick().Action == "split", "Observed pickup survives host recovery seeding");
        foreach (string phase in new[] { "notRunning", "ended", "paused" })
        {
            Reset(); Tick(phase); cards = Tuple.Create(100L, 1);
            Check(Tick(phase).Action == null, "Non-running timers do not split"); Tick(phase); Tick(phase);
            Check(Tick().Action == null, "A stale pickup cannot split a later running timer");
        }
        Reset(); Tick(); cards = Tuple.Create(100L, 1); Tick();
        state = Tuple.Create(1L, true, false, false); Tick();
        state = Tuple.Create(1L, false, true, true);
        Check(Tick("notRunning").Action == null, "New Game waits for loading completion with card settings enabled");
        state = Tuple.Create(1L, false, false, false); cards = Tuple.Create(100L, 0);
        Check(Tick("notRunning").Action == "start", "Loading completion starts with card settings enabled");
        cards = Tuple.Create(100L, 1);
        Check(Tick().Action == "split", "New Game rearms cards for the next attempt");
        Reset(); Tick(); cards = Tuple.Create(100L, 1); Tick();
        state = null;
        try { Tick(); throw new Exception("Invalid game state accepted"); } catch (IOException) { checks++; }
        state = Tuple.Create(1L, false, false, false); cards = Tuple.Create(100L, 0); Tick(); cards = Tuple.Create(100L, 1);
        Check(Tick().Action == null, "A transient game-state failure preserves consumed cards");
        Reset(); Tick(); cards = Tuple.Create(100L, 3); ending = Tuple.Create(10L, true, true);
        Check(Tick().Action == "split" && Tick().Action == null, "Final Pod opening takes priority and clears card events");

        // The production inventory reader uses real memory owned by this process.
        var reader = RivageReaderFixture.Create(File.ReadAllText(RivageReaderFixture.ScriptPath));
        var allocations = new List<IntPtr>();
        long Allocate(int size)
        {
            var p = Marshal.AllocHGlobal(size); allocations.Add(p);
            Marshal.Copy(new byte[size], 0, p, size); return p.ToInt64();
        }
        void Long(long p, long v) => Marshal.WriteInt64((IntPtr)p, v);
        void Int(long p, int v) => Marshal.WriteInt32((IntPtr)p, v);
        try
        {
            long pool = Allocate(32), block = Allocate(65536); Long(pool + 16, block); reader.vars.namesBase = pool;
            int cursor = 2;
            int Name(string text)
            {
                int id = cursor / 2; byte[] bytes = Encoding.UTF8.GetBytes(text);
                Marshal.WriteInt16((IntPtr)(block + cursor), (short)(bytes.Length << 6));
                Marshal.Copy(bytes, 0, (IntPtr)(block + cursor + 2), bytes.Length);
                cursor = (cursor + bytes.Length + 3) & ~1; return id;
            }
            long Object(string type, int size)
            {
                long p = Allocate(size), c = Allocate(0x28); Long(p + 0x10, c); Int(c + 0x18, Name(type)); return p;
            }
            long instance = Allocate(0x50), players = Allocate(8), player = Allocate(0x38), controller = Allocate(0x300);
            long pawn = Object("CharacterFPS_C", 0x830), inventory = Object("AC_Inventory_C", 0x1C0), items = Allocate(80);
            Long(instance + 0x38, players); Long(players, player); Long(player + 0x30, controller); Long(controller + 0x2F0, pawn);
            Long(pawn + 0x6B0, inventory); Long(inventory + 0xF0, pawn); Long(inventory + 0xF8, items);
            Int(inventory + 0x104, 10);
            Tuple<long, int>? Read() => reader.vars.readCards(instance);
            Check(Read()?.Item2 == 0, "A readable empty inventory is a valid baseline");
            Int(inventory + 0x100, 1);
            for (int i = 0; i < 8; i++)
            {
                Int(items, Name(rows[i]));
                Check(Read()?.Item2 == 1 << i, $"Production reader maps {rows[i]} to its setting");
            }
            Int(items, Name("MirandaBirthday"));
            Check(Read()?.Item2 == 0, "Related names that are not cards do not match");
            Int(items, Name("MirandaCard")); Int(items + 4, 1);
            Check(Read()?.Item2 == 0, "Numbered FNames cannot masquerade as the card row");
            Int(items + 4, 0); Int(items + 8, Name("PodPass")); Int(inventory + 0x100, 2);
            Check(Read()?.Item2 == 3, "Reader recognizes simultaneous card ownership");
            Int(items + 8, Marshal.ReadInt32((IntPtr)items));
            Check(Read()?.Item2 == 1, "Duplicate inventory entries collapse to one card");
            Long(inventory + 0xF0, 0);
            Check(Read() == null, "Inventory must belong to the local pawn"); Long(inventory + 0xF0, pawn);
            Int(inventory + 0x100, -1); Check(Read() == null, "Negative array length is rejected");
            Int(inventory + 0x100, 257); Int(inventory + 0x104, 257);
            Check(Read() == null, "Oversized arrays are rejected before reading items");
            Int(inventory + 0x100, 2); Int(inventory + 0x104, 1);
            Check(Read() == null, "Length exceeding capacity is rejected");
            Int(inventory + 0x104, 10); Long(inventory + 0xF8, 0);
            Check(Read() == null, "Nonempty null arrays are unreadable, not empty");
            Long(inventory + 0xF8, items); Int(items, int.MaxValue);
            Check(Read() == null, "Unreadable names invalidate the entire inventory sample");
        }
        finally
        {
            reader.game.Dispose();
            foreach (var allocation in allocations) Marshal.FreeHGlobal(allocation);
        }
        Console.WriteLine($"Rivage card splits: {checks} checks passed.");
    }
}
