using System.Collections.Generic;
using UnityEngine;

// Data hasil PCG (murni data, tanpa GameObject). Dibangun menjadi scene oleh ProceduralLevelBuilder.
// Grid: 1 sel = CellSize meter. Sel (x, y) -> dunia (x, z).

public enum RoomTheme { Dock, Hall, Office, Storage, Server }

public enum PropKind { Crate, Boxes, Barrel, Pillar, Locker, Sandbag, Desk, Shelf, ServerRack }

public class LayoutRoom
{
    public int id;
    public RectInt rect;
    public RoomTheme theme;
    public string name;
    public readonly List<int> doors = new List<int>();
    public int Area => rect.width * rect.height;
    public Vector2 Center => rect.center;
}

public class LayoutDoor
{
    public int id;
    public int roomA, roomB;
    public Vector2Int cellA, cellB; // sel di kedua sisi pintu (bertetangga)
    public bool locked;

    public bool AlongX => cellA.y != cellB.y; // dinding sejajar sumbu X

    public int Other(int room) => room == roomA ? roomB : roomA;
    public Vector2Int CellIn(int room) => room == roomA ? cellA : cellB;
}

public class LayoutProp
{
    public PropKind kind;
    public RectInt cells;
    public bool rotated; // true = memanjang di sumbu Y grid (Z dunia)
}

public class WallMount
{
    public Vector2Int cell;
    public Vector2Int toWall; // arah dari sel menuju dinding
    public Vector2Int Front => cell - toWall;
}

// Saluran ventilasi di luar dinding batas peta: menghubungkan dua sel tepi (hanya pemain, jongkok).
public enum VentHazardType { None, Toxic, Laser }

public class LayoutVent
{
    public VentHazardType hazard;  // bahaya di saluran (mati saat listrik padam)
    public int roomA, roomB;      // A = pintu masuk (penutup dibaut), B = keluar
    public Vector2Int cellA, cellB;
    public Vector2Int outward;    // arah dari sel ke luar peta (sama untuk A & B)
    public int Length => Mathf.Abs(cellA.x - cellB.x) + Mathf.Abs(cellA.y - cellB.y);
}

public class LayoutGuard
{
    public List<Vector2Int> route = new List<Vector2Int>();
    public float waitTime;
    public bool post;
    public int room;
}

public class LayoutCamera
{
    public Vector2Int cell;
    public Vector2 cornerOffset; // dorong ke pojok ruangan (dalam sel)
    public float yaw;
    public int room;
}

public class LevelLayout
{
    public const float CellSize = 2f;

    public int seed;
    public int width, height;
    public int[,] roomOf;
    public bool[,] blocked;
    public bool[,] reserved;

    public readonly List<LayoutRoom> rooms = new List<LayoutRoom>();
    public readonly List<LayoutDoor> doors = new List<LayoutDoor>();
    public readonly List<LayoutProp> props = new List<LayoutProp>();
    public readonly List<LayoutGuard> guards = new List<LayoutGuard>();
    public readonly List<LayoutCamera> cameras = new List<LayoutCamera>();
    public readonly List<WallMount> alarms = new List<WallMount>();
    public readonly List<Vector2Int> cans = new List<Vector2Int>();
    public readonly List<LayoutVent> vents = new List<LayoutVent>();
    public Vector2Int screwdriver;

    public int startRoom, vaultRoom, keyRoom, extractionRoom, powerRoom;
    public readonly List<int> poweredRooms = new List<int>();

    public Vector2Int playerStart;
    public Vector2Int keycard;
    public Vector2Int extraction;
    public WallMount terminal;
    public WallMount powerBox;

    // ---------- Hasil validasi ----------
    public float[,] watch;     // intensitas pengawasan 0..1 per sel
    public bool[,] watched;    // watch >= LayoutGenerator.WatchThreshold
    public readonly List<Vector2Int> stealthPath = new List<Vector2Int>();
    public int exposure;
    public int exposureNoVent;   // eksposur rute terbaik TANPA ventilasi (pembanding)
    public bool routeUsesVent;   // rute stealth terbaik melewati ventilasi
    public int exposureBudget;
    public int pathLength;
    public float parTime;
    public int layoutAttempts;
    public int populationAttempts;
    public int rejectedProps;
    public bool valid;
    public readonly List<string> log = new List<string>();

