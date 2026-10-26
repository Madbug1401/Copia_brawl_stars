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
        public static Vector2 Move => ReadMove();
        public static Vector2 Look => ReadLook();
        public static bool Sprint => ReadSprint();
        public static bool JumpPressed => ReadJumpPressed();
        public static bool Fire => ReadFire();
        public static bool MeleePressed => GameInputSettings.WasPressedThisFrame(GameAction.Melee);
        /// <summary>Agachar (Ctrl por defeito; o C passou a ser o corpo-a-corpo).</summary>
        public static bool CrouchHeld => GameInputSettings.IsHeld(GameAction.Crouch);
        public static bool CrouchPressed => GameInputSettings.WasPressedThisFrame(GameAction.Crouch);
        /// <summary>Inclinar (Q esquerda / E direita por defeito).</summary>
        public static bool LeanLeftHeld => GameInputSettings.IsHeld(GameAction.LeanLeft);
        public static bool LeanRightHeld => GameInputSettings.IsHeld(GameAction.LeanRight);
        public static bool LeanLeftPressed => GameInputSettings.WasPressedThisFrame(GameAction.LeanLeft);
        public static bool LeanRightPressed => GameInputSettings.WasPressedThisFrame(GameAction.LeanRight);
        /// <summary>A mirar (Lince): tecla premida, ou ligado/desligado em modo alternar (SETTINGS > CONTROLS).</summary>
        public static bool AimHeld
        {
            get
            {
                if (!GameSession.AimToggle) return GameInputSettings.IsHeld(GameAction.Aim);
                // Lido várias vezes por frame: o toque só conta uma vez
                if (s_AimFrame != Time.frameCount)
                {
                    s_AimFrame = Time.frameCount;
                    if (GameUI.GameplayInputAllowed && GameInputSettings.WasPressedThisFrame(GameAction.Aim)) s_AimToggled = !s_AimToggled;
                }
                return s_AimToggled;
            }
        }
        static bool s_AimToggled;
        static int s_AimFrame = -1;
        /// <summary>Sai da luneta no modo alternar (morte, troca de personagem, mudança de modo).</summary>
        public static void ResetAimToggle() => s_AimToggled = false;
        public static bool InteractHeld => GameInputSettings.IsHeld(GameAction.Interact);
        public static bool ThermalTogglePressed => GameInputSettings.WasPressedThisFrame(GameAction.ThermalScope);
        public static bool SuperPressed => ReadSuperPressed();
        public static bool ScoreboardHeld => ReadScoreboardHeld();
        public static bool PausePressed => ReadPausePressed();
        /// <summary>Roda do rato neste frame: +1 para a frente (para longe do jogador), −1 para trás, 0 parada.</summary>
        public static float ScrollNotches => ReadScroll();

#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
        static Mouse M => Mouse.current;

        static Vector2 ReadMove()
        {
            float x = (GameInputSettings.IsHeld(GameAction.MoveRight) ? 1f : 0f) -
                      (GameInputSettings.IsHeld(GameAction.MoveLeft) ? 1f : 0f);
            float y = (GameInputSettings.IsHeld(GameAction.MoveForward) ? 1f : 0f) -
                      (GameInputSettings.IsHeld(GameAction.MoveBackward) ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y) + GamepadInput.LeftStick, 1f);
        }

        // Escala igual ao "Mouse X/Y" do Input Manager antigo (0.1 por pixel).
        static Vector2 ReadLook() => (M == null ? Vector2.zero : M.delta.ReadValue() * 0.1f) + GamepadInput.LookDelta(Time.deltaTime);
        static bool ReadSprint() => GameInputSettings.IsHeld(GameAction.Sprint);
        static bool ReadJumpPressed() => GameInputSettings.WasPressedThisFrame(GameAction.Jump);
        static bool ReadFire() => GameInputSettings.IsHeld(GameAction.Fire);
        static bool ReadSuperPressed() => GameInputSettings.WasPressedThisFrame(GameAction.Super);
        static bool ReadScoreboardHeld() => GameInputSettings.IsHeld(GameAction.Scoreboard);
        static bool ReadPausePressed() => GameInputSettings.WasPressedThisFrame(GameAction.Pause);
        static float ReadScroll()
        {
            float y = M == null ? 0f : M.scroll.ReadValue().y;
            return Mathf.Abs(y) < 0.01f ? GamepadInput.ZoomNotches : Mathf.Sign(y);
        }
#else
        static Vector2 ReadMove()
        {
            float x = (GameInputSettings.IsHeld(GameAction.MoveRight) ? 1f : 0f) -
                      (GameInputSettings.IsHeld(GameAction.MoveLeft) ? 1f : 0f);
            float y = (GameInputSettings.IsHeld(GameAction.MoveForward) ? 1f : 0f) -
                      (GameInputSettings.IsHeld(GameAction.MoveBackward) ? 1f : 0f);
            return Vector2.ClampMagnitude(new Vector2(x, y) + GamepadInput.LeftStick, 1f);
        }

        static Vector2 ReadLook() => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) + GamepadInput.LookDelta(Time.deltaTime);
        static bool ReadSprint() => GameInputSettings.IsHeld(GameAction.Sprint);
        static bool ReadJumpPressed() => GameInputSettings.WasPressedThisFrame(GameAction.Jump);
        static bool ReadFire() => GameInputSettings.IsHeld(GameAction.Fire);
        static bool ReadSuperPressed() => GameInputSettings.WasPressedThisFrame(GameAction.Super);
        static bool ReadScoreboardHeld() => GameInputSettings.IsHeld(GameAction.Scoreboard);
        static bool ReadPausePressed() => GameInputSettings.WasPressedThisFrame(GameAction.Pause);
        static float ReadScroll()
        {
            float y = Input.mouseScrollDelta.y;
            return Mathf.Abs(y) < 0.01f ? GamepadInput.ZoomNotches : Mathf.Sign(y);
        }
#endif
    }
}
