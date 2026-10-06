using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Rivage.AslHost;

internal static class RivageDrawingTests
{
    public static void Run()
    {
        var source = File.ReadAllText(RivageReaderFixture.ScriptPath);
        string[] ids = ["drawing_lobby_map", "drawing_laboratory_sequence"];
        int checks = 0, reads = 0;
        void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
        using var runtime = new AslRuntime(new() { Source = source, TimingMethod = "gameTime" });
        var script = runtime.Script;
        Check(runtime.Describe().Settings!.Where(s => ids.Contains(s.Id)).Select(s => s.Id).SequenceEqual(ids)
            && ids.All(id => !script.settings[id]), "Drawing options follow existing settings and default off");
        var state = Tuple.Create(1L, false, false, false);
        Tuple<long, bool>? lobby = Tuple.Create(100L, false), lab = Tuple.Create(10L, false);
        var ending = Tuple.Create(10L, false, false);
        script.vars.readRunState = (Func<Tuple<long, bool, bool, bool>>)(() => state);
        script.vars.readLoadingScreen = (Func<bool?>)(() => state.Item3);
        script.vars.readEnding = (Func<long, Tuple<long, bool, bool>>)(_ => ending);
        script.vars.readLobbyDrawing = (Func<long, Tuple<long, bool>?>)(_ => { reads++; return lobby; });
        script.vars.readLaboratoryDrawing = (Func<long, Tuple<long, bool>?>)(_ => { reads++; return lab; });
        HostReply Tick(bool first = false, bool enabled = true)
        {
            script.update();
            var reply = new HostReply();
            runtime.Evaluate(new() { Phase = "running", AutoSplit = enabled }, reply, first);
            return reply;
        }
        void Reset(bool active = false)
        {
            script.vars.initializeTracking(); script.vars.drawingSeen = 0;
            state = Tuple.Create(1L, false, false, false);
            lobby = Tuple.Create(100L, active); lab = Tuple.Create(10L, active);
            ending = Tuple.Create(10L, false, false);
            foreach (var setting in script.settings.Entries.Values) setting.Value = ids.Contains(setting.Id);
        }
        Tick(); Check(reads == 0, "Disabled options perform no drawing reads");
        foreach (int index in new[] { 0, 1 })
        {
            Reset(); script.settings.Entries[ids[1 - index]].Value = false; Tick(first: true);
            if (index == 0) lobby = Tuple.Create(100L, true); else lab = Tuple.Create(10L, true);
            Check(Tick().Action == "split" && Tick().Action == null, "Each drawing independently splits once on its display edge");
            if (index == 0) lobby = Tuple.Create(100L, false); else lab = Tuple.Create(10L, false);
            Tick();
            if (index == 0) lobby = Tuple.Create(100L, true); else lab = Tuple.Create(10L, true);
            Check(Tick().Action == null, "Reopening a drawing cannot duplicate its split");
        }
        Reset(true); Check(Tick(first: true).Action == null && Tick().Action == null, "Already displayed drawings seed without retrospective splits");
        Reset(); Tick(); lobby = null; lab = null;
        Check(Tick().Action == null && Tick().IsLoading == false, "Drawing read failures do not pause Game Time");
        lobby = Tuple.Create(100L, true); lab = Tuple.Create(10L, true);
        Check(Tick().Action == null, "Recovery into displayed drawings establishes a baseline");
        Reset(); Tick(); lobby = Tuple.Create(200L, true); lab = Tuple.Create(20L, true);
        Check(Tick().Action == null, "Replaced widgets or pawns establish a baseline");
        foreach (bool menu in new[] { false, true })
        {
            Reset(); Tick(); state = Tuple.Create(1L, menu, !menu, false); Tick();
            state = Tuple.Create(1L, false, false, false); lobby = Tuple.Create(100L, true); lab = Tuple.Create(10L, true);
            Check(Tick().Action == null, "Menu/load restoration does not manufacture a drawing edge");
        }
        Reset(); Tick(); lobby = Tuple.Create(100L, true); lab = Tuple.Create(10L, true);
        Check(Tick(enabled: false).Action == null && Tick(enabled: false).Action == null && Tick().Action == null,
            "Global Auto Split consumes disabled events");
        Reset(); Tick(); lobby = Tuple.Create(100L, true); lab = Tuple.Create(10L, true);
        Check(Tick(first: true).Action == null && Tick().Action == "split" && Tick().Action == "split" && Tick().Action == null,
            "Simultaneous drawings survive recovery seeding and drain one per tick");
        Reset(); Tick(); lobby = Tuple.Create(100L, true); ending = Tuple.Create(10L, true, true);
        Check(Tick().Action == "split" && Tick().Action == null, "Final Pod split overrides simultaneous drawings");
        Reset(); Tick(); lobby = Tuple.Create(100L, true); Tick();
        state = Tuple.Create(1L, true, false, false); Tick();
        state = Tuple.Create(1L, false, true, true); Tick();
        state = Tuple.Create(1L, false, false, false); lobby = Tuple.Create(100L, false); Tick();
        lobby = Tuple.Create(100L, true); Check(Tick().Action == "split", "New Game rearms drawings");

        const string horizonId = "projection_horizon_probe";
        Tuple<long, bool>? horizon = Tuple.Create(300L, false);
        script.vars.readHorizonProjection = (Func<long, Tuple<long, bool>?>)(_ => horizon);
        Check(!runtime.Describe().Settings!.Single(s => s.Id == horizonId).Value, "Horizon projection defaults off");
        Reset(); script.settings.Entries[horizonId].Value = true; Tick();
        horizon = Tuple.Create(300L, true);
        Check(Tick().Action == "split" && Tick().Action == null, "Wall projection appearance splits once");
        horizon = Tuple.Create(300L, false); Tick(); horizon = Tuple.Create(300L, true);
        Check(Tick().Action == null, "Replaying the sequence cannot duplicate the projection split");
        Reset(); script.settings.Entries[horizonId].Value = true;
        Check(Tick(first: true).Action == null && Tick().Action == null, "Attaching to a visible projection does not split");
        Reset(); script.settings.Entries[horizonId].Value = true; horizon = Tuple.Create(300L, false); Tick();
        horizon = null; Check(Tick().IsLoading == false, "Unavailable projection data does not pause Game Time");
        horizon = Tuple.Create(300L, true); Check(Tick().Action == null, "Projection recovery seeds without a false edge");

        var reader = RivageReaderFixture.Create(source);
        var allocations = new List<IntPtr>();
        long Allocate(int size)
        {
            var p = Marshal.AllocHGlobal(size); allocations.Add(p);
            Marshal.Copy(new byte[size], 0, p, size); return p.ToInt64();
        }
        void Long(long p, long value) => Marshal.WriteInt64((IntPtr)p, value);
        void Int(long p, int value) => Marshal.WriteInt32((IntPtr)p, value);
        try
        {
            long pool = Allocate(32), block = Allocate(65536); Long(pool + 16, block); reader.vars.namesBase = pool;
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
                long p = Allocate(size), c = Allocate(0x28); Int(c + 0x18, Name(type)); Long(p + 0x10, c); return p;
            }
            long instance = Allocate(0x40), players = Allocate(8), player = Allocate(0x38), controller = Allocate(0x300);
            long pawn = Object("CharacterFPS_C", 0x830);
            Long(instance + 0x38, players); Long(players, player); Long(player + 0x30, controller); Long(controller + 0x2F0, pawn);
            long pc = Object("W_RafaelLaptop_C", 0x440);
            long map = Object("W_RafaelLaptop_Hexadecimal_C", 0x3E0), switcher = Object("WidgetSwitcher", 0x198);
            long drawing = Object("W_RafaelDrawing_Cubik_Where_C", 0x28);
            Long(pawn + 0x7B0, pc); Long(pc + 0x390, map);
            Long(map + 0x330, switcher); Long(map + 0x398, drawing);
            Marshal.WriteByte((IntPtr)(map + 0x3D0), 1);
            Check(!((Tuple<long, bool>)reader.vars.readLobbyDrawing(instance)).Item2, "Solved Lobby puzzle before drawing display remains inactive");
            Int(switcher + 0x190, 1);
            Check(((Tuple<long, bool>)reader.vars.readLobbyDrawing(instance)).Item2, "The open PC follows drawing display without a hover pointer");
            Int(switcher + 0x190, 7); Check(reader.vars.readLobbyDrawing(instance) == null, "Invalid view index invalidates the Lobby sample");
            Check(!((Tuple<long, bool>)reader.vars.readLaboratoryDrawing(instance)).Item2, "A closed inspection is inactive");
            long inspected = Object("AC_Inspectable_C", 0x3F0), paper = Object("BP_VaultLab_Rubik_Sequence_C", 0x2D8);
            Long(pawn + 0x708, inspected); Long(inspected + 0x2D0, paper); Long(paper + 0x2B8, inspected);
            Check(((Tuple<long, bool>)reader.vars.readLaboratoryDrawing(instance)).Item2, "Laboratory click is active immediately without waiting for its animation");
            long other = Object("BP_OtherPaper_C", 0x2D8); Long(inspected + 0x2D0, other);
            Check(!((Tuple<long, bool>)reader.vars.readLaboratoryDrawing(instance)).Item2, "Other papers do not trigger the Laboratory split");
            Long(inspected + 0x2D0, 1); Check(reader.vars.readLaboratoryDrawing(instance) == null, "Unreadable paper ownership invalidates the sample");
            Long(inspected + 0x2D0, paper); Long(paper + 0x2B8, 0);
            Check(reader.vars.readLaboratoryDrawing(instance) == null, "Mismatched inspection ownership is rejected");

            long objectArray = Allocate(48), chunks = Allocate(8), chunk = Allocate(72);
            reader.vars.moduleBase = objectArray - 0x920CB10;
            Long(objectArray + 0x10, chunks); Int(objectArray + 0x24, 3); Long(chunks, chunk);
            long probe = Object("BP_Showcase_Horizon_C", 0x398), projection = Object("StaticMeshComponent", 0x1B0);
            Long(probe + 0x2E0, projection); Long(projection + 0x20, probe);
            Long(chunk + 2 * 24 + 8, probe); Int(chunk + 2 * 24 + 16, 123);
            long pad = Object("BP_PK_StellarWave_Pad_C", 0x440), bindings = Allocate(16);
            Long(inspected + 0x2D0, pad); Long(pad + 0x3E8, bindings); Int(pad + 0x3F0, 1); Int(pad + 0x3F4, 4);
            Int(bindings, 2); Int(bindings + 4, 123); Int(bindings + 8, Name("SoundPlayed"));
            Check(!((Tuple<long, bool>)reader.vars.readHorizonProjection(instance)).Item2, "Pad delegate discovers the Horizon probe with its projection hidden");
            Marshal.WriteByte((IntPtr)(projection + 0x1A8), 0x20); Long(pawn + 0x708, 0);
            Check(((Tuple<long, bool>)reader.vars.readHorizonProjection(instance)).Item2, "Cached delegate reference follows projection after closing the pad");
            Marshal.WriteByte((IntPtr)(projection + 0x1A9), 0x08);
            Check(!((Tuple<long, bool>)reader.vars.readHorizonProjection(instance)).Item2, "Hidden-in-game projections remain inactive");
            Marshal.WriteByte((IntPtr)(projection + 0x1A9), 0); Long(projection + 0x20, other);
            Check(reader.vars.readHorizonProjection(instance) == null, "A projection owned by another actor is rejected");
            Long(projection + 0x20, probe); Int(chunk + 2 * 24 + 16, 124);
            Check(reader.vars.readHorizonProjection(instance) == null, "Expired weak reference serials invalidate cached probes");
            Check((long)reader.vars.readHorizonObject(3, 123) == 0, "Weak object indexes cannot exceed array bounds");
        }
        finally { reader.game.Dispose(); foreach (var p in allocations) Marshal.FreeHGlobal(p); }
        Console.WriteLine($"Rivage drawings: {checks} checks passed.");
    }
}
