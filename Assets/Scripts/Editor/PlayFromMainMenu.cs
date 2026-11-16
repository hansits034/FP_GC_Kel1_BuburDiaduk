using UnityEditor;
using UnityEditor.SceneManagement;

// Tombol Play di Editor selalu memulai dari Menu Utama, apa pun scene yang sedang dibuka.
// Bisa dimatikan lewat menu: Stealth > Play Mulai dari Menu Utama (untuk tes level langsung).
[InitializeOnLoad]
public static class PlayFromMainMenu
{
    private const string MenuPath = "Stealth/Play Mulai dari Menu Utama";
    private const string PrefKey = "Stealth.PlayFromMainMenu";
    private const string MainMenuPath = "Assets/Scenes/MainMenu.unity";

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(PrefKey, true);
        set => EditorPrefs.SetBool(PrefKey, value);
    }

    static PlayFromMainMenu()
    {
        EditorApplication.delayCall += Apply;
    }

    [MenuItem(MenuPath)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
    }

    [MenuItem(MenuPath, true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }

    private static void Apply()
    {
        SceneAsset menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MainMenuPath);
        EditorSceneManager.playModeStartScene = Enabled ? menu : null;
    }
}
