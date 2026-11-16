using UnityEngine;
using UnityEngine.InputSystem;

// F11 = fullscreen di semua scene.
//   Build  : beralih Fullscreen Window <-> Windowed.
//   Editor : memaksimalkan Game view (Play Maximized) karena Screen.fullScreen tidak berlaku di editor.
public class FullscreenToggle : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindFirstObjectByType<FullscreenToggle>() != null)
            return;

        GameObject go = new GameObject("FullscreenToggle");
        DontDestroyOnLoad(go);
        go.AddComponent<FullscreenToggle>();
    }

    private void Update()
    {
        Keyboard k = Keyboard.current;

        if (k == null || !k.f11Key.wasPressedThisFrame)
            return;

#if UNITY_EDITOR
        System.Type gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");

        if (gameViewType != null)
        {
            UnityEditor.EditorWindow gameView = UnityEditor.EditorWindow.GetWindow(gameViewType, false, null, false);
            gameView.maximized = !gameView.maximized;
            StealthHUD.Toast(gameView.maximized ? "Layar penuh (F11 untuk kembali)" : "Mode jendela");
        }
#else
        if (Screen.fullScreen)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            Screen.fullScreen = false;
        }
        else
        {
            Resolution r = Screen.currentResolution;
            Screen.SetResolution(r.width, r.height, FullScreenMode.FullScreenWindow);
        }

        StealthHUD.Toast(Screen.fullScreen ? "Mode jendela" : "Layar penuh (F11 untuk kembali)");
#endif
    }
}
