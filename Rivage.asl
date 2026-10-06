/*
Rivage: New Game start, native load removal, optional pickups/drawings, final Pod opening.
Supported executable and validation are documented in docs/Technical-reference.md.
All process access is read-only. No automatic reset.
Developed by Codex, under the supervision of VisionElf.
*/
state("Rivage-Win64-Shipping") { }

startup
{
    settings.Add("start_after_cutscene", false, "Start: After Cutscene");
    settings.SetToolTip("start_after_cutscene", "Start RTA and Game Time when the second New Game loading screen closes, after the introduction or its skip. When disabled, start after the first loading screen. Configure before selecting New Game.");
    vars.cardSettings = new[] { "card_miranda", "card_pod_pass", "card_amaan", "card_multipass", "card_jahi", "card_train_jahi", "card_rafael", "card_jonny" };
    vars.cardRows = new[] { "MirandaCard", "PodPass", "AmaanCard", "Multipass", "JahiCard", "TrainCardJahi", "RafaelCard", "JonnyCard" };
    string[] labels = { "Level 1 - Miranda card", "Pod Bay Access", "Level 2 - Amaan card", "Multipass", "Level 3 - Jahi card", "Train Card (Jahi)", "Level 4 - Rafael card", "Level 5 - Jonny card" };
    for (int i = 0; i < labels.Length; i++)
    {
        string id = ((string[])vars.cardSettings)[i];
        settings.Add(id, false, labels[i]);
        settings.SetToolTip(id, "Split once when this card is added to the inventory. Cards already owned when attaching or loading a save do not split.");
    }
    settings.Add("fingerprint_jonny", false, "Jonny fingerprint (4/4)");
    settings.SetToolTip("fingerprint_jonny", "Split once when all four slots contain Jonny's matching fingerprint fragments. An already complete fingerprint does not split on attachment or save loading.");
    vars.drawingSettings = new[] { "drawing_lobby_map", "drawing_laboratory_sequence", "projection_horizon_probe" };
    settings.Add("drawing_lobby_map", false, "Lobby decoded map");
    settings.SetToolTip("drawing_lobby_map", "Split once when the decoded map switches to its drawing view. Solving the code before the drawing appears does not split.");
    settings.Add("drawing_laboratory_sequence", false, "Laboratory sequence drawing");
    settings.SetToolTip("drawing_laboratory_sequence", "Split once when you click the sequence drawing inside the gear puzzle's chest to inspect it. Does not wait for the inspection animation.");
    settings.Add("projection_horizon_probe", false, "Horizon probe projection");
    settings.SetToolTip("projection_horizon_probe", "Split once when the six-note pad sequence makes the Horizon probe project its code on the wall. Replaying the sequence does not split again.");
    vars.drawingSeen = 0;
    vars.clearDrawingSample = (Action<int>)(index => {
        ((long[])vars.drawingOwners)[index] = 0L;
        ((bool[])vars.drawingSeeded)[index] = false;
        ((bool[])vars.drawingPrevious)[index] = false;
        vars.drawingPending = (int)vars.drawingPending & ~(1 << index);
    });
    vars.fingerprintSeen = false;
    vars.clearFingerprintSample = (Action)(() => {
        vars.fingerprintInventory = 0L;
        vars.fingerprintSeeded = false;
        vars.fingerprintPending = 0;
    });
    vars.cardSeen = 0;
    vars.clearCardSample = (Action)(() => {
        vars.cardInventory = 0L;
        vars.cardSeeded = false;
        vars.cardPrevious = 0;
        vars.cardPending = 0;
        vars.cardPendingAge = 0;
    });
    vars.nameCache = new Dictionary<uint, string>();
    vars.initializeTracking = (Action)(() => {
        vars.instance = 0L;
        vars.menuArmed = false;
        vars.startWaitingForLoad = false;
        vars.startLoadObserved = false;
        vars.startLoadsRemaining = 0;
        vars.startPending = 0;
        vars.finalPending = 0;
        vars.finalCompleted = false;
        vars.endingPawn = 0L;
        vars.endingSeeded = false;
        vars.endingArmed = false;
        vars.loading = true;
        vars.clearCardSample();
        vars.clearFingerprintSample();
        vars.drawingOwners = new long[3];
        vars.drawingSeeded = new bool[3];
        vars.drawingPrevious = new bool[3];
        vars.drawingPending = 0;
        vars.drawingPendingAge = 0;
        vars.clearHorizonReference();
    });
    vars.clearHorizonReference = (Action)(() => {
        vars.horizonPawn = 0L;
        vars.horizonIndex = -1;
        vars.horizonSerial = 0;
    });
    vars.initializeTracking();

    // Bind readers only after attachment; LiveSplit startup receives a null game.
    vars.initializeReaders = (Action<Process>)(process => {
        vars.readName = (Func<uint, string>)(id => {
            var cache = (Dictionary<uint, string>)vars.nameCache;
            string cached;
            if (cache.TryGetValue(id, out cached)) return cached;
            if ((id >> 16) >= 8192) return null;
            long block;
            if (!process.ReadValue<long>((IntPtr)((long)vars.namesBase + 0x10 + (id >> 16) * 8L), out block) || block == 0) return null;
            long entry = block + (id & 65535) * 2L;
            ushort header;
            if (!process.ReadValue<ushort>((IntPtr)entry, out header)) return null;
            int length = header >> 6;
            if (length < 1 || length > 1023) return null;
            byte[] bytes;
            bool wide = (header & 1) != 0;
            if (!process.ReadBytes((IntPtr)(entry + 2), length * (wide ? 2 : 1), out bytes)) return null;
            string name = (wide ? System.Text.Encoding.Unicode : System.Text.Encoding.UTF8).GetString(bytes);
            if (cache.Count >= 128) cache.Clear();
            cache[id] = name;
            return name;
        });
        vars.objectName = (Func<long, string>)(address => {
            uint id;
            return address != 0 && process.ReadValue<uint>((IntPtr)(address + 0x18), out id) ? (string)vars.readName(id) : null;
        });
        vars.isClass = (Func<long, string, bool>)((address, expected) => {
            long type;
            return address != 0 && process.ReadValue<long>((IntPtr)(address + 0x10), out type) && vars.objectName(type) == expected;
        });
        // The game's loading flag stays true for two seconds after EndLoadingScreen.
        // MoviePlayer destroys its loading thread before its final on-screen wait.
        // Its playback-active byte also covers that wait and viewport restoration.
        vars.readLoadingScreen = (Func<bool?>)(() => {
            long player = 0, type = 0, after = 0;
            byte[] playback = null;
            if (!process.ReadValue<long>((IntPtr)((long)vars.moduleBase + 0x93533E8), out player) || player == 0
                || !process.ReadValue<long>((IntPtr)(player + 0x10), out type)
                || type != (long)vars.moduleBase + 0x7709050
                || !process.ReadBytes((IntPtr)(player + 0xA0), 0x11, out playback)
                || playback[0x10] > 1
                || !process.ReadValue<long>((IntPtr)((long)vars.moduleBase + 0x93533E8), out after)
                || after != player) return null;
            return BitConverter.ToInt64(playback, 0) != 0 || playback[0x10] == 1;
        });
        // Instance, main menu, native transition flag, fresh New Game save.
        vars.readRunState = (Func<Tuple<long, bool, bool, bool>>)(() => {
            long engine = 0, instance = 0;
            byte[] data = null;
            if (!process.ReadValue<long>((IntPtr)((long)vars.moduleBase + 0x939A830), out engine) || engine == 0
                || !process.ReadValue<long>((IntPtr)(engine + 0x12C8), out instance)
                || !vars.isClass(instance, "GlobalInstance_C")
                || !process.ReadBytes((IntPtr)(instance + 0x260), 0x164, out data)) return null;
            byte loading = data[3], menu = data[0x160];
            if (loading > 1 || menu > 1) return null;
            bool fresh = false;
            // Save data is only needed to recognize a fresh New Game transition.
            if (loading == 1 && menu == 0)
            {
                long save = BitConverter.ToInt64(data, 0x88);
                byte[] saved = null;
                // Missing save data prevents Auto Start, but must not prolong load removal.
                if (vars.isClass(save, "BP_SaveGame_C") && process.ReadBytes((IntPtr)(save + 0x28), 0x19, out saved))
                    fresh = saved[0x18] == 0 && BitConverter.ToInt32(saved, 0) == -1;
            }
            return Tuple.Create(instance, menu == 1, loading == 1, fresh);
        });
        vars.readPawn = (Func<long, long>)(instance => {
            long players = 0, player = 0, controller = 0, pawn = 0;
            if (!process.ReadValue<long>((IntPtr)(instance + 0x38), out players) || players == 0
                || !process.ReadValue<long>((IntPtr)players, out player) || player == 0
                || !process.ReadValue<long>((IntPtr)(player + 0x30), out controller) || controller == 0
                || !process.ReadValue<long>((IntPtr)(controller + 0x2F0), out pawn)
                || !vars.isClass(pawn, "CharacterFPS_C")) return 0L;
            return pawn;
        });
        vars.readInventory = (Func<long, long>)(instance => {
            long pawn = vars.readPawn(instance), inventory = 0, owner = 0;
            if (pawn == 0 || !process.ReadValue<long>((IntPtr)(pawn + 0x6B0), out inventory)
                || !vars.isClass(inventory, "AC_Inventory_C")
                || !process.ReadValue<long>((IntPtr)(inventory + 0xF0), out owner) || owner != pawn) return 0L;
            return inventory;
        });
        // Follow the local player's open PC and map view, independently of cursor hover.
        vars.readLobbyDrawing = (Func<long, Tuple<long, bool>>)(instance => {
            long pawn = vars.readPawn(instance), widget = 0, map = 0;
            if (pawn == 0 || !process.ReadValue<long>((IntPtr)(pawn + 0x7B0), out widget)
                || !vars.isClass(widget, "W_RafaelLaptop_C")
                || !process.ReadValue<long>((IntPtr)(widget + 0x390), out map)) return null;
            long switcher = 0, drawing = 0, after = 0;
            int view = 0;
            if (!vars.isClass(map, "W_RafaelLaptop_Hexadecimal_C")
                || !process.ReadValue<long>((IntPtr)(map + 0x330), out switcher)
                || !vars.isClass(switcher, "WidgetSwitcher")
                || !process.ReadValue<long>((IntPtr)(map + 0x398), out drawing)
                || !vars.isClass(drawing, "W_RafaelDrawing_Cubik_Where_C")
                || !process.ReadValue<int>((IntPtr)(switcher + 0x190), out view) || view < 0 || view > 1
                || !process.ReadValue<long>((IntPtr)(map + 0x330), out after) || after != switcher
                || !process.ReadValue<long>((IntPtr)(pawn + 0x7B0), out after) || after != widget) return null;
            return Tuple.Create(map, view == 1);
        });
        // The local pawn selects this exact paper on click, before the viewing animation.
        vars.readLaboratoryDrawing = (Func<long, Tuple<long, bool>>)(instance => {
            long pawn = vars.readPawn(instance), inspected = 0, owner = 0, component = 0, after = 0;
            if (pawn == 0 || !process.ReadValue<long>((IntPtr)(pawn + 0x708), out inspected)) return null;
            if (inspected == 0) return Tuple.Create(pawn, false);
            long type = 0;
            if (!process.ReadValue<long>((IntPtr)(inspected + 0x10), out type)) return null;
            string inspectedClass = vars.objectName(type);
            if (inspectedClass == null) return null;
            if (inspectedClass != "AC_Inspectable_C") return Tuple.Create(pawn, false);
            if (!process.ReadValue<long>((IntPtr)(inspected + 0x2D0), out owner)) return null;
            if (owner == 0 || !process.ReadValue<long>((IntPtr)(owner + 0x10), out type)) return null;
            string ownerClass = vars.objectName(type);
            if (ownerClass == null) return null;
            if (ownerClass != "BP_VaultLab_Rubik_Sequence_C") return Tuple.Create(pawn, false);
            if (!process.ReadValue<long>((IntPtr)(owner + 0x2B8), out component) || component != inspected
                || !process.ReadValue<long>((IntPtr)(pawn + 0x708), out after) || after != inspected) return null;
            return Tuple.Create(pawn, true);
        });
        // Resolve only a delegate's weak reference, never scan the global object array.
        vars.readHorizonObject = (Func<int, int, long>)((index, serial) => {
            long objects = (long)vars.moduleBase + 0x920CB10, chunks = 0, chunk = 0;
            int count = 0;
            byte[] item = null;
            if (index < 0 || serial <= 0
                || !process.ReadValue<int>((IntPtr)(objects + 0x24), out count) || index >= count
                || !process.ReadValue<long>((IntPtr)(objects + 0x10), out chunks) || chunks == 0
                || !process.ReadValue<long>((IntPtr)(chunks + (index / 65536) * 8L), out chunk) || chunk == 0
                || !process.ReadBytes((IntPtr)(chunk + (index % 65536) * 24L), 24, out item)
                || BitConverter.ToInt32(item, 16) != serial) return 0L;
            long actor = BitConverter.ToInt64(item, 8);
            return vars.isClass(actor, "BP_Showcase_Horizon_C") ? actor : 0L;
        });
        vars.readHorizonProjection = (Func<long, Tuple<long, bool>>)(instance => {
            long pawn = vars.readPawn(instance), inspected = 0, pad = 0;
            if (pawn == 0 || !process.ReadValue<long>((IntPtr)(pawn + 0x708), out inspected)) return null;
            if (pawn != vars.horizonPawn) { vars.clearHorizonReference(); vars.horizonPawn = pawn; }
            long actor = vars.readHorizonObject((int)vars.horizonIndex, (int)vars.horizonSerial);
            if (actor == 0)
            {
                vars.horizonIndex = -1;
                if (!vars.isClass(inspected, "AC_Inspectable_C")
                    || !process.ReadValue<long>((IntPtr)(inspected + 0x2D0), out pad)
                    || !vars.isClass(pad, "BP_PK_StellarWave_Pad_C")) return null;
                byte[] header = null, bindings = null, after = null;
                if (!process.ReadBytes((IntPtr)(pad + 0x3E8), 16, out header)) return null;
                long data = BitConverter.ToInt64(header, 0);
                int count = BitConverter.ToInt32(header, 8), capacity = BitConverter.ToInt32(header, 12);
                if (data == 0 || count < 1 || count > 16 || capacity < count || capacity > 64
                    || !process.ReadBytes((IntPtr)data, count * 16, out bindings)
                    || !process.ReadBytes((IntPtr)(pad + 0x3E8), 16, out after) || !header.SequenceEqual(after)) return null;
                for (int i = 0; i < count; i++)
                {
                    int index = BitConverter.ToInt32(bindings, i * 16), serial = BitConverter.ToInt32(bindings, i * 16 + 4);
                    if (BitConverter.ToUInt32(bindings, i * 16 + 12) != 0
                        || vars.readName(BitConverter.ToUInt32(bindings, i * 16 + 8)) != "SoundPlayed") continue;
                    actor = vars.readHorizonObject(index, serial);
                    if (actor == 0) continue;
                    vars.horizonIndex = index; vars.horizonSerial = serial; break;
                }
            }
            long projection = 0, owner = 0;
            byte[] visibility = null;
            if (actor == 0 || !process.ReadValue<long>((IntPtr)(actor + 0x2E0), out projection)
                || !vars.isClass(projection, "StaticMeshComponent")
                || !process.ReadValue<long>((IntPtr)(projection + 0x20), out owner) || owner != actor
                || !process.ReadBytes((IntPtr)(projection + 0x1A8), 2, out visibility)
                || vars.readHorizonObject((int)vars.horizonIndex, (int)vars.horizonSerial) != actor) return null;
            return Tuple.Create(actor, (visibility[0] & 0x20) != 0 && (visibility[1] & 0x08) == 0);
        });
        // Inventory identity and a bit for each recognized card, independent of localization.
        vars.readCards = (Func<long, Tuple<long, int>>)(instance => {
            long inventory = vars.readInventory(instance);
            if (inventory == 0) return null;
            byte[] header = null, data = null, after = null;
            int count = 0;
            bool stable = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!process.ReadBytes((IntPtr)(inventory + 0xF8), 16, out header)) return null;
                long items = BitConverter.ToInt64(header, 0);
                count = BitConverter.ToInt32(header, 8);
                int capacity = BitConverter.ToInt32(header, 12);
                if (count < 0 || count > 256 || capacity < count || capacity > 4096 || (count > 0 && items == 0)) return null;
                if (count > 0 && !process.ReadBytes((IntPtr)items, count * 8, out data)) return null;
                if (!process.ReadBytes((IntPtr)(inventory + 0xF8), 16, out after)) return null;
                // Retry once if the array grows while reading a pickup.
                if (header.SequenceEqual(after)) { stable = true; break; }
            }
            if (!stable) return null;
            int mask = 0;
            string[] rows = vars.cardRows;
            for (int i = 0; i < count; i++)
            {
                uint nameId = BitConverter.ToUInt32(data, i * 8);
                uint number = BitConverter.ToUInt32(data, i * 8 + 4);
                string name = vars.readName(nameId);
                if (name == null) return null;
                if (number != 0) continue;
                int index = Array.IndexOf(rows, name);
                if (index >= 0) mask |= 1 << index;
            }
            return Tuple.Create(inventory, mask);
        });
        vars.readFingerprint = (Func<long, Tuple<long, bool>>)(instance => {
            long inventory = vars.readInventory(instance);
            if (inventory == 0) return null;
            byte[] header = null, data = null, after = null;
            bool stable = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!process.ReadBytes((IntPtr)(inventory + 0x120), 16, out header)) return null;
                long slots = BitConverter.ToInt64(header, 0);
                int count = BitConverter.ToInt32(header, 8), capacity = BitConverter.ToInt32(header, 12);
                // The game preallocates four row handles even when no fragments are owned.
                if (slots == 0 || count != 4 || capacity < count || capacity > 64) return null;
                if (!process.ReadBytes((IntPtr)slots, 64, out data)
                    || !process.ReadBytes((IntPtr)(inventory + 0x120), 16, out after)) return null;
                if (header.SequenceEqual(after)) { stable = true; break; }
            }
            if (!stable) return null;
            long table = BitConverter.ToInt64(data, 0);
            if (!vars.isClass(table, "DataTable") || vars.objectName(table) != "DT_Fingerprint") return null;
            bool complete = true;
            for (int i = 0; i < 4; i++)
            {
                if (BitConverter.ToInt64(data, i * 16) != table) return null;
                string row = vars.readName(BitConverter.ToUInt32(data, i * 16 + 8));
                if (row == null) return null;
                uint number = BitConverter.ToUInt32(data, i * 16 + 12);
                // Jonny_0 through Jonny_3: FName stores the numeric suffix plus one.
                if (row != "Jonny" || number != i + 1) complete = false;
            }
            return Tuple.Create(inventory, complete);
        });
        // Pawn identity, final cinematic selected, playback active. Unlocking is unrelated.
        vars.readEnding = (Func<long, Tuple<long, bool, bool>>)(instance => {
            long pawn = vars.readPawn(instance);
            byte cinematic = 0;
            if (pawn == 0 || !process.ReadValue<byte>((IntPtr)(pawn + 0x81B), out cinematic)) return null;
            if (cinematic != 2) return Tuple.Create(pawn, false, false);
            long actor = 0, sequence = 0, sequencePlayer = 0;
            byte status = 0;
            if (!process.ReadValue<long>((IntPtr)(pawn + 0x778), out actor)) return null;
            if (actor == 0) return Tuple.Create(pawn, true, false);
            if (!vars.isClass(actor, "LevelSequenceActor")
                || !process.ReadValue<long>((IntPtr)(actor + 0x2F8), out sequence)
                || !process.ReadValue<long>((IntPtr)(actor + 0x2F0), out sequencePlayer)) return null;
            if (sequencePlayer == 0) return Tuple.Create(pawn, true, false);
            if (!process.ReadValue<byte>((IntPtr)(sequencePlayer + 0x290), out status)) return null;
            string sequenceName = vars.objectName(sequence);
            if (sequenceName == null || status > 6) return null;
            // Playback status 1 is Playing; a prepared or stopped sequence is insufficient.
            return Tuple.Create(pawn, true, sequenceName == "Cinematic_End" && status == 1);
        });
    });
}

