using System;
using System.Collections.Generic;
using UnityEngine;

// Inventory kecil: kunci, alat (obeng), barang pengalih (kaleng), item misi.
public class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public class Entry
    {
        public string id;
        public string displayName;
        public int count;
        public Color color = Color.white;
    }

    [SerializeField] private List<Entry> items = new List<Entry>();

    public event Action Changed;

    public IReadOnlyList<Entry> Items => items;

    public bool Has(string id)
    {
        return Count(id) > 0;
    }

    public int Count(string id)
    {
        Entry entry = Find(id);
        return entry != null ? entry.count : 0;
    }

    public string GetName(string id)
    {
        Entry entry = Find(id);
        return entry != null ? entry.displayName : id;
    }

    public void Add(string id, string displayName, int amount, Color color)
    {
        Entry entry = Find(id);

        if (entry == null)
        {
            entry = new Entry { id = id, displayName = displayName, count = 0, color = color };
            items.Add(entry);
        }

        entry.count += amount;
        Changed?.Invoke();
    }

    public bool Remove(string id, int amount = 1)
    {
        Entry entry = Find(id);

        if (entry == null || entry.count < amount)
            return false;

        entry.count -= amount;

        if (entry.count <= 0)
            items.Remove(entry);

        Changed?.Invoke();
        return true;
    }

    private Entry Find(string id)
    {
        foreach (Entry entry in items)
        {
            if (entry.id == id)
                return entry;
        }

        return null;
    }
}
