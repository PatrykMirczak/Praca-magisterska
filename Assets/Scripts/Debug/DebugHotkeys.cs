//using UnityEngine;
//using UnityEngine.InputSystem;

//public class DebugHotkeys : MonoBehaviour
//{
//    private void Update()
//    {
//        var kbd = Keyboard.current;
//        if (kbd == null) return;

//        // Spacja – pauza/wznowienie
//        if (kbd.spaceKey.wasPressedThisFrame)
//            Time.timeScale = Time.timeScale < 0.5f ? 1f : 0f;

//        // 1 – slow motion, 0 – normal
//        if (kbd.digit1Key.wasPressedThisFrame)
//            Time.timeScale = 0.1f;
//        if (kbd.digit0Key.wasPressedThisFrame)
//            Time.timeScale = 1f;

//        // F1 – wlacz/wylacz zamrozenie ewolucji
//        if (kbd.f1Key.wasPressedThisFrame && PopulationManager.Instance != null)
//            PopulationManager.Instance.debugFreezeEvolution = !PopulationManager.Instance.debugFreezeEvolution;
//    }
//}
