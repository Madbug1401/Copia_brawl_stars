using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Emote de spray SITA: manter [Y] abre a roda de cores (o rato escolhe, a câmara fica parada), largar pinta o
    /// logótipo SITA na parede/chão para onde se olha. Toque rápido em [Y] repete a última cor.
    /// O host valida (vivo, distância, espera) e manda todos desenhar o spray. Só cosmético.
    /// </summary>
    public partial class PlayerNetwork
    {
        public const float SprayCooldown = 2.5f, SprayRange = 4.5f;
        const float WheelDeadZone = 0.3f, QuickTapTime = 0.2f;
        const string LastSprayPref = "fpslan.spray.last";

        public bool SprayWheelOpen { get; private set; }
        public Vector2 SprayWheelVector { get; private set; }
        public int SprayWheelSelection { get; private set; } = -1;
        public float SprayCooldownRemaining => Mathf.Max(0f, m_NextSprayLocal - Time.time);
        public string SprayHint => Time.time < m_SprayHintUntil ? m_SprayHint : null;
        public static int LastSprayColor
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(LastSprayPref, 0), 0, Branding.SprayColors.Length - 1);
            set => PlayerPrefs.SetInt(LastSprayPref, value);
        }

        float m_NextSprayLocal, m_WheelOpenedAt, m_SprayHintUntil;
        string m_SprayHint;
        double m_ServerNextSpray;

        void TickSpray()
        {
            if (!IsSpawned || !IsLocalHuman) return;
            bool can = GameUI.GameplayInputAllowed && Health != null && !Health.IsDead && !IsFeedingAnim && !m_FeedPending;
            bool held = can && GameInputSettings.IsHeld(GameAction.Emote);

            if (held && !SprayWheelOpen)
            {
                SprayWheelOpen = true;
                SprayWheelVector = Vector2.zero;
                SprayWheelSelection = -1;
                m_WheelOpenedAt = Time.unscaledTime;
                Sfx.Play2D(Sound.UiClick, 0.35f);
            }
            if (!SprayWheelOpen) return;

            if (held)
            {
                var v = SprayWheelVector + InputReader.Look * 0.02f;
                if (v.sqrMagnitude > 1f) v.Normalize();
                SprayWheelVector = v;
                int previous = SprayWheelSelection;
                SprayWheelSelection = v.magnitude < WheelDeadZone ? -1 : WheelIndex(v);
                if (SprayWheelSelection != previous && SprayWheelSelection >= 0) Sfx.Play2D(Sound.UiClick, 0.2f);
                return;
            }

            // Largou: pinta (ou cancela se ainda não escolheu e não foi um toque rápido)
            SprayWheelOpen = false;
            int color = SprayWheelSelection;
            if (color < 0 && Time.unscaledTime - m_WheelOpenedAt <= QuickTapTime) color = LastSprayColor;
            SprayWheelSelection = -1;
            if (color >= 0 && can) TrySpray(color);
        }

        /// <summary>Setor da roda: 0 = topo, sentido horário.</summary>
        public static int WheelIndex(Vector2 v)
        {
            int n = Branding.SprayColors.Length;
            float angle = Mathf.Atan2(v.x, v.y) * Mathf.Rad2Deg;
            if (angle < 0f) angle += 360f;
            return Mathf.RoundToInt(angle / (360f / n)) % n;
        }

        void SprayHintText(string text)
        {
            m_SprayHint = text;
            m_SprayHintUntil = Time.time + 1.6f;
        }

        void TrySpray(int color)
        {
            if (SprayCooldownRemaining > 0f)
            {
                SprayHintText("SPRAY READY IN " + Mathf.CeilToInt(SprayCooldownRemaining) + "s");
                return;
            }
            var cam = Visuals != null ? Visuals.PlayerCamera : null;
            var origin = cam != null ? cam.transform.position : transform.position + Vector3.up * 1.6f;
            var dir = cam != null ? cam.transform.forward : transform.forward;
            if (!CombatMath.Raycast(origin, dir, SprayRange, this, out var hit) || hit.collider.GetComponentInParent<PlayerHealth>() != null)
            {
                SprayHintText("GET CLOSER · AIM AT A WALL OR THE FLOOR");
                return;
            }
            LastSprayColor = color;
            m_NextSprayLocal = Time.time + SprayCooldown;
            SprayRpc(hit.point, hit.normal, dir, (byte)color);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        void SprayRpc(Vector3 point, Vector3 normal, Vector3 viewForward, byte color)
        {
            var now = NetworkManager.ServerTime.Time;
            if (Health == null || Health.IsDead || now < m_ServerNextSpray || color >= Branding.SprayColors.Length) return;
            if (float.IsNaN(point.x + point.y + point.z) || normal.sqrMagnitude < 0.5f || normal.sqrMagnitude > 1.5f) return;
            var head = transform.position + Vector3.up * 1.6f;
            if ((point - head).sqrMagnitude > (SprayRange + 1.5f) * (SprayRange + 1.5f)) return;
            m_ServerNextSpray = now + SprayCooldown * 0.8f;
            SprayEffectRpc(point, normal, viewForward, color);
        }

        [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
        void SprayEffectRpc(Vector3 point, Vector3 normal, Vector3 viewForward, byte color)
        {
            SprayDecals.Spawn(Id, point, normal, viewForward, color);
            Visuals?.OnSpray(point, color);
        }
    }
}