init
{
    if (modules.First().ModuleMemorySize != 164438016)
        throw new Exception("Unsupported Rivage build. Verify the executable before using this autosplitter.");
    using (var algorithm = System.Security.Cryptography.SHA256.Create())
    using (var stream = System.IO.File.OpenRead(modules.First().FileName))
    {
        string hash = BitConverter.ToString(algorithm.ComputeHash(stream)).Replace("-", "");
        if (hash != "2AEFBF42C85A52F241BD1ADC08ADE906267A6CF45F1E54CBED1888C05DEBC553")
            throw new Exception("Unsupported Rivage executable. Memory offsets need validation for this build.");
    }
    version = "Steam UE5.7 / 2AEFBF42";
    vars.moduleBase = modules.First().BaseAddress.ToInt64();
    vars.namesBase = (long)vars.moduleBase + 0x913EA80;
    vars.nameCache.Clear();
    vars.cardSeen = 0;
    vars.fingerprintSeen = false;
    vars.drawingSeen = 0;
    vars.initializeTracking();
    vars.initializeReaders(game);
    refreshRate = 60;
}

update
{
    var sample = (Tuple<long, bool, bool, bool>)vars.readRunState();
    bool? loadingScreen = vars.readLoadingScreen();
    if (sample == null || !loadingScreen.HasValue)
    {
        vars.initializeTracking();
        throw new System.IO.IOException("Waiting for readable Rivage game state.");
    }
    if (sample.Item1 != vars.instance)
    {
        vars.initializeTracking();
        vars.instance = sample.Item1;
    }
    if (vars.startPending > 0) vars.startPending--;
    if (vars.finalPending > 0) vars.finalPending--;
    // Resume on the first sample after the actual loading screen closes, including logos/warnings.
    vars.loading = loadingScreen.Value;
    if (sample.Item2)
    {
        // Returning to the main menu arms a new attempt without resetting the timer.
        if (!vars.loading) vars.menuArmed = true;
        vars.startWaitingForLoad = false;
        vars.startLoadObserved = false;
        vars.startLoadsRemaining = 0;
        vars.startPending = 0;
        vars.finalCompleted = false;
    }
    else if (vars.menuArmed)
    {
        vars.menuArmed = false;
        if (sample.Item3 && sample.Item4)
        {
            // Latch New Game before travel; the visible loading screen may begin later.
            vars.startWaitingForLoad = true;
            vars.startLoadObserved = false;
            vars.startLoadsRemaining = settings["start_after_cutscene"] ? 2 : 1;
            vars.cardSeen = 0;
            vars.fingerprintSeen = false;
            vars.drawingSeen = 0;
        }
    }
    if (vars.startWaitingForLoad)
    {
        if (vars.loading) vars.startLoadObserved = true;
        else if (vars.startLoadObserved)
        {
            // Count each observed loading screen once, on its active-to-inactive edge.
            vars.startLoadObserved = false;
            vars.startLoadsRemaining--;
            if (vars.startLoadsRemaining == 0)
            {
                // Start both timing methods without the native flag's two-second tail.
                vars.startWaitingForLoad = false;
                vars.startPending = 2;
            }
        }
    }
    if (sample.Item2 || vars.loading)
    {
        vars.endingSeeded = false;
        vars.endingPawn = 0L;
        vars.finalPending = 0;
    }
    else if (!vars.finalCompleted)
    {
        var ending = (Tuple<long, bool, bool>)vars.readEnding(sample.Item1);
        if (ending == null)
        {
            // An unreadable or replaced pawn cannot manufacture an opening edge.
            vars.endingSeeded = false;
            vars.endingPawn = 0L;
        }
        else
        {
            if (!vars.endingSeeded || vars.endingPawn != ending.Item1) vars.endingArmed = false;
            if (!ending.Item2) vars.endingArmed = true;
            if (vars.endingArmed && ending.Item3)
            {
                vars.finalPending = 2;
                vars.finalCompleted = true;
            }
            vars.endingPawn = ending.Item1;
            vars.endingSeeded = true;
        }
    }

    int enabledCards = 0;
    string[] cardSettings = vars.cardSettings;
    for (int i = 0; i < cardSettings.Length; i++)
        if (settings[cardSettings[i]]) enabledCards |= 1 << i;
    if (sample.Item2 || vars.loading || vars.finalCompleted || enabledCards == 0)
        vars.clearCardSample();
    else
    {
        if (vars.cardPendingAge > 0) vars.cardPendingAge--;
        if (vars.cardPendingAge == 0) vars.cardPending = 0;
        var cards = (Tuple<long, int>)vars.readCards(sample.Item1);
        if (cards == null) vars.clearCardSample();
        else
        {
            if (!vars.cardSeeded || vars.cardInventory != cards.Item1)
                vars.clearCardSample();
            else
            {
                int added = cards.Item2 & ~(int)vars.cardPrevious & ~(int)vars.cardSeen & enabledCards;
                if (added != 0)
                {
                    vars.cardPending = (int)vars.cardPending | added;
                    vars.cardPendingAge = 2;
                }
            }
            // Baselines and disabled cards are consumed too; neither may split later.
            vars.cardSeen = (int)vars.cardSeen | cards.Item2;
            vars.cardPrevious = cards.Item2;
            vars.cardInventory = cards.Item1;
            vars.cardSeeded = true;
        }
    }
    if (sample.Item2 || vars.loading || vars.finalCompleted || !settings["fingerprint_jonny"])
        vars.clearFingerprintSample();
    else
    {
        if (vars.fingerprintPending > 0) vars.fingerprintPending--;
        var fingerprint = (Tuple<long, bool>)vars.readFingerprint(sample.Item1);
        if (fingerprint == null) vars.clearFingerprintSample();
        else
        {
            if (!vars.fingerprintSeeded || vars.fingerprintInventory != fingerprint.Item1)
                vars.clearFingerprintSample();
            else if (!vars.fingerprintSeen && fingerprint.Item2)
                vars.fingerprintPending = 2;
            if (fingerprint.Item2) vars.fingerprintSeen = true;
            vars.fingerprintInventory = fingerprint.Item1;
            vars.fingerprintSeeded = true;
        }
    }
    if (vars.drawingPendingAge > 0) vars.drawingPendingAge--;
    if (vars.drawingPendingAge == 0) vars.drawingPending = 0;
    string[] drawingSettings = vars.drawingSettings;
    for (int i = 0; i < drawingSettings.Length; i++)
    {
        if (sample.Item2 || vars.loading || vars.finalCompleted || !settings[drawingSettings[i]])
        {
            vars.clearDrawingSample(i);
            if (i == 2) vars.clearHorizonReference();
            continue;
        }
        var drawing = (Tuple<long, bool>)(i == 0 ? vars.readLobbyDrawing(sample.Item1)
            : i == 1 ? vars.readLaboratoryDrawing(sample.Item1) : vars.readHorizonProjection(sample.Item1));
        if (drawing == null) { vars.clearDrawingSample(i); continue; }
        long[] owners = vars.drawingOwners;
        bool[] seeded = vars.drawingSeeded, previous = vars.drawingPrevious;
        int bit = 1 << i;
        if (!seeded[i] || owners[i] != drawing.Item1) vars.clearDrawingSample(i);
        else if (!previous[i] && drawing.Item2 && (((int)vars.drawingSeen & bit) == 0))
        {
            vars.drawingPending = (int)vars.drawingPending | bit;
            vars.drawingPendingAge = 2;
        }
        if (drawing.Item2) vars.drawingSeen = (int)vars.drawingSeen | bit;
        owners[i] = drawing.Item1; seeded[i] = true; previous[i] = drawing.Item2;
    }
    return true;
}