    public LayoutDoor LockedDoor => doors.Find(d => d.locked);

    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;

    public static Vector3 CellToWorld(Vector2Int c, float y = 0f)
    {
        return new Vector3((c.x + 0.5f) * CellSize, y, (c.y + 0.5f) * CellSize);
    }

    public static Vector3 CellToWorld(Vector2 c, float y = 0f)
    {
        return new Vector3((c.x + 0.5f) * CellSize, y, (c.y + 0.5f) * CellSize);
    }

    public static Vector2Int WorldToCell(Vector3 p)
    {
        return new Vector2Int(Mathf.FloorToInt(p.x / CellSize), Mathf.FloorToInt(p.z / CellSize));
    }

    public Vector3 WorldMin => Vector3.zero;
    public Vector3 WorldSize => new Vector3(width * CellSize, 0f, height * CellSize);
}

// Ukuran tiap jenis prop: footprint di grid & ukuran collider di dunia.
public static class PropCatalog
{
    public static Vector2Int Footprint(PropKind kind)
    {
        switch (kind)
        {
            case PropKind.Sandbag:
            case PropKind.Desk:
                return new Vector2Int(2, 1);
            case PropKind.Shelf:
            case PropKind.ServerRack:
                return new Vector2Int(3, 1);
            default:
                return new Vector2Int(1, 1);
        }
    }

    // Ukuran collider (sebelum diputar): x = memanjang.
    public static Vector3 WorldSize(PropKind kind)
    {
        switch (kind)
        {
            case PropKind.Crate: return new Vector3(1.5f, 1.2f, 1.5f);
            case PropKind.Boxes: return new Vector3(1.8f, 1.2f, 1.4f);
            case PropKind.Barrel: return new Vector3(1f, 1.2f, 1f);
            case PropKind.Pillar: return new Vector3(1f, 3f, 1f);
            case PropKind.Locker: return new Vector3(1.6f, 2.2f, 0.8f);
            case PropKind.Sandbag: return new Vector3(3.6f, 1f, 0.6f);
            case PropKind.Desk: return new Vector3(3f, 0.9f, 1.2f);
            case PropKind.Shelf: return new Vector3(5.4f, 2.2f, 0.8f);
            case PropKind.ServerRack: return new Vector3(5.4f, 2.2f, 0.8f);
            default: return Vector3.one;
        }
    }

    public static PropKind[] ForTheme(RoomTheme theme)
    {
        switch (theme)
        {
            case RoomTheme.Dock: return new[] { PropKind.Crate, PropKind.Crate, PropKind.Boxes, PropKind.Barrel };
            case RoomTheme.Hall: return new[] { PropKind.Sandbag, PropKind.Sandbag, PropKind.Pillar, PropKind.Crate };
            case RoomTheme.Office: return new[] { PropKind.Desk, PropKind.Desk, PropKind.Locker, PropKind.Boxes };
            case RoomTheme.Storage: return new[] { PropKind.Shelf, PropKind.Shelf, PropKind.Boxes, PropKind.Crate };
            case RoomTheme.Server: return new[] { PropKind.ServerRack, PropKind.ServerRack, PropKind.Boxes };
            default: return new[] { PropKind.Crate };
        }
    }

    public static string ThemeName(RoomTheme theme)
    {
        switch (theme)
        {
            case RoomTheme.Dock: return "Dok Muat";
            case RoomTheme.Hall: return "Aula";
            case RoomTheme.Office: return "Ruang Jaga";
            case RoomTheme.Storage: return "Gudang";
            case RoomTheme.Server: return "Ruang Server";
            default: return "Ruangan";
        }
    }
}
