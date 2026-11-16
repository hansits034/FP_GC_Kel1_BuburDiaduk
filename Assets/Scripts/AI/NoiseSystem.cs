using System.Collections.Generic;
using UnityEngine;

// Perception (Hearing): siapa pun yang ingin "mendengar" suara mendaftar ke sini.
public interface INoiseListener
{
    // strength: pengali kecurigaan (1 = langkah kaki, 2 = benda jatuh / alarm kecil).
    void HearNoise(Vector3 position, float radius, float strength);
}

public static class NoiseSystem
{
    private static readonly List<INoiseListener> listeners = new List<INoiseListener>();

    // Untuk debug: suara terakhir yang dipancarkan.
    public static Vector3 LastNoisePosition { get; private set; }
    public static float LastNoiseRadius { get; private set; }

    // Dipakai PlayerPerformanceTracker (DDA) untuk menghitung suara yang dibuat.
    public static event System.Action<Vector3, float> Emitted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        listeners.Clear();
        LastNoiseRadius = 0f;
        Emitted = null;
    }

    public static void Register(INoiseListener listener)
    {
        if (!listeners.Contains(listener))
            listeners.Add(listener);
    }

    public static void Unregister(INoiseListener listener)
    {
        listeners.Remove(listener);
    }

    // Pancarkan suara pada posisi tertentu dengan radius jangkauan (meter).
    // Tiap listener menghitung sendiri jarak & halangan dinding.
    public static void Emit(Vector3 position, float radius, float strength = 1f)
    {
        if (radius <= 0f)
            return;

        LastNoisePosition = position;
        LastNoiseRadius = radius;
        Emitted?.Invoke(position, radius);

        for (int i = listeners.Count - 1; i >= 0; i--)
            listeners[i].HearNoise(position, radius, strength);
    }
}
