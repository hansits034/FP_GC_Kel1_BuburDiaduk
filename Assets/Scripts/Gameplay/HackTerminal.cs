using UnityEngine;

// Terminal komputer: tahan E beberapa detik untuk mengunduh data.
public class HackTerminal : Interactable
{
    [SerializeField] private string actionName = "Unduh data";
    [SerializeField] private float hackDuration = 3f;
    [SerializeField] private Renderer screen;

    public bool IsDone { get; private set; }

    public override float HoldDuration => hackDuration;

    public override bool IsAvailable => base.IsAvailable && !IsDone;

    private void Start()
    {
        SetScreen(new Color(0.2f, 0.5f, 1f));
    }

    public override string GetPrompt(PlayerInventory inventory)
    {
        return "[Tahan E] " + actionName;
    }

    public override void Interact(PlayerInteractor interactor)
    {
        IsDone = true;
        SetScreen(new Color(0.2f, 1f, 0.3f));
        StealthHUD.Toast(actionName + " selesai!");
        SoundManager.Play("hack_done", 0.8f, 0f);

        NotifyMission();
    }

    private void SetScreen(Color color)
    {
        if (screen == null)
            return;

        screen.material.color = color;
        screen.material.SetColor("_EmissionColor", color * 2.5f);
    }
}
