using System.Collections.Generic;
using UnityEngine;

// Panel alarm dinding. Penjaga (perilaku Raise Alarm) berlari ke sini untuk memanggil semua penjaga.
// Pemain bisa menyabotase panel (tahan E) supaya alarm tidak bisa dibunyikan.
public class AlarmPanel : Interactable
{
    [SerializeField] private float sabotageDuration = 2.5f;
    [SerializeField] private Renderer indicator;

    private static readonly List<AlarmPanel> panels = new List<AlarmPanel>();

    public static IReadOnlyList<AlarmPanel> Panels => panels;

    public bool Sabotaged { get; private set; }
    public bool Usable => isActiveAndEnabled && !Sabotaged;
    // Titik di lantai depan panel (forward panel menghadap ke dalam ruangan).
    public Vector3 StandPoint
    {
        get
        {
            Vector3 p = transform.position + transform.forward * 0.9f;
            p.y = 0f;
            return p;
        }
    }

    public override float HoldDuration => sabotageDuration;
    public override bool IsAvailable => base.IsAvailable && !Sabotaged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        panels.Clear();
    }

    protected override void OnEnable()
    {
        base.OnEnable();
        panels.Add(this);
        SetIndicator(new Color(1f, 0.15f, 0.1f));
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        panels.Remove(this);
    }

    public static AlarmPanel Nearest(Vector3 position)
    {
        AlarmPanel best = null;
        float bestDistance = float.MaxValue;

        foreach (AlarmPanel panel in panels)
        {
            if (!panel.Usable)
                continue;

            float d = (panel.StandPoint - position).sqrMagnitude;

            if (d < bestDistance)
            {
                bestDistance = d;
                best = panel;
            }
        }

        return best;
    }

    public override string GetPrompt(PlayerInventory inventory)
    {
        return "[Tahan E] Sabotase panel alarm";
    }

    public override void Interact(PlayerInteractor interactor)
    {
        Sabotaged = true;
        SetIndicator(new Color(0.25f, 0.25f, 0.25f));
        SoundManager.Play("hack_done", 0.8f, 0f);
        StealthHUD.Toast("Panel alarm disabotase - penjaga tidak bisa memanggil bantuan dari sini");
        NotifyMission();
    }

    public void Trigger()
    {
        SetIndicator(new Color(1f, 0.6f, 0.1f));
        SoundManager.PlayAt("camera_alarm", transform.position, 1f, 40f, 0f);
    }

    private void SetIndicator(Color color)
    {
        if (indicator == null)
            return;

        indicator.material.color = color;
        indicator.material.SetColor("_EmissionColor", color * 2f);
    }
}
