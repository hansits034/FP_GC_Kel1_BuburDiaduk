using System.Collections.Generic;
using UnityEngine;

// Dasar semua objek yang bisa dipakai dengan tombol E.
public abstract class Interactable : MonoBehaviour
{
    [Tooltip("Kunci misi yang dikirim ke MissionManager saat berhasil dipakai, misal 'pickup:keycard_red'.")]
    [SerializeField] protected string missionKey;

    private static readonly List<Interactable> all = new List<Interactable>();

    public static IReadOnlyList<Interactable> All => all;

    // > 0 berarti harus menahan E selama durasi ini.
    public virtual float HoldDuration => 0f;

    public virtual bool IsAvailable => isActiveAndEnabled;

    public virtual Vector3 InteractionPoint => transform.position;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        all.Clear();
    }

    protected virtual void OnEnable()
    {
        all.Add(this);
    }

    protected virtual void OnDisable()
    {
        all.Remove(this);
    }

    public abstract string GetPrompt(PlayerInventory inventory);

    public virtual bool CanInteract(PlayerInventory inventory, out string reason)
    {
        reason = null;
        return true;
    }

    public abstract void Interact(PlayerInteractor interactor);

    protected void NotifyMission()
    {
        if (!string.IsNullOrEmpty(missionKey))
            MissionManager.Notify(missionKey);
    }
}
