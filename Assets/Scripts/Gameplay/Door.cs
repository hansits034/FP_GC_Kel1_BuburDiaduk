using UnityEngine;

// Pintu geser. Bisa membutuhkan item (keycard) untuk dibuka.
// Panel memakai NavMeshObstacle (carve) sehingga penjaga tidak bisa lewat saat tertutup.
public class Door : Interactable
{
    [SerializeField] private string doorName = "Pintu";
    [SerializeField] private string requiredItemId;
    [SerializeField] private string requiredItemName;

    [Header("Motion")]
    [SerializeField] private Transform panel;
    [SerializeField] private Vector3 openOffset = new Vector3(2f, 0f, 0f);
    [SerializeField] private float openSpeed = 3f;

    [Header("Indicator")]
    [SerializeField] private Renderer indicator;

    private Vector3 closedLocalPosition;

    public bool IsOpen { get; private set; }

    private bool RequiresKey => !string.IsNullOrEmpty(requiredItemId);

    public override bool IsAvailable => base.IsAvailable && !IsOpen;

    public override Vector3 InteractionPoint =>
        panel != null ? panel.parent.TransformPoint(closedLocalPosition) : transform.position;

    private void Awake()
    {
        if (panel != null)
            closedLocalPosition = panel.localPosition;

        SetIndicator(RequiresKey ? Color.red : Color.green);
    }

    private void Update()
    {
        if (panel == null)
            return;

        Vector3 target = closedLocalPosition + (IsOpen ? openOffset : Vector3.zero);
        panel.localPosition = Vector3.MoveTowards(panel.localPosition, target, openSpeed * Time.deltaTime);
    }

    public override string GetPrompt(PlayerInventory inventory)
    {
        if (!RequiresKey)
            return "[E] Buka " + doorName;

        return inventory.Has(requiredItemId)
            ? "[E] Buka " + doorName + " (" + requiredItemName + ")"
            : doorName + " terkunci - butuh " + requiredItemName;
    }

    public override bool CanInteract(PlayerInventory inventory, out string reason)
    {
        if (RequiresKey && !inventory.Has(requiredItemId))
        {
            reason = doorName + " terkunci. Cari " + requiredItemName + "!";
            return false;
        }

        reason = null;
        return true;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        IsOpen = true;
        SetIndicator(Color.green);

        // Bunyi pintu geser (pelan).
        SoundManager.PlayAt("door_open", transform.position, 0.9f);
        NoiseSystem.Emit(transform.position, 3f, 0.6f);
        StealthHUD.Toast(doorName + " terbuka");

        NotifyMission();
    }

    private void SetIndicator(Color color)
    {
        if (indicator == null)
            return;

        indicator.material.color = color;
        indicator.material.SetColor("_EmissionColor", color * 2f);
    }
}
