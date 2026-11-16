using System.Collections.Generic;
using UnityEngine;

// PROCEDURAL CONTENT GENERATION (berbasis aturan + validasi), dikendalikan DifficultyProfile.
//
// Tahapan:
//  1. BSP membagi area menjadi N ruangan (N dari DDA).
//  2. Graf ruangan: spanning tree + loop tambahan (jumlah loop dari DDA).
//  3. Peran ruangan: Start, Brankas (daun terjauh, satu pintu terkunci), Keycard (kedalaman dari DDA),
//     Ekstraksi (dekat/jauh dari DDA), Panel listrik.
//  4. Prop/cover sesuai tema ruangan, kepadatan dari DDA. Constraint: tidak menutup pintu dan
//     tidak memutus ruangan (flood fill setiap penempatan).
//  5. Penjaga (jumlah, rute antar-ruangan) & CCTV dari DDA. Constraint: jauh dari titik start.
//  6. VALIDASI: konektivitas, lock-and-key (keycard bisa diambil sebelum pintu terkunci),
//     dan "jalur stealth" (Dijkstra di atas peta sel yang diawasi penjaga/CCTV) harus memiliki
//     eksposur <= batas dari DDA. Jika gagal: populasi diulang, lalu layout diulang.
public static class LayoutGenerator
{
    private const int MinRoomSize = 4;
    private const int MaxLayoutAttempts = 12;
    private const int MaxPopulationAttempts = 10;

    // Sel dengan intensitas pengawasan >= ini dihitung "terekspos".
    public const float WatchThreshold = 0.35f;

