using UnityEngine;
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace FPSLAN
{
    /// <summary>
    /// Leitura de input centralizada. Funciona com o Input System novo ou com o Input Manager antigo,
    /// conforme o que estiver ativo no projeto.
    /// </summary>
    public static class InputReader
    {
#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
        static Keyboard K => Keyboard.current;
        static Mouse M => Mouse.current;

        public static Vector2 Move
        {
            get
            {
                var k = K;
                if (k == null) return Vector2.zero;
                float x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                float y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        // Escala igual ao "Mouse X/Y" do Input Manager antigo (0.1 por pixel).
        public static Vector2 Look => M == null ? Vector2.zero : M.delta.ReadValue() * 0.1f;
        public static bool Sprint => K != null && K.leftShiftKey.isPressed;
        public static bool JumpPressed => K != null && K.spaceKey.wasPressedThisFrame;
        public static bool Fire => M != null && M.leftButton.isPressed;
        public static bool SuperPressed => (M != null && M.rightButton.wasPressedThisFrame) || (K != null && K.eKey.wasPressedThisFrame);
        public static bool ScoreboardHeld => K != null && K.tabKey.isPressed;
        public static bool PausePressed => K != null && K.escapeKey.wasPressedThisFrame;
#else
        public static Vector2 Move
        {
            get
            {
                float x = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
                float y = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
                return new Vector2(x, y);
            }
        }

        public static Vector2 Look => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        public static bool Sprint => Input.GetKey(KeyCode.LeftShift);
        public static bool JumpPressed => Input.GetKeyDown(KeyCode.Space);
        public static bool Fire => Input.GetMouseButton(0);
        public static bool SuperPressed => Input.GetMouseButtonDown(1) || Input.GetKeyDown(KeyCode.E);
        public static bool ScoreboardHeld => Input.GetKey(KeyCode.Tab);
        public static bool PausePressed => Input.GetKeyDown(KeyCode.Escape);
#endif
    }
}
