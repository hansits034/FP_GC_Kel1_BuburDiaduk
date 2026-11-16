using UnityEngine;

// Item yang bisa diambil: kunci, obeng, kaleng, item misi.
public class PickupItem : Interactable
{
    [SerializeField] private string itemId = "item";
    [SerializeField] private string displayName = "Item";
    [SerializeField] private int amount = 1;
    [SerializeField] private Color color = Color.white;

    [Header("Visual")]
    [SerializeField] private Transform visual;
    [SerializeField] private float spinSpeed = 90f;
    [SerializeField] private float bobHeight = 0.08f;

    private Vector3 visualBasePosition;

    private void Start()
    {
        if (visual != null)
            visualBasePosition = visual.localPosition;
    }

    private void Update()
    {
        if (visual == null)
            return;

        visual.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.World);
        visual.localPosition = visualBasePosition + Vector3.up * (Mathf.Sin(Time.time * 2.5f) * bobHeight);
    }

    public override string GetPrompt(PlayerInventory inventory)
    {
        return amount > 1
            ? "[E] Ambil " + displayName + " x" + amount
            : "[E] Ambil " + displayName;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        interactor.Inventory.Add(itemId, displayName, amount, color);
        StealthHUD.Toast("+ " + displayName + (amount > 1 ? " x" + amount : ""));
        SoundManager.Play("pickup", 0.8f, 0f);

        NotifyMission();
        Destroy(gameObject);
    }
}
