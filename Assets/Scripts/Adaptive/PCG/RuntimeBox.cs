using System.Collections.Generic;
using UnityEngine;

// Kotak dengan UV dalam meter (tekstur tidak melar), versi runtime dari helper StealthLevelBuilder.
public static class RuntimeBox
{
    private static readonly Dictionary<Vector3Int, Mesh> cache = new Dictionary<Vector3Int, Mesh>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
    }

    public static GameObject Create(string name, Vector3 center, Vector3 size, Material material, Transform parent, int layer)
    {
        GameObject go = new GameObject(name);
        go.layer = layer;
        go.transform.SetParent(parent, false);
        go.transform.position = center;

        go.AddComponent<MeshFilter>().sharedMesh = GetMesh(size);
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        go.AddComponent<BoxCollider>().size = size;
        return go;
    }

    public static Mesh GetMesh(Vector3 size)
    {
        Vector3Int key = Vector3Int.RoundToInt(size * 100f);

        if (cache.TryGetValue(key, out Mesh mesh) && mesh != null)
            return mesh;

        mesh = Build(size);
        mesh.name = "RuntimeBox_" + key;
        cache[key] = mesh;
        return mesh;
    }

    private static Mesh Build(Vector3 s)
    {
        Vector3 h = s * 0.5f;
        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<int> triangles = new List<int>();

        void Face(Vector3 origin, Vector3 uDir, Vector3 vDir, float uLen, float vLen, Vector3 normal)
        {
            int i = vertices.Count;
            vertices.Add(origin);
            vertices.Add(origin + uDir * uLen);
            vertices.Add(origin + uDir * uLen + vDir * vLen);
            vertices.Add(origin + vDir * vLen);
            uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(uLen, 0f));
            uvs.Add(new Vector2(uLen, vLen));
            uvs.Add(new Vector2(0f, vLen));

            if (Vector3.Dot(Vector3.Cross(uDir, vDir), normal) > 0f)
                triangles.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            else
                triangles.AddRange(new[] { i, i + 2, i + 1, i, i + 3, i + 2 });
        }

        Face(new Vector3(h.x, -h.y, h.z), Vector3.back, Vector3.up, s.z, s.y, Vector3.right);
        Face(new Vector3(-h.x, -h.y, -h.z), Vector3.forward, Vector3.up, s.z, s.y, Vector3.left);
        Face(new Vector3(h.x, -h.y, h.z), Vector3.left, Vector3.up, s.x, s.y, Vector3.forward);
        Face(new Vector3(-h.x, -h.y, -h.z), Vector3.right, Vector3.up, s.x, s.y, Vector3.back);
        Face(new Vector3(-h.x, h.y, -h.z), Vector3.right, Vector3.forward, s.x, s.z, Vector3.up);
        Face(new Vector3(-h.x, -h.y, h.z), Vector3.right, Vector3.back, s.x, s.z, Vector3.down);

        Mesh mesh = new Mesh();
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }
}
