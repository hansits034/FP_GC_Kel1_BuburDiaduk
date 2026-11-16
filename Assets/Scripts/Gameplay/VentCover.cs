using UnityEngine;

// Penutup ventilasi yang dibaut. Butuh obeng & menahan E untuk membukanya.
// Di dalam ventilasi Player harus jongkok (atap rendah), penjaga tidak bisa masuk.
public class VentCover : Interactable
{
    [SerializeField] private string requiredItemId = "screwdriver";
    [SerializeField] private string requiredItemName = "Obeng";
    [SerializeField] private float unscrewDuration = 1.5f;
    [SerializeField] private Transform cover;

    public bool IsOpen { get; private set; }

    public override float HoldDuration => unscrewDuration;

    public override bool IsAvailable => base.IsAvailable && !IsOpen;

    public override string GetPrompt(PlayerInventory inventory)
    {
        return inventory.Has(requiredItemId)
            ? "[Tahan E] Buka baut ventilasi (" + requiredItemName + ")"
            : "Ventilasi dibaut - butuh " + requiredItemName;
    }

    public override bool CanInteract(PlayerInventory inventory, out string reason)
    {
        if (!inventory.Has(requiredItemId))
        {
            reason = "Penutup ventilasi dibaut. Cari " + requiredItemName + "!";
            return false;
        }

        reason = null;
        return true;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        IsOpen = true;

        if (cover != null)
        {
            foreach (Collider c in cover.GetComponentsInChildren<Collider>())
                c.enabled = false;

            // Letakkan penutup di lantai, di depan lubang (forward cover menghadap keluar ventilasi).
            Vector3 outward = cover.forward;
            cover.position = new Vector3(cover.position.x, 0.05f, cover.position.z) + outward * 1f;
            cover.rotation = Quaternion.LookRotation(Vector3.up, outward);
        }

        SoundManager.PlayAt("vent_open", transform.position, 0.9f);
        NoiseSystem.Emit(transform.position, 2.5f, 0.6f);
        StealthHUD.Toast("Ventilasi terbuka - jongkok (Ctrl) untuk masuk");

        NotifyMission();
    }
}
