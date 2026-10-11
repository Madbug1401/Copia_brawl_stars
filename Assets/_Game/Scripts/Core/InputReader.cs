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
        public static Vector2 Move => MobileControls.Available ? MobileControls.Move : ReadMove();
        public static Vector2 Look => MobileControls.Available ? MobileControls.Look : ReadLook();
        public static bool Sprint => MobileControls.Available ? MobileControls.Sprint : ReadSprint();
        public static bool JumpPressed => MobileControls.Available ? MobileControls.JumpPressed : ReadJumpPressed();
        public static bool Fire => MobileControls.Available ? MobileControls.Fire : ReadFire();
        public static bool AimHeld => MobileControls.Available ? MobileControls.AimHeld : GameInputSettings.IsHeld(GameAction.Aim);
        public static bool SuperPressed => MobileControls.Available ? MobileControls.SuperPressed : ReadSuperPressed();
        public static bool ScoreboardHeld => MobileControls.Available ? MobileControls.ScoreboardHeld : ReadScoreboardHeld();
        public static bool PausePressed => MobileControls.Available ? MobileControls.PausePressed : ReadPausePressed();

#if FPSLAN_INPUTSYS && ENABLE_INPUT_SYSTEM
        static Mouse M => Mouse.current;

        static Vector2 ReadMove()
        {
            float x = (GameInputSettings.IsHeld(GameAction.MoveRight) ? 1f : 0f) -
                      (GameInputSettings.IsHeld(GameAction.MoveLeft) ? 1f : 0f);
            float y = (GameInputSettings.IsHeld(GameAction.MoveForward) ? 1f : 0f) -
                      (GameInputSettings.IsHeld(GameAction.MoveBackward) ? 1f : 0f);
            return new Vector2(x, y);
        }

        // Escala igual ao "Mouse X/Y" do Input Manager antigo (0.1 por pixel).
        static Vector2 ReadLook() => M == null ? Vector2.zero : M.delta.ReadValue() * 0.1f;
        static bool ReadSprint() => GameInputSettings.IsHeld(GameAction.Sprint);
        static bool ReadJumpPressed() => GameInputSettings.WasPressedThisFrame(GameAction.Jump);
        static bool ReadFire() => GameInputSettings.IsHeld(GameAction.Fire);
        static bool ReadSuperPressed() => GameInputSettings.WasPressedThisFrame(GameAction.Super) ||
            (!GameInputSettings.HasCustomBinding(GameAction.Super) && GameInputSettings.WasPressedThisFrame(KeyCode.E));
        static bool ReadScoreboardHeld() => GameInputSettings.IsHeld(GameAction.Scoreboard);
        static bool ReadPausePressed() => GameInputSettings.WasPressedThisFrame(GameAction.Pause);
#else
        static Vector2 ReadMove()
        {
            float x = (GameInputSettings.IsHeld(GameAction.MoveRight) ? 1f : 0f) -
                      (GameInputSettings.IsHeld(GameAction.MoveLeft) ? 1f : 0f);
            float y = (GameInputSettings.IsHeld(GameAction.MoveForward) ? 1f : 0f) -
                      (GameInputSettings.IsHeld(GameAction.MoveBackward) ? 1f : 0f);
            return new Vector2(x, y);
        }

        static Vector2 ReadLook() => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
        static bool ReadSprint() => GameInputSettings.IsHeld(GameAction.Sprint);
        static bool ReadJumpPressed() => GameInputSettings.WasPressedThisFrame(GameAction.Jump);
        static bool ReadFire() => GameInputSettings.IsHeld(GameAction.Fire);
        static bool ReadSuperPressed() => GameInputSettings.WasPressedThisFrame(GameAction.Super) ||
            (!GameInputSettings.HasCustomBinding(GameAction.Super) && Input.GetKeyDown(KeyCode.E));
        static bool ReadScoreboardHeld() => GameInputSettings.IsHeld(GameAction.Scoreboard);
        static bool ReadPausePressed() => GameInputSettings.WasPressedThisFrame(GameAction.Pause);
#endif
    }
}