    private static readonly Vector2Int[] Dirs =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1)
    };

    private struct Candidate
    {
        public int a, b;
        public List<(Vector2Int ca, Vector2Int cb)> slots;
    }

    public static LevelLayout Generate(int seed, DifficultyProfile profile)
    {
        List<string> history = new List<string>();
        LevelLayout last = null;
        int totalPopulation = 0;
        int attemptCounter = 0;

        // Jika tidak ada yang lolos, batas eksposur dilonggarkan bertahap (tetap tervalidasi).
        for (int relax = 0; relax < 3; relax++)
        {
            int budget = profile.maxExposedCells + relax * 4;

            for (int attempt = 0; attempt < MaxLayoutAttempts; attempt++)
            {
                attemptCounter++;
                System.Random rng = new System.Random(seed + attemptCounter * 7919);
                LevelLayout layout = new LevelLayout { seed = seed, exposureBudget = budget };
                string reason;

                if (!BuildStructure(layout, rng, profile, out reason))
                {
                    history.Add("Layout #" + attemptCounter + " ditolak: " + reason);
                    continue;
                }

                for (int pop = 0; pop < MaxPopulationAttempts; pop++)
                {
                    totalPopulation++;
                    ClearPopulation(layout);

                    if (!Populate(layout, rng, profile, out reason))
                        continue;

                    last = layout;

                    if (Validate(layout, profile, out reason))
                    {
                        layout.valid = true;
                        layout.layoutAttempts = attemptCounter;
                        layout.populationAttempts = totalPopulation;
                        layout.log.AddRange(history);
                        layout.log.Add("Layout #" + attemptCounter + " DITERIMA (populasi ke-" + (pop + 1) +
                                       ", eksposur " + layout.exposure + "/" + budget + ")");
                        return layout;
                    }
                }

                history.Add("Layout #" + attemptCounter + " ditolak: " + reason);
            }

            history.Add("Batas eksposur dilonggarkan menjadi " + (profile.maxExposedCells + (relax + 1) * 4));
        }

        if (last != null)
        {
            last.layoutAttempts = attemptCounter;
            last.populationAttempts = totalPopulation;
            last.log.AddRange(history);
            last.log.Add("PERINGATAN: tidak ada layout yang lolos penuh, dipakai yang terakhir");
            Debug.LogWarning("[PCG] Tidak ada layout yang lolos validasi.");
        }

        return last;
    }

    // ==================================================
    // 1-4. STRUKTUR
    // ==================================================

    private static bool BuildStructure(LevelLayout layout, System.Random rng, DifficultyProfile profile, out string reason)
    {
        reason = null;

        int area = profile.roomCount * 32;
        int w = Mathf.CeilToInt(Mathf.Sqrt(area * 1.45f));
        int h = Mathf.CeilToInt(area / (float)w);
        w = Mathf.Max(w, MinRoomSize * 2);
        h = Mathf.Max(h, MinRoomSize * 2);

        layout.width = w;
        layout.height = h;
        layout.roomOf = new int[w, h];
        layout.blocked = new bool[w, h];
        layout.reserved = new bool[w, h];

        // ---------- BSP ----------
        List<RectInt> leaves = new List<RectInt> { new RectInt(0, 0, w, h) };

        while (leaves.Count < profile.roomCount)
        {
            leaves.Sort((x, y) => (y.width * y.height).CompareTo(x.width * x.height));
            int index = leaves.FindIndex(r => r.width >= MinRoomSize * 2 || r.height >= MinRoomSize * 2);

            if (index < 0)
                break;

            RectInt leaf = leaves[index];
            bool canX = leaf.width >= MinRoomSize * 2;
            bool canY = leaf.height >= MinRoomSize * 2;
            bool cutX = canX && (!canY || leaf.width > leaf.height || (leaf.width == leaf.height && rng.Next(2) == 0));

            leaves.RemoveAt(index);

            if (cutX)
            {
                int cut = rng.Next(MinRoomSize, leaf.width - MinRoomSize + 1);
                leaves.Add(new RectInt(leaf.x, leaf.y, cut, leaf.height));
                leaves.Add(new RectInt(leaf.x + cut, leaf.y, leaf.width - cut, leaf.height));
            }
            else
            {
                int cut = rng.Next(MinRoomSize, leaf.height - MinRoomSize + 1);
                leaves.Add(new RectInt(leaf.x, leaf.y, leaf.width, cut));
                leaves.Add(new RectInt(leaf.x, leaf.y + cut, leaf.width, leaf.height - cut));
            }
        }

        if (leaves.Count < 3)
        {
            reason = "BSP menghasilkan terlalu sedikit ruangan";
            return false;
        }

        for (int i = 0; i < leaves.Count; i++)
        {
            LayoutRoom room = new LayoutRoom { id = i, rect = leaves[i] };
            layout.rooms.Add(room);

            for (int x = room.rect.xMin; x < room.rect.xMax; x++)
                for (int y = room.rect.yMin; y < room.rect.yMax; y++)
                    layout.roomOf[x, y] = i;
        }

        // ---------- Graf ruangan ----------
        List<Candidate> candidates = FindAdjacency(layout);

        int[] parent = new int[layout.rooms.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }

        Shuffle(candidates, rng);
        List<Candidate> tree = new List<Candidate>();
        List<Candidate> spare = new List<Candidate>();

        foreach (Candidate c in candidates)
        {
            int ra = Find(c.a), rb = Find(c.b);

            if (ra != rb)
            {
                parent[ra] = rb;
                tree.Add(c);
            }
            else
            {
                spare.Add(c);
            }
        }

        if (tree.Count != layout.rooms.Count - 1)
        {
            reason = "ruangan tidak terhubung (graf BSP terputus)";
            return false;
        }

        foreach (Candidate c in tree)
            AddDoor(layout, c, rng);

        // ---------- Start: ruangan di salah satu pojok peta ----------
        Vector2Int[] corners = { new Vector2Int(0, 0), new Vector2Int(w - 1, 0), new Vector2Int(0, h - 1), new Vector2Int(w - 1, h - 1) };
        Vector2Int corner = corners[rng.Next(corners.Length)];
        layout.startRoom = layout.roomOf[corner.x, corner.y];

        // ---------- Brankas: daun pohon terjauh dari start ----------
        int[] treeDist = RoomDistances(layout, layout.startRoom, true);
        int vault = -1;

        for (int i = 0; i < layout.rooms.Count; i++)
        {
            if (i == layout.startRoom || layout.rooms[i].doors.Count != 1)
                continue;

            if (vault < 0 || treeDist[i] > treeDist[vault])
                vault = i;
        }

        if (vault < 0)
        {
            reason = "tidak ada ruangan daun untuk brankas";
            return false;
        }

        layout.vaultRoom = vault;
        layout.doors[layout.rooms[vault].doors[0]].locked = true;

        // ---------- Loop tambahan (jalur alternatif), tidak menyentuh brankas ----------
        Shuffle(spare, rng);
        int loops = 0;

        foreach (Candidate c in spare)
        {
            if (loops >= profile.extraLoops)
                break;

            if (c.a == vault || c.b == vault)
                continue;

            AddDoor(layout, c, rng);
            loops++;
        }

        // ---------- Peran ruangan ----------
        int[] dist = RoomDistances(layout, layout.startRoom, false);

        int keyRoom = -1;
        int bestKeyScore = int.MaxValue;

        for (int i = 0; i < layout.rooms.Count; i++)
        {
            if (i == vault || i == layout.startRoom || dist[i] < 0)
                continue;

            int score = Mathf.Abs(dist[i] - profile.keycardDepth) * 10 + rng.Next(5);

            if (score < bestKeyScore)
            {
                bestKeyScore = score;
                keyRoom = i;
            }
        }

        if (keyRoom < 0)
        {
            reason = "tidak ada ruangan untuk keycard";
            return false;
        }

        layout.keyRoom = keyRoom;

        layout.extractionRoom = layout.startRoom;

        if (profile.extractionFar)
        {
            int far = -1;

            for (int i = 0; i < layout.rooms.Count; i++)
            {
                if (i == vault || i == layout.startRoom || dist[i] < 0)
                    continue;

                int score = dist[i] * 10 + (i == keyRoom ? -5 : 0);

                if (far < 0 || score > dist[far] * 10 + (far == keyRoom ? -5 : 0))
                    far = i;
            }

            if (far >= 0)
                layout.extractionRoom = far;
        }

        AssignThemes(layout, rng);

        // ---------- Reservasi area pintu ----------
        foreach (LayoutDoor door in layout.doors)
        {
            Reserve(layout, door.cellA);
            Reserve(layout, door.cellB);
            Reserve(layout, door.cellA + (door.cellA - door.cellB));
            Reserve(layout, door.cellB + (door.cellB - door.cellA));
        }

        // ---------- Objek misi ----------
        LayoutDoor locked = layout.LockedDoor;
        layout.terminal = PickWallMount(layout, vault, rng, locked.CellIn(vault), true);

        if (layout.terminal == null)
        {
            reason = "tidak ada dinding untuk terminal";
            return false;
        }

        layout.blocked[layout.terminal.cell.x, layout.terminal.cell.y] = true;
        Reserve(layout, layout.terminal.cell);
        Reserve(layout, layout.terminal.Front);

        if (!PickInteriorCell(layout, layout.startRoom, rng, null, out layout.playerStart))
        {
            reason = "tidak ada sel start";
            return false;
        }

        ReserveArea(layout, layout.playerStart);

        if (!PickInteriorCell(layout, layout.extractionRoom, rng, layout.playerStart, out layout.extraction))
        {
            reason = "tidak ada sel ekstraksi";
            return false;
        }

        ReserveArea(layout, layout.extraction);

        if (!PickFreeCell(layout, keyRoom, rng, out layout.keycard))
        {
            reason = "tidak ada sel keycard";
            return false;
        }

        Reserve(layout, layout.keycard);

        // Panel listrik: di ruangan non-brankas, mengendalikan ruangan itu + tetangga terbesarnya.
        List<int> powerCandidates = new List<int>();
        for (int i = 0; i < layout.rooms.Count; i++)
            if (i != vault && i != layout.startRoom) powerCandidates.Add(i);

        if (powerCandidates.Count == 0)
            powerCandidates.Add(layout.startRoom);

        layout.powerRoom = powerCandidates[rng.Next(powerCandidates.Count)];
        layout.powerBox = PickWallMount(layout, layout.powerRoom, rng, null, false);

        if (layout.powerBox != null)
        {
            Reserve(layout, layout.powerBox.cell);
            Reserve(layout, layout.powerBox.Front);
        }

        layout.poweredRooms.Add(layout.powerRoom);
        int biggestNeighbour = -1;

        foreach (int doorId in layout.rooms[layout.powerRoom].doors)
        {
            int other = layout.doors[doorId].Other(layout.powerRoom);

            if (other != vault && (biggestNeighbour < 0 || layout.rooms[other].Area > layout.rooms[biggestNeighbour].Area))
                biggestNeighbour = other;
        }

        if (biggestNeighbour >= 0)
            layout.poweredRooms.Add(biggestNeighbour);

        // ---------- Ventilasi (jalur pintas pemain) + obeng ----------
        PlaceVents(layout, rng, profile);

        // ---------- Prop / cover ----------
        PlaceProps(layout, rng, profile);

        // ---------- Resource: kaleng ----------
        List<int> canRooms = new List<int> { layout.startRoom };
        foreach (int doorId in layout.rooms[layout.startRoom].doors)
            canRooms.Add(layout.doors[doorId].Other(layout.startRoom));
        for (int i = 0; i < layout.rooms.Count; i++)
            if (i != vault && !canRooms.Contains(i)) canRooms.Add(i);

        for (int i = 0; i < profile.canCount; i++)
        {
            int room = canRooms[i % canRooms.Count];

            if (PickFreeCell(layout, room, rng, out Vector2Int cell))
            {
                layout.cans.Add(cell);
                Reserve(layout, cell);
            }
        }

        return true;
    }

    private static List<Candidate> FindAdjacency(LevelLayout layout)
    {
        List<Candidate> result = new List<Candidate>();

        for (int i = 0; i < layout.rooms.Count; i++)
        {
            for (int j = i + 1; j < layout.rooms.Count; j++)
            {
                RectInt a = layout.rooms[i].rect;
                RectInt b = layout.rooms[j].rect;
                List<(Vector2Int, Vector2Int)> slots = new List<(Vector2Int, Vector2Int)>();

                if (a.xMax == b.xMin || b.xMax == a.xMin)
                {
                    bool aLeft = a.xMax == b.xMin;
                    int lo = Mathf.Max(a.yMin, b.yMin), hi = Mathf.Min(a.yMax, b.yMax);

                    for (int y = lo + 1; y <= hi - 2; y++)
                    {
                        Vector2Int ca = new Vector2Int(aLeft ? a.xMax - 1 : a.xMin, y);
                        Vector2Int cb = new Vector2Int(aLeft ? b.xMin : b.xMax - 1, y);
                        slots.Add((ca, cb));
                    }
                }
                else if (a.yMax == b.yMin || b.yMax == a.yMin)
                {
                    bool aBelow = a.yMax == b.yMin;
                    int lo = Mathf.Max(a.xMin, b.xMin), hi = Mathf.Min(a.xMax, b.xMax);

                    for (int x = lo + 1; x <= hi - 2; x++)
                    {
                        Vector2Int ca = new Vector2Int(x, aBelow ? a.yMax - 1 : a.yMin);
                        Vector2Int cb = new Vector2Int(x, aBelow ? b.yMin : b.yMax - 1);
                        slots.Add((ca, cb));
                    }
                }

                if (slots.Count > 0)
                    result.Add(new Candidate { a = i, b = j, slots = slots });
            }
        }

        return result;
    }

    private static void AddDoor(LevelLayout layout, Candidate c, System.Random rng)
    {
        // Pilih slot di sekitar tengah dinding bersama.
        int mid = c.slots.Count / 2;
        int spread = Mathf.Max(1, c.slots.Count / 3);
        int index = Mathf.Clamp(mid + rng.Next(-spread, spread + 1), 0, c.slots.Count - 1);

        LayoutDoor door = new LayoutDoor
        {
            id = layout.doors.Count,
            roomA = c.a,
            roomB = c.b,
            cellA = c.slots[index].ca,
            cellB = c.slots[index].cb
        };

        layout.doors.Add(door);
        layout.rooms[c.a].doors.Add(door.id);
        layout.rooms[c.b].doors.Add(door.id);
    }

    private static int[] RoomDistances(LevelLayout layout, int from, bool includeLocked)
    {
        int[] dist = new int[layout.rooms.Count];
        for (int i = 0; i < dist.Length; i++) dist[i] = -1;

        Queue<int> queue = new Queue<int>();
        dist[from] = 0;
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            int room = queue.Dequeue();

            foreach (int doorId in layout.rooms[room].doors)
            {
                LayoutDoor door = layout.doors[doorId];

                if (door.locked && !includeLocked)
                    continue;

                int other = door.Other(room);

                if (dist[other] >= 0)
                    continue;

                dist[other] = dist[room] + 1;
                queue.Enqueue(other);
            }
        }

        return dist;
    }

    private static void AssignThemes(LevelLayout layout, System.Random rng)
    {
        int largest = -1;

        for (int i = 0; i < layout.rooms.Count; i++)
        {
            if (i == layout.startRoom || i == layout.vaultRoom)
                continue;

            if (largest < 0 || layout.rooms[i].Area > layout.rooms[largest].Area)
                largest = i;
        }

        RoomTheme[] pool = { RoomTheme.Hall, RoomTheme.Office, RoomTheme.Storage, RoomTheme.Office, RoomTheme.Storage, RoomTheme.Server };
        Dictionary<RoomTheme, int> counts = new Dictionary<RoomTheme, int>();

        foreach (LayoutRoom room in layout.rooms)
        {
            if (room.id == layout.startRoom) room.theme = RoomTheme.Dock;
            else if (room.id == layout.vaultRoom) room.theme = RoomTheme.Server;
            else if (room.id == largest) room.theme = RoomTheme.Hall;
            else room.theme = pool[rng.Next(pool.Length)];

            counts.TryGetValue(room.theme, out int n);
            counts[room.theme] = n + 1;

            string baseName = room.id == layout.vaultRoom ? "Brankas Data" : PropCatalog.ThemeName(room.theme);
            room.name = n > 0 ? baseName + " " + (char)('A' + n) : baseName;
        }
    }

    // ==================================================
    // VENTILASI
    // ==================================================

    // Saluran di luar dinding batas: masuk dekat start, keluar di ruangan yang lebih dalam/penting.
    // Constraint: tidak ke brankas (lock-and-key tetap utuh), selisih kedalaman >= 2 ruangan,
    // lubang tidak di pojok/area pintu, saluran tidak saling tumpang tindih.
    // Peluang ada ventilasi & peluang berbahaya (gas/laser) diatur difficulty.
    private static void PlaceVents(LevelLayout layout, System.Random rng, DifficultyProfile profile)
    {
        float chance = Mathf.Lerp(0.85f, 0.5f, profile.difficulty);
        float hazardChance = Mathf.Lerp(0.15f, 0.65f, profile.difficulty);
        int[] dist = RoomDistances(layout, layout.startRoom, false);
        int outsideVault = layout.LockedDoor.Other(layout.vaultRoom);

        for (int v = 0; v < profile.ventCount; v++)
        {
            if (rng.NextDouble() > chance)
                continue;

            float bestScore = float.MinValue;
            LayoutVent best = null;

            foreach (Vector2Int outward in Dirs)
            {
                List<Vector2Int> edge = BoundaryCells(layout, outward);

                for (int i = 0; i < edge.Count; i++)
                {
                    for (int j = 0; j < edge.Count; j++)
                    {
                        Vector2Int a = edge[i], b = edge[j];
                        int ra = layout.roomOf[a.x, a.y], rb = layout.roomOf[b.x, b.y];

                        if (ra == rb || ra == layout.vaultRoom || rb == layout.vaultRoom || dist[ra] < 0 || dist[rb] < 0)
                            continue;

                        if (dist[rb] - dist[ra] < 2 || Manhattan(a, b) < 3 || Manhattan(a, b) > 14)
                            continue;

                        if (!VentCellOk(layout, a, outward) || !VentCellOk(layout, b, outward) || VentOverlaps(layout, a, b, outward))
                            continue;

                        float score =
                            -dist[ra] * 2f
                            + (rb == layout.keyRoom || rb == outsideVault ? 6f : 0f)
                            + (rb == layout.extractionRoom ? 3f : 0f)
                            + (dist[rb] - dist[ra]) * 1.5f
                            - Manhattan(a, b) * 0.2f
                            + (float)rng.NextDouble() * 2f;

                        if (score > bestScore)
                        {
                            bestScore = score;
                            best = new LayoutVent { roomA = ra, roomB = rb, cellA = a, cellB = b, outward = outward };
                        }
                    }
                }
            }

            if (best == null)
                break;

            if (rng.NextDouble() < hazardChance)
                best.hazard = rng.Next(2) == 0 ? VentHazardType.Toxic : VentHazardType.Laser;

            layout.vents.Add(best);
            Reserve(layout, best.cellA);
            Reserve(layout, best.cellB);
            Reserve(layout, best.cellA - best.outward);
            Reserve(layout, best.cellB - best.outward);
        }

        if (layout.vents.Count == 0)
            return;

        // Obeng: ruangan dengan kedalaman ~screwdriverDepth (dapat dicapai tanpa keycard).
        int room = -1, bestRoomScore = int.MaxValue;

        for (int i = 0; i < layout.rooms.Count; i++)
        {
            if (i == layout.vaultRoom || dist[i] < 0)
                continue;

            int score = Mathf.Abs(dist[i] - profile.screwdriverDepth) * 10 + rng.Next(5);
            if (score < bestRoomScore) { bestRoomScore = score; room = i; }
        }

        if (room >= 0 && PickFreeCell(layout, room, rng, out Vector2Int cell))
        {
            layout.screwdriver = cell;
            Reserve(layout, cell);
        }
        else
        {
            layout.vents.Clear(); // tanpa obeng, ventilasi tidak berguna
        }
    }

    private static List<Vector2Int> BoundaryCells(LevelLayout layout, Vector2Int outward)
    {
        List<Vector2Int> cells = new List<Vector2Int>();

        for (int x = 0; x < layout.width; x++)
        {
            for (int y = 0; y < layout.height; y++)
            {
                Vector2Int c = new Vector2Int(x, y);
                if (!layout.InBounds(c + outward)) cells.Add(c);
            }
        }

        return cells;
    }

    private static bool VentCellOk(LevelLayout layout, Vector2Int c, Vector2Int outward)
    {
        if (layout.blocked[c.x, c.y] || layout.reserved[c.x, c.y])
            return false;

        Vector2Int inward = c - outward;
        if (!layout.InBounds(inward) || layout.blocked[inward.x, inward.y] || layout.roomOf[inward.x, inward.y] != layout.roomOf[c.x, c.y])
            return false;

        // Bukan pojok ruangan (kedua tetangga sepanjang dinding masih ruangan yang sama).
        Vector2Int side = new Vector2Int(outward.y, outward.x);
        foreach (Vector2Int n in new[] { c + side, c - side })
            if (!layout.InBounds(n) || layout.roomOf[n.x, n.y] != layout.roomOf[c.x, c.y])
                return false;

        return true;
    }

    private static bool VentOverlaps(LevelLayout layout, Vector2Int a, Vector2Int b, Vector2Int outward)
    {
        bool alongX = outward.x == 0;
        int lo = alongX ? Mathf.Min(a.x, b.x) : Mathf.Min(a.y, b.y);
        int hi = alongX ? Mathf.Max(a.x, b.x) : Mathf.Max(a.y, b.y);

        foreach (LayoutVent v in layout.vents)
        {
            if (v.outward != outward)
                continue;

            int vlo = alongX ? Mathf.Min(v.cellA.x, v.cellB.x) : Mathf.Min(v.cellA.y, v.cellB.y);
            int vhi = alongX ? Mathf.Max(v.cellA.x, v.cellB.x) : Mathf.Max(v.cellA.y, v.cellB.y);

            if (lo <= vhi + 1 && vlo <= hi + 1)
                return true;
        }

        return false;
    }

    // ==================================================
    // PROPS (constraint: tidak menutup pintu & tidak memutus ruangan)
    // ==================================================

    private static void PlaceProps(LevelLayout layout, System.Random rng, DifficultyProfile profile)
    {
        foreach (LayoutRoom room in layout.rooms)
        {
            PropKind[] kinds = PropCatalog.ForTheme(room.theme);
            int freeCells = CountFree(layout, room);
            int target = Mathf.RoundToInt(freeCells * profile.coverDensity * 0.24f);

            if (room.id == layout.startRoom)
                target = Mathf.Min(target, 3);

            int covered = 0;

            for (int attempt = 0; attempt < 60 && covered < target; attempt++)
            {
                PropKind kind = kinds[rng.Next(kinds.Length)];
                Vector2Int fp = PropCatalog.Footprint(kind);
                bool rotated = fp.x > 1 && rng.Next(2) == 0;
                int fw = rotated ? fp.y : fp.x;
                int fh = rotated ? fp.x : fp.y;

                if (room.rect.width < fw || room.rect.height < fh)
                    continue;

                int x = rng.Next(room.rect.xMin, room.rect.xMax - fw + 1);
                int y = rng.Next(room.rect.yMin, room.rect.yMax - fh + 1);
                RectInt cells = new RectInt(x, y, fw, fh);

                if (!AreaFree(layout, cells, room.id))
                    continue;

                SetBlocked(layout, cells, true);

                if (!RoomStaysConnected(layout, room))
                {
                    SetBlocked(layout, cells, false);
                    layout.rejectedProps++;
                    continue;
                }

                layout.props.Add(new LayoutProp { kind = kind, cells = cells, rotated = rotated });
                covered += fw * fh;
            }
        }
    }

    private static bool AreaFree(LevelLayout layout, RectInt cells, int room)
    {
        for (int x = cells.xMin; x < cells.xMax; x++)
            for (int y = cells.yMin; y < cells.yMax; y++)
                if (layout.roomOf[x, y] != room || layout.blocked[x, y] || layout.reserved[x, y])
                    return false;

        return true;
    }

    private static void SetBlocked(LevelLayout layout, RectInt cells, bool value)
    {
        for (int x = cells.xMin; x < cells.xMax; x++)
            for (int y = cells.yMin; y < cells.yMax; y++)
                layout.blocked[x, y] = value;
    }

    private static bool RoomStaysConnected(LevelLayout layout, LayoutRoom room)
    {
        Vector2Int start = new Vector2Int(-1, -1);
        int total = 0;

        for (int x = room.rect.xMin; x < room.rect.xMax; x++)
        {
            for (int y = room.rect.yMin; y < room.rect.yMax; y++)
            {
                if (layout.blocked[x, y])
                    continue;

                total++;
                if (start.x < 0) start = new Vector2Int(x, y);
            }
        }

        if (start.x < 0)
            return false;

        HashSet<Vector2Int> seen = new HashSet<Vector2Int> { start };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();

            foreach (Vector2Int d in Dirs)
            {
                Vector2Int n = c + d;

                if (!room.rect.Contains(n) || layout.blocked[n.x, n.y] || !seen.Add(n))
                    continue;

                queue.Enqueue(n);
            }
        }

        return seen.Count == total;
    }

    private static int CountFree(LevelLayout layout, LayoutRoom room)
    {
        int n = 0;

        for (int x = room.rect.xMin; x < room.rect.xMax; x++)
            for (int y = room.rect.yMin; y < room.rect.yMax; y++)
                if (!layout.blocked[x, y] && !layout.reserved[x, y]) n++;

        return n;
    }

    // ==================================================
    // HELPERS PENEMPATAN
    // ==================================================

    private static void Reserve(LevelLayout layout, Vector2Int c)
    {
        if (layout.InBounds(c))
            layout.reserved[c.x, c.y] = true;
    }

    private static void ReserveArea(LevelLayout layout, Vector2Int c)
    {
        Reserve(layout, c);
        foreach (Vector2Int d in Dirs)
            Reserve(layout, c + d);
    }

    private static bool PickFreeCell(LevelLayout layout, int room, System.Random rng, out Vector2Int cell)
    {
        RectInt r = layout.rooms[room].rect;

        for (int i = 0; i < 80; i++)
        {
            Vector2Int c = new Vector2Int(rng.Next(r.xMin, r.xMax), rng.Next(r.yMin, r.yMax));

            if (!layout.blocked[c.x, c.y] && !layout.reserved[c.x, c.y])
            {
                cell = c;
                return true;
            }
        }

        cell = default;
        return false;
    }

    // Sel yang keempat tetangganya juga berada di ruangan yang sama (bukan menempel dinding).
    private static bool PickInteriorCell(LevelLayout layout, int room, System.Random rng, Vector2Int? avoid, out Vector2Int cell)
    {
        RectInt r = layout.rooms[room].rect;
        List<Vector2Int> options = new List<Vector2Int>();

        for (int x = r.xMin + 1; x < r.xMax - 1; x++)
        {
            for (int y = r.yMin + 1; y < r.yMax - 1; y++)
            {
                if (layout.blocked[x, y] || layout.reserved[x, y])
                    continue;

                Vector2Int c = new Vector2Int(x, y);

                if (avoid.HasValue && Manhattan(c, avoid.Value) < 3)
                    continue;

                options.Add(c);
            }
        }

        if (options.Count == 0)
        {
            cell = default;
            return false;
        }

        cell = options[rng.Next(options.Count)];
        return true;
    }

    // Sel menempel dinding (bukan pintu). farFrom = pilih yang paling jauh dari titik itu.
    private static WallMount PickWallMount(LevelLayout layout, int room, System.Random rng, Vector2Int? farFrom, bool preferFar)
    {
        RectInt r = layout.rooms[room].rect;
        List<WallMount> options = new List<WallMount>();

        for (int x = r.xMin; x < r.xMax; x++)
        {
            for (int y = r.yMin; y < r.yMax; y++)
            {
                Vector2Int c = new Vector2Int(x, y);

                if (layout.blocked[x, y] || layout.reserved[x, y])
                    continue;

                foreach (Vector2Int d in Dirs)
                {
                    Vector2Int n = c + d;
                    bool wall = !layout.InBounds(n) || layout.roomOf[n.x, n.y] != room;

                    if (!wall)
                        continue;

                    Vector2Int front = c - d;

                    if (!r.Contains(front) || layout.blocked[front.x, front.y])
                        continue;

                    // Hindari pojok (dua dinding) supaya panel/terminal tidak menempel pintu.
                    options.Add(new WallMount { cell = c, toWall = d });
                }
            }
        }

        if (options.Count == 0)
            return null;

        if (preferFar && farFrom.HasValue)
        {
            options.Sort((a, b) => Manhattan(b.cell, farFrom.Value).CompareTo(Manhattan(a.cell, farFrom.Value)));
            return options[rng.Next(Mathf.Min(3, options.Count))];
        }

        return options[rng.Next(options.Count)];
    }

    private static int Manhattan(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    // ==================================================
    // 5. POPULASI: penjaga, CCTV, panel alarm
    // ==================================================

    private static void ClearPopulation(LevelLayout layout)
    {
        foreach (WallMount alarm in layout.alarms)
            layout.reserved[alarm.cell.x, alarm.cell.y] = false;

        layout.guards.Clear();
        layout.cameras.Clear();
        layout.alarms.Clear();
        layout.stealthPath.Clear();
        layout.watched = null;
        layout.watch = null;
    }

    private static bool Populate(LevelLayout layout, System.Random rng, DifficultyProfile profile, out string reason)
    {
        reason = null;

        int[,] distFromStart = CellDistances(layout, layout.playerStart, true);
        HashSet<int> critical = CriticalRooms(layout);
        Dictionary<int, int> guardsPerRoom = new Dictionary<int, int>();
        LayoutDoor locked = layout.LockedDoor;
        int outsideVault = locked.Other(layout.vaultRoom);

        for (int g = 0; g < profile.guardCount; g++)
        {
            LayoutGuard guard = null;

            if (g == 0 && profile.vaultPost)
                guard = MakePostGuard(layout, rng, locked, outsideVault, distFromStart);

            for (int tries = 0; guard == null && tries < 25; tries++)
                guard = MakePatrolGuard(layout, rng, profile, critical, guardsPerRoom, distFromStart);

            if (guard == null)
            {
                reason = "tidak bisa membuat rute penjaga yang aman dari titik start";
                return false;
            }

            layout.guards.Add(guard);
            guardsPerRoom.TryGetValue(guard.room, out int n);
            guardsPerRoom[guard.room] = n + 1;
        }

        // ---------- CCTV di pojok ruangan penting ----------
        List<int> camRooms = new List<int>();
        foreach (int room in critical)
            if (room != layout.startRoom) camRooms.Add(room);
        for (int i = 0; i < layout.rooms.Count; i++)
            if (i != layout.startRoom && !camRooms.Contains(i)) camRooms.Add(i);

        Shuffle(camRooms, rng);

        for (int i = 0; i < profile.cameraCount && i < camRooms.Count; i++)
        {
            LayoutRoom room = layout.rooms[camRooms[i]];
            RectInt r = room.rect;
            Vector2Int[] corners =
            {
                new Vector2Int(r.xMin, r.yMin), new Vector2Int(r.xMax - 1, r.yMin),
                new Vector2Int(r.xMin, r.yMax - 1), new Vector2Int(r.xMax - 1, r.yMax - 1)
            };

            Vector2Int corner = corners[rng.Next(4)];
            Vector2 offset = new Vector2(corner.x == r.xMin ? -0.3f : 0.3f, corner.y == r.yMin ? -0.3f : 0.3f);
            Vector2 toCenter = room.Center - new Vector2(corner.x + 0.5f, corner.y + 0.5f);
            float yaw = Mathf.Atan2(toCenter.x, toCenter.y) * Mathf.Rad2Deg;

            if (Manhattan(corner, layout.playerStart) < 5)
                continue;

            layout.cameras.Add(new LayoutCamera { cell = corner, cornerOffset = offset, yaw = yaw, room = room.id });
        }

        // ---------- Panel alarm (dipakai perilaku Raise Alarm) ----------
        if (profile.raiseAlarm)
        {
            List<int> alarmRooms = new List<int>();

            foreach (LayoutGuard guard in layout.guards)
                if (!alarmRooms.Contains(guard.room)) alarmRooms.Add(guard.room);

            Shuffle(alarmRooms, rng);
            int alarmCount = Mathf.Clamp(layout.guards.Count / 2, 1, 3);

            for (int i = 0; i < alarmRooms.Count && layout.alarms.Count < alarmCount; i++)
            {
                WallMount mount = PickWallMount(layout, alarmRooms[i], rng, null, false);

                if (mount == null)
                    continue;

                layout.alarms.Add(mount);
                layout.reserved[mount.cell.x, mount.cell.y] = true;
            }
        }

        return true;
    }

    private static LayoutGuard MakePostGuard(LevelLayout layout, System.Random rng, LayoutDoor locked, int outsideRoom,
        int[,] distFromStart)
    {
        Vector2Int doorCell = locked.CellIn(outsideRoom);
        Vector2Int outward = doorCell - locked.CellIn(locked.Other(outsideRoom));
        RectInt r = layout.rooms[outsideRoom].rect;

        // Pos jaga 2 sel di samping depan pintu, mondar-mandir pendek.
        List<Vector2Int> spots = new List<Vector2Int>();
        Vector2Int side = new Vector2Int(outward.y, outward.x);

        foreach (int s in new[] { 2, -2, 3, -3 })
        {
            Vector2Int c = doorCell + outward + side * s;
            if (r.Contains(c) && !layout.blocked[c.x, c.y]) spots.Add(c);
        }

        if (spots.Count < 1)
            return null;

        Vector2Int a = spots[rng.Next(spots.Count)];
        Vector2Int b = doorCell + outward * 3;

        if (!r.Contains(b) || layout.blocked[b.x, b.y])
            b = doorCell + outward * 2;

        if (!r.Contains(b) || layout.blocked[b.x, b.y] || distFromStart[a.x, a.y] < 6 || distFromStart[b.x, b.y] < 6)
            return null;

        return new LayoutGuard { route = { a, b }, waitTime = 3f, post = true, room = outsideRoom };
    }

    private static LayoutGuard MakePatrolGuard(LevelLayout layout, System.Random rng, DifficultyProfile profile,
        HashSet<int> critical, Dictionary<int, int> guardsPerRoom, int[,] distFromStart)
    {
        // Pilih ruangan awal (berbobot luas, ruangan kritis lebih sering, hindari menumpuk).
        float total = 0f;
        float[] weights = new float[layout.rooms.Count];

        for (int i = 0; i < layout.rooms.Count; i++)
        {
            if (i == layout.startRoom || i == layout.vaultRoom)
                continue;

            guardsPerRoom.TryGetValue(i, out int n);
            weights[i] = layout.rooms[i].Area * (critical.Contains(i) ? 1.6f : 1f) / (1f + n * 2f);
            total += weights[i];
        }

        if (total <= 0f)
            return null;

        float pick = (float)rng.NextDouble() * total;
        int room = -1;

        for (int i = 0; i < weights.Length; i++)
        {
            pick -= weights[i];
            if (weights[i] > 0f) room = i;
            if (pick <= 0f && weights[i] > 0f) break;
        }

        if (room < 0)
            return null;

        // Rantai ruangan untuk rute (lewat pintu yang tidak terkunci, tidak masuk start).
        List<int> chain = new List<int> { room };

        while (chain.Count < profile.patrolRoomsPerGuard)
        {
            int last = chain[chain.Count - 1];
            List<int> next = new List<int>();

            foreach (int doorId in layout.rooms[last].doors)
            {
                LayoutDoor door = layout.doors[doorId];
                int other = door.Other(last);

                if (!door.locked && other != layout.startRoom && !chain.Contains(other))
                    next.Add(other);
            }

            if (next.Count == 0)
                break;

            chain.Add(next[rng.Next(next.Count)]);
        }

        LayoutGuard guard = new LayoutGuard { room = room, waitTime = (float)rng.NextDouble() * 1.5f };

        foreach (int r in chain)
        {
            Vector2Int a, b;

            if (!FarPair(layout, r, rng, out a, out b))
                return null;

            guard.route.Add(a);
            if (chain.Count == 1) guard.route.Add(b);
        }

        if (chain.Count > 1)
        {
            // Kembali lewat titik kedua ruangan pertama agar rute berbentuk loop.
            if (FarPair(layout, room, rng, out _, out Vector2Int back))
                guard.route.Add(back);
        }

        foreach (Vector2Int c in guard.route)
        {
            if (distFromStart[c.x, c.y] < 7)
                return null;
        }

        return guard;
    }

    private static bool FarPair(LevelLayout layout, int room, System.Random rng, out Vector2Int a, out Vector2Int b)
    {
        a = b = default;
        List<Vector2Int> free = new List<Vector2Int>();
        RectInt r = layout.rooms[room].rect;

        for (int x = r.xMin; x < r.xMax; x++)
            for (int y = r.yMin; y < r.yMax; y++)
                if (!layout.blocked[x, y]) free.Add(new Vector2Int(x, y));

        if (free.Count < 2)
            return false;

        int best = -1;

        for (int i = 0; i < 12; i++)
        {
            Vector2Int p = free[rng.Next(free.Count)];
            Vector2Int q = free[rng.Next(free.Count)];
            int d = Manhattan(p, q);

            if (d > best)
            {
                best = d;
                a = p;
                b = q;
            }
        }

        return best >= 2;
    }

    private static HashSet<int> CriticalRooms(LevelLayout layout)
    {
        HashSet<int> set = new HashSet<int> { layout.keyRoom, layout.extractionRoom };
        LayoutDoor locked = layout.LockedDoor;
        set.Add(locked.Other(layout.vaultRoom));
        return set;
    }

    // ==================================================
    // 6. VALIDASI
    // ==================================================

    private static bool Validate(LevelLayout layout, DifficultyProfile profile, out string reason)
    {
        reason = null;

        // --- Konektivitas & lock-and-key ---
        int[,] noKey = CellDistances(layout, layout.playerStart, false);

        if (noKey[layout.keycard.x, layout.keycard.y] < 0)
        {
            reason = "keycard tidak bisa dicapai tanpa melewati pintu terkunci";
            return false;
        }

        Vector2Int terminalFront = layout.terminal.Front;

        if (noKey[terminalFront.x, terminalFront.y] >= 0)
        {
            reason = "terminal bisa dicapai tanpa keycard (lock-and-key bocor)";
            return false;
        }

        int[,] withKey = CellDistances(layout, layout.playerStart, true);

        if (withKey[terminalFront.x, terminalFront.y] < 0 || withKey[layout.extraction.x, layout.extraction.y] < 0)
        {
            reason = "terminal/ekstraksi tidak terhubung";
            return false;
        }

        foreach (LayoutRoom room in layout.rooms)
        {
            bool reachable = false;

            for (int x = room.rect.xMin; x < room.rect.xMax && !reachable; x++)
                for (int y = room.rect.yMin; y < room.rect.yMax && !reachable; y++)
                    if (withKey[x, y] >= 0) reachable = true;

            if (!reachable)
            {
                reason = "ruangan " + room.name + " tidak bisa dimasuki";
                return false;
            }
        }

        // --- Peta intensitas pengawasan (0..1 = fraksi waktu sel terlihat) ---
        int w = layout.width, h = layout.height;
        layout.watch = new float[w, h];
        layout.watched = new bool[w, h];
        float guardRadius = profile.visionRange / LevelLayout.CellSize * 0.85f;
        float guardHalfAngle = profile.visionAngle * 0.5f + 10f;

        foreach (LayoutGuard guard in layout.guards)
        {
            // Sampel (sel, arah hadap) sepanjang loop patroli + tambahan saat berhenti di waypoint.
            List<(Vector2Int cell, float yaw)> samples = new List<(Vector2Int, float)>();

            for (int i = 0; i < guard.route.Count; i++)
            {
                Vector2Int from = guard.route[i];
                Vector2Int to = guard.route[(i + 1) % guard.route.Count];
                List<Vector2Int> segment = CellPath(layout, from, to, null, false);

                if (segment == null)
                {
                    reason = "rute patroli penjaga terputus";
                    return false;
                }

                for (int k = 0; k < segment.Count - 1; k++)
                {
                    Vector2Int d = segment[k + 1] - segment[k];
                    samples.Add((segment[k], Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg));
                }

                // Berhenti di waypoint: menoleh kiri-kanan.
                Vector2 look = guard.post ? layout.rooms[guard.room].Center - (Vector2)from : (Vector2)(to - from);
                float baseYaw = Mathf.Atan2(look.x, look.y) * Mathf.Rad2Deg;
                int waits = Mathf.RoundToInt(guard.waitTime * 1.5f);

                for (int k = 0; k < waits; k++)
                    samples.Add((from, baseYaw + (k % 3 - 1) * 35f));
            }

            if (samples.Count == 0)
                continue;

            float[,] counts = new float[w, h];

            foreach ((Vector2Int cell, float yaw) in samples)
                MarkVision(layout, counts, cell, guardRadius, yaw, guardHalfAngle);

            Accumulate(layout.watch, counts, 1f / samples.Count);
        }

        foreach (LayoutCamera cam in layout.cameras)
        {
            float[,] counts = new float[w, h];
            MarkVision(layout, counts, cam.cell, 5.2f, cam.yaw, 70f);
            Accumulate(layout.watch, counts, 0.6f);
        }

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                layout.watched[x, y] = layout.watch[x, y] >= WatchThreshold;

        // --- Jalur stealth: start -> keycard -> terminal -> ekstraksi ---
        // Rute A: lewat pintu saja. Rute B: ambil obeng dulu, boleh lewat ventilasi aman.
        // Dipakai rute dengan eksposur terkecil (ventilasi bisa "menyelamatkan" layout yang padat penjaga).
        layout.stealthPath.Clear();
        List<Vector2Int> routeA = Route(layout, terminalFront, false);

        if (routeA == null)
        {
            reason = "jalur misi tidak lengkap";
            return false;
        }

        layout.exposureNoVent = Exposure(layout, routeA);
        List<Vector2Int> chosen = routeA;
        layout.routeUsesVent = false;

        if (layout.vents.Exists(v => v.hazard == VentHazardType.None))
        {
            List<Vector2Int> routeB = Route(layout, terminalFront, true);

            if (routeB != null && UsesVent(routeB) && Exposure(layout, routeB) < layout.exposureNoVent)
            {
                chosen = routeB;
                layout.routeUsesVent = true;
            }
        }

        layout.stealthPath.AddRange(chosen);
        layout.exposure = Exposure(layout, chosen);
        layout.pathLength = layout.stealthPath.Count;
        layout.parTime = layout.pathLength * LevelLayout.CellSize / 1.6f + 25f;

        // Titik start harus aman (tidak terlihat sama sekali).
        if (layout.watch[layout.playerStart.x, layout.playerStart.y] > 0.1f)
        {
            reason = "titik start terlihat penjaga/CCTV";
            return false;
        }

        if (layout.exposure > layout.exposureBudget)
        {
            reason = "jalur stealth terlalu terekspos (" + layout.exposure + " > " + layout.exposureBudget + " sel)";
            return false;
        }

        return true;
    }

    private static List<Vector2Int> Route(LevelLayout layout, Vector2Int terminalFront, bool viaVent)
    {
        List<Vector2Int> route = new List<Vector2Int>();
        Vector2Int from = layout.playerStart;

        if (viaVent)
        {
            List<Vector2Int> toDriver = CellPath(layout, from, layout.screwdriver, layout.watch, false);
            if (toDriver == null) return null;
            route.AddRange(toDriver);
            from = layout.screwdriver;
        }

        List<Vector2Int> leg1 = CellPath(layout, from, layout.keycard, layout.watch, false, viaVent);
        List<Vector2Int> leg2 = CellPath(layout, layout.keycard, terminalFront, layout.watch, true, viaVent);
        List<Vector2Int> leg3 = CellPath(layout, terminalFront, layout.extraction, layout.watch, true, viaVent);

        if (leg1 == null || leg2 == null || leg3 == null)
            return null;

        route.AddRange(leg1);
        route.AddRange(leg2);
        route.AddRange(leg3);
        return route;
    }

    private static int Exposure(LevelLayout layout, List<Vector2Int> route)
    {
        HashSet<Vector2Int> exposed = new HashSet<Vector2Int>();

        foreach (Vector2Int c in route)
            if (layout.watched[c.x, c.y]) exposed.Add(c);

        return exposed.Count;
    }

    // Lompatan antar sel yang tidak bertetangga = melewati saluran ventilasi.
    private static bool UsesVent(List<Vector2Int> route)
    {
        for (int i = 1; i < route.Count; i++)
            if (Manhattan(route[i], route[i - 1]) > 1) return true;

        return false;
    }

    // BFS jarak sel dari titik awal. withKey = pintu terkunci bisa dilewati.
    private static int[,] CellDistances(LevelLayout layout, Vector2Int from, bool withKey)
    {
        int[,] dist = new int[layout.width, layout.height];

        for (int x = 0; x < layout.width; x++)
            for (int y = 0; y < layout.height; y++)
                dist[x, y] = -1;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        dist[from.x, from.y] = 0;
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();

            foreach (Vector2Int d in Dirs)
            {
                Vector2Int n = c + d;

                if (!CanStep(layout, c, n, withKey) || dist[n.x, n.y] >= 0)
                    continue;

                dist[n.x, n.y] = dist[c.x, c.y] + 1;
                queue.Enqueue(n);
            }
        }

        return dist;
    }

    public static bool CanStep(LevelLayout layout, Vector2Int a, Vector2Int b, bool withKey)
    {
        if (!layout.InBounds(b) || layout.blocked[b.x, b.y])
            return false;

        int ra = layout.roomOf[a.x, a.y];
        int rb = layout.roomOf[b.x, b.y];

        if (ra == rb)
            return true;

        foreach (int doorId in layout.rooms[ra].doors)
        {
            LayoutDoor door = layout.doors[doorId];

            if ((door.cellA == a && door.cellB == b) || (door.cellA == b && door.cellB == a))
                return !door.locked || withKey;
        }

        return false;
    }

    // Dijkstra di grid. watched != null -> sel terawasi sangat mahal (mencari jalur paling tersembunyi).
    private static void Relax(SortedSet<(int, int, int)> open, int[,] cost, Vector2Int[,] prev, Vector2Int cur, int c, Vector2Int n, int step)
    {
        int nc = c + step;

        if (nc < cost[n.x, n.y])
        {
            if (cost[n.x, n.y] != int.MaxValue)
                open.Remove((cost[n.x, n.y], n.x, n.y));

            cost[n.x, n.y] = nc;
            prev[n.x, n.y] = cur;
            open.Add((nc, n.x, n.y));
        }
    }

    // allowVents = boleh lewat ventilasi TANPA bahaya (biaya = 2x panjang saluran, tidak terawasi).
    private static List<Vector2Int> CellPath(LevelLayout layout, Vector2Int from, Vector2Int to, float[,] watch, bool withKey, bool allowVents = false)
    {
        int w = layout.width, h = layout.height;
        int[,] cost = new int[w, h];
        Vector2Int[,] prev = new Vector2Int[w, h];

        for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
                cost[x, y] = int.MaxValue;

        // Antrian prioritas sederhana (grid kecil, cukup cepat).
        SortedSet<(int, int, int)> open = new SortedSet<(int, int, int)>();
        cost[from.x, from.y] = 0;
        open.Add((0, from.x, from.y));

        while (open.Count > 0)
        {
            (int c, int cx, int cy) = open.Min;
            open.Remove(open.Min);
            Vector2Int cur = new Vector2Int(cx, cy);

            if (cur == to)
                break;

            if (c > cost[cx, cy])
                continue;

            foreach (Vector2Int d in Dirs)
            {
                Vector2Int n = cur + d;

                if (CanStep(layout, cur, n, withKey))
                    Relax(open, cost, prev, cur, c, n, 1 + (watch != null ? Mathf.RoundToInt(watch[n.x, n.y] * 30f) : 0));
            }

            if (allowVents)
            {
                foreach (LayoutVent vent in layout.vents)
                {
                    if (vent.hazard != VentHazardType.None)
                        continue;

                    // Merangkak (jongkok) lebih lambat: biaya 2x panjang saluran.
                    if (cur == vent.cellA) Relax(open, cost, prev, cur, c, vent.cellB, vent.Length * 2 + 2);
                    else if (cur == vent.cellB) Relax(open, cost, prev, cur, c, vent.cellA, vent.Length * 2 + 2);
                }
            }
        }

        if (cost[to.x, to.y] == int.MaxValue)
            return null;

        List<Vector2Int> path = new List<Vector2Int>();

        for (Vector2Int c = to; c != from; c = prev[c.x, c.y])
            path.Add(c);

        path.Add(from);
        path.Reverse();
        return path;
    }

    // Tandai sel yang terlihat dari sumber (radius dalam sel). yaw/halfAngle untuk kerucut CCTV.
    private static void MarkVision(LevelLayout layout, float[,] counts, Vector2Int source, float radius, float yaw, float halfAngle)
    {
        int r = Mathf.CeilToInt(radius);

        for (int dx = -r; dx <= r; dx++)
        {
            for (int dy = -r; dy <= r; dy++)
            {
                Vector2Int t = new Vector2Int(source.x + dx, source.y + dy);

                if (!layout.InBounds(t) || dx * dx + dy * dy > radius * radius)
                    continue;

                // Sel sangat dekat selalu "terasa" (proximity), sisanya harus di dalam kerucut.
                if (dx * dx + dy * dy > 1)
                {
                    float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;

                    if (Mathf.Abs(Mathf.DeltaAngle(angle, yaw)) > halfAngle)
                        continue;
                }

                if (HasLineOfSight(layout, source, t))
                    counts[t.x, t.y] += 1f;
            }
        }
    }

    // Gabungkan beberapa sumber pengawasan: P(terlihat) = 1 - prod(1 - p_i).
    private static void Accumulate(float[,] watch, float[,] counts, float weight)
    {
        for (int x = 0; x < watch.GetLength(0); x++)
        {
            for (int y = 0; y < watch.GetLength(1); y++)
            {
                float p = Mathf.Clamp01(counts[x, y] * weight);
                watch[x, y] = 1f - (1f - watch[x, y]) * (1f - p);
            }
        }
    }

    public static bool HasLineOfSight(LevelLayout layout, Vector2Int from, Vector2Int to)
    {
        Vector2 a = new Vector2(from.x + 0.5f, from.y + 0.5f);
        Vector2 b = new Vector2(to.x + 0.5f, to.y + 0.5f);
        int steps = Mathf.CeilToInt(Vector2.Distance(a, b) * 4f);
        Vector2Int current = from;

        for (int i = 1; i <= steps; i++)
        {
            Vector2 p = Vector2.Lerp(a, b, i / (float)steps);
            Vector2Int cell = new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));

            if (cell == current)
                continue;

            if (cell.x != current.x && cell.y != current.y)
            {
                // Diagonal: cukup salah satu jalur ortogonal yang terbuka.
                Vector2Int viaX = new Vector2Int(cell.x, current.y);
                Vector2Int viaY = new Vector2Int(current.x, cell.y);
                bool openX = SeeThrough(layout, current, viaX, to) && SeeThrough(layout, viaX, cell, to);
                bool openY = SeeThrough(layout, current, viaY, to) && SeeThrough(layout, viaY, cell, to);

                if (!openX && !openY)
                    return false;
            }
            else if (!SeeThrough(layout, current, cell, to))
            {
                return false;
            }

            current = cell;
        }

        return true;
    }

    private static bool SeeThrough(LevelLayout layout, Vector2Int a, Vector2Int b, Vector2Int target)
    {
        if (!layout.InBounds(b))
            return false;

        if (b != target && layout.blocked[b.x, b.y])
            return false;

        if (layout.roomOf[a.x, a.y] == layout.roomOf[b.x, b.y])
            return true;

        // Pandangan menembus pintu yang terbuka (pintu terkunci tertutup = menghalangi).
        foreach (int doorId in layout.rooms[layout.roomOf[a.x, a.y]].doors)
        {
            LayoutDoor door = layout.doors[doorId];

            if ((door.cellA == a && door.cellB == b) || (door.cellA == b && door.cellB == a))
                return !door.locked;
        }

        return false;
    }
}
