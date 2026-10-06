using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Rivage.AslHost;

internal static class RivageFingerprintTests
{
    public static void Run()
    {
        const string id = "fingerprint_jonny";
        int checks = 0, reads = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        using var runtime = new AslRuntime(new() { Source = File.ReadAllText(RivageReaderFixture.ScriptPath), TimingMethod = "gameTime" });
        var script = runtime.Script;
        var setting = runtime.Describe().Settings!.Single(s => s.Id == id);
        Check(setting.Id == id && !setting.Value && setting.Parent == null, "Fingerprint option follows the cards, independent and default off");
        Tuple<long, bool, bool, bool>? state = Tuple.Create(1L, false, false, false);
        Tuple<long, bool>? fingerprint = Tuple.Create(100L, false);
        var cards = Tuple.Create(100L, 0);
        var ending = Tuple.Create(10L, false, false);
        script.vars.readRunState = (Func<Tuple<long, bool, bool, bool>?>)(() => state);
        script.vars.readLoadingScreen = (Func<bool?>)(() => state?.Item3);
        script.vars.readFingerprint = (Func<long, Tuple<long, bool>?>)(_ => { reads++; return fingerprint; });
        script.vars.readCards = (Func<long, Tuple<long, int>>)(_ => cards);
        script.vars.readEnding = (Func<long, Tuple<long, bool, bool>>)(_ => ending);
        HostReply Tick(string phase = "running", bool first = false, bool autoSplit = true)
        {
            script.update();
            var reply = new HostReply();
            runtime.Evaluate(new() { Phase = phase, AutoSplit = autoSplit }, reply, first);
            return reply;
        }
        void Reset(bool complete = false)
        {
            script.vars.initializeTracking(); script.vars.fingerprintSeen = false; script.vars.cardSeen = 0;
            state = Tuple.Create(1L, false, false, false); fingerprint = Tuple.Create(100L, complete);
            cards = Tuple.Create(100L, 0); ending = Tuple.Create(10L, false, false);
            foreach (var entry in script.settings.Entries.Values) entry.Value = entry.Id == id;
        }
        Tick(); Check(reads == 0, "Disabled fingerprint option does not poll fragments");
        Reset(); Tick(first: true);
        Check(Tick().Action == null, "Incomplete fingerprint does not split");
        fingerprint = Tuple.Create(100L, true);
        Check(Tick().Action == "split" && Tick().Action == null, "Completion splits exactly once");
        fingerprint = Tuple.Create(100L, false); Tick(); fingerprint = Tuple.Create(100L, true);
        Check(Tick().Action == null, "Replacing fragments and completing again cannot duplicate the split");
        Reset(true);
        Check(Tick(first: true).Action == null && Tick().Action == null, "An already complete fingerprint never splits on attachment");
        Reset(); Tick(); script.settings.Entries[id].Value = false;
        fingerprint = Tuple.Create(100L, true); Tick(); script.settings.Entries[id].Value = true;
        Check(Tick().Action == null && Tick().Action == null, "Enabling the option after completion seeds without splitting");
        foreach (bool menu in new[] { false, true })
        {
            Reset(); Tick(); state = Tuple.Create(1L, menu, !menu, false); fingerprint = Tuple.Create(100L, true);
            var reply = Tick();
            Check(reply.Action == null && reply.IsLoading == !menu, "Menu/load restoration does not split; menuing remains timed");
            state = Tuple.Create(1L, false, false, false);
            Check(Tick().Action == null && Tick().Action == null, "Complete fingerprint after menu/loading establishes a baseline");
        }
        Reset(); Tick(); fingerprint = null;
        Check(Tick().Action == null && Tick().IsLoading == false, "Unreadable fragments do not split or pause Game Time");
        fingerprint = Tuple.Create(100L, true);
        Check(Tick().Action == null && Tick().Action == null, "Reader recovery with a complete fingerprint does not manufacture completion");
        Reset(); Tick(); fingerprint = Tuple.Create(200L, true);
        Check(Tick().Action == null, "Inventory replacement seeds without splitting");
        Reset(); Tick(); fingerprint = Tuple.Create(100L, true); Tick(); state = null;
        try { Tick(); throw new Exception("Invalid game state accepted"); } catch (IOException) { checks++; }
        state = Tuple.Create(1L, false, false, false); fingerprint = Tuple.Create(100L, false); Tick();
        fingerprint = Tuple.Create(100L, true);
        Check(Tick().Action == null, "Transient game-state failure preserves consumed completion");
        Reset(); Tick(); fingerprint = Tuple.Create(100L, true);
        Check(Tick(autoSplit: false).Action == null && Tick().Action == null, "Global Auto Split remains authoritative");
        foreach (string phase in new[] { "notRunning", "ended", "paused" })
        {
            Reset(); Tick(phase); fingerprint = Tuple.Create(100L, true);
            Check(Tick(phase).Action == null, "Non-running timers do not split"); Tick(phase); Tick(phase);
            Check(Tick().Action == null, "Stale completion cannot split a later running timer");
        }
        Reset(); Tick(); fingerprint = Tuple.Create(100L, true); Tick();
        state = Tuple.Create(1L, true, false, false); Tick();
        state = Tuple.Create(1L, false, true, true);
        Check(Tick("notRunning").Action == null, "New Game waits for loading completion with fingerprint enabled");
        state = Tuple.Create(1L, false, false, false); fingerprint = Tuple.Create(100L, false);
        Check(Tick("notRunning").Action == "start", "Loading completion starts with fingerprint enabled");
        fingerprint = Tuple.Create(100L, true);
        Check(Tick().Action == "split", "New Game rearms fingerprint completion");
        foreach (bool recovery in new[] { false, true })
        {
            Reset(); script.settings.Entries["card_jonny"].Value = true; Tick();
            fingerprint = Tuple.Create(100L, true); cards = Tuple.Create(100L, 128);
            if (recovery) Check(Tick(first: true).Action == null, "Host recovery suppresses only its initial action");
            Check(Tick().Action == "split" && Tick().Action == "split" && Tick().Action == null,
                "Simultaneous fingerprint and card events each split once, including after recovery");
        }
        Reset(); Tick(); fingerprint = Tuple.Create(100L, true); ending = Tuple.Create(10L, true, true);
        Check(Tick().Action == "split" && Tick().Action == null, "Final Pod opening takes priority over completion");

        // Exercise the production reader with memory owned by this test process.
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
                int index = cursor / 2; byte[] bytes = Encoding.UTF8.GetBytes(text);
                Marshal.WriteInt16((IntPtr)(block + cursor), (short)(bytes.Length << 6));
                Marshal.Copy(bytes, 0, (IntPtr)(block + cursor + 2), bytes.Length);
                cursor = (cursor + bytes.Length + 3) & ~1; return index;
            }
            long Object(string type, int size)
            {
                long p = Allocate(size), c = Allocate(0x28); Long(p + 0x10, c); Int(c + 0x18, Name(type)); return p;
            }
            long instance = Allocate(0x50), players = Allocate(8), player = Allocate(0x38), controller = Allocate(0x300);
            long pawn = Object("CharacterFPS_C", 0x830), inventory = Object("AC_Inventory_C", 0x1C0), slots = Allocate(64);
            long table = Object("DataTable", 0x28);
            int tableName = Name("DT_Fingerprint"), none = Name("None"), jonny = Name("Jonny");
            Int(table + 0x18, tableName);
            Long(instance + 0x38, players); Long(players, player); Long(player + 0x30, controller); Long(controller + 0x2F0, pawn);
            Long(pawn + 0x6B0, inventory); Long(inventory + 0xF0, pawn);
            Long(inventory + 0x120, slots); Int(inventory + 0x128, 4); Int(inventory + 0x12C, 4);
            void Fragments(int mask, int row)
            {
                for (int i = 0; i < 4; i++)
                {
                    Long(slots + i * 16, table);
                    Int(slots + i * 16 + 8, (mask & (1 << i)) != 0 ? row : none);
                    Int(slots + i * 16 + 12, (mask & (1 << i)) != 0 ? i + 1 : 0);
                }
            }
            Tuple<long, bool>? Read() => reader.vars.readFingerprint(instance);
            for (int mask = 0; mask < 16; mask++)
            {
                Fragments(mask, jonny);
                Check(Read()?.Item2 == (mask == 15), $"Only four distinct Jonny fragments complete the fingerprint (mask {mask})");
            }
            foreach (string crew in new[] { "Jahi", "Rafael" })
            {
                Fragments(15, Name(crew)); Check(Read()?.Item2 == false, "Other crew fingerprints do not complete Jonny's fingerprint");
                Int(slots + 8, jonny); Check(Read()?.Item2 == false, "Mixed crew fragments do not complete Jonny's fingerprint");
            }
            Fragments(15, jonny); Int(slots + 28, 1);
            Check(Read()?.Item2 == false, "Duplicate Jonny fragments cannot count as four distinct parts");
            Fragments(15, jonny); Int(slots + 12, 2); Int(slots + 28, 1);
            Check(Read()?.Item2 == false, "Fragments must match their designated positions");
            Fragments(15, jonny); Int(table + 0x18, Name("UnrelatedTable"));
            Check(Read() == null, "Unrelated data tables are rejected"); Int(table + 0x18, tableName);
            long tableClass = Marshal.ReadInt64((IntPtr)(table + 0x10)); Long(table + 0x10, 0);
            Check(Read() == null, "Fingerprint table class must be valid"); Long(table + 0x10, tableClass);
            Long(slots + 16, 0); Check(Read() == null, "Every fragment handle must refer to the same fingerprint table");
            Fragments(15, jonny); Long(inventory + 0xF0, 0);
            Check(Read() == null, "Fingerprint inventory must belong to the local pawn"); Long(inventory + 0xF0, pawn);
            foreach (int count in new[] { -1, 0, 3, 5 })
            {
                Int(inventory + 0x128, count); Check(Read() == null, "A malformed slot count cannot be completion");
            }
            Int(inventory + 0x128, 4);
            foreach (int capacity in new[] { 3, 65 })
            {
                Int(inventory + 0x12C, capacity); Check(Read() == null, "Invalid fingerprint capacity is rejected");
            }
            Int(inventory + 0x12C, 4); Long(inventory + 0x120, 0);
            Check(Read() == null, "Null slot arrays are rejected"); Long(inventory + 0x120, 1);
            Check(Read() == null, "Unreadable slot arrays are rejected"); Long(inventory + 0x120, slots);
            Int(slots + 8, int.MaxValue); Check(Read() == null, "Unreadable row names invalidate the fingerprint sample");
        }
        finally
        {
            reader.game.Dispose();
            foreach (var allocation in allocations) Marshal.FreeHGlobal(allocation);
        }
        Console.WriteLine($"Rivage fingerprint split: {checks} checks passed.");
    }
}