start
{
    if (vars.startPending <= 0) return false;
    vars.startPending = 0;
    return true;
}

split
{
    if (vars.finalPending > 0)
    {
        vars.finalPending = 0;
        vars.clearCardSample();
        vars.clearFingerprintSample();
        for (int i = 0; i < ((string[])vars.drawingSettings).Length; i++) vars.clearDrawingSample(i);
        return true;
    }
    string[] drawingSettings = vars.drawingSettings;
    for (int i = 0; i < drawingSettings.Length; i++)
    {
        int bit = 1 << i;
        if (((int)vars.drawingPending & bit) == 0) continue;
        vars.drawingPending = (int)vars.drawingPending & ~bit;
        if (!settings[drawingSettings[i]]) continue;
        vars.drawingPendingAge = 2;
        if (vars.fingerprintPending > 0) vars.fingerprintPending = 2;
        if (vars.cardPending > 0) vars.cardPendingAge = 2;
        return true;
    }
    if (vars.fingerprintPending > 0)
    {
        vars.fingerprintPending = 0;
        // Preserve a simultaneous card event, including after host recovery seeding.
        if (vars.cardPending > 0) vars.cardPendingAge = 2;
        return settings["fingerprint_jonny"];
    }
    string[] cardSettings = vars.cardSettings;
    for (int i = 0; i < cardSettings.Length; i++)
    {
        int bit = 1 << i;
        if (((int)vars.cardPending & bit) == 0) continue;
        vars.cardPending = (int)vars.cardPending & ~bit;
        if (!settings[cardSettings[i]]) continue;
        // Drain simultaneous pickups one per tick, but expire unconsumed events.
        vars.cardPendingAge = 2;
        return true;
    }
    return false;
}

isLoading
{
    return vars.loading;
}
