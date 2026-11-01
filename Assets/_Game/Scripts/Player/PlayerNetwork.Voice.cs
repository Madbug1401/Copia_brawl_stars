using Unity.Netcode;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Voz por proximidade (push-to-talk, [V] por defeito). O dono grava e envia frames de 40 ms ao host
    /// (unreliable), o host valida e reencaminha a todos os outros; cada cliente toca a voz em 3D na cabeça do jogador,
    /// por isso aliados e inimigos só se ouvem quando estão perto (até <see cref="VoicePlayback.MaxDistance"/> m).
    /// </summary>
    public partial class PlayerNetwork
    {
        const float VoiceTail = 0.12f;          // continua a enviar um pouco depois de largar a tecla
        const float VoiceAutoHold = 0.45f;      // automático: continua aberto entre palavras
        const int VoiceMaxPacketsPerSecond = 30; // 25 esperados (40 ms)

        readonly float[] m_VoicePcm = new float[VoiceCodec.FrameSamples];
        VoicePlayback m_VoicePlayback;
        float m_VoiceSendUntil;
        ushort m_VoiceSeq;
        bool m_VoiceMicTried;
        double m_ServerVoiceWindow;
        int m_ServerVoiceCount;

        /// <summary>Este jogador está a falar (local: a transmitir; remoto: voz recebida há pouco).</summary>
        public bool IsSpeaking => IsLocalHuman
            ? VoiceTransmitting
            : m_VoicePlayback != null && Time.unscaledTime - m_VoicePlayback.LastHeard < 0.25f;

        public bool VoiceTransmitting { get; private set; }

        void TickVoice()
        {
            if (!IsSpawned || !IsLocalHuman) return;
            if (!GameSession.VoiceEnabled)
            {
                VoiceTransmitting = false;
                if (VoiceMic.IsRecording) VoiceMic.Stop();
                m_VoiceMicTried = false;
                return;
            }
            if (!VoiceMic.IsRecording)
            {
                // Tenta uma vez por spawn (Android: pede permissão e volta a tentar quando a tecla for usada)
                if (!m_VoiceMicTried || GameInputSettings.WasPressedThisFrame(GameAction.PushToTalk))
                {
                    m_VoiceMicTried = true;
                    VoiceMic.Start();
                }
                if (!VoiceMic.IsRecording) { VoiceTransmitting = false; return; }
            }

            if (GameSession.VoiceMode == VoiceMode.Automatic)
            {
                // Deteção de voz: cada frame acima do limiar mantém a transmissão aberta mais um pouco
                float threshold = GameSession.VoiceActivationThreshold;
                int n = 0;
                while (n++ < 4 && VoiceMic.TryReadFrame(m_VoicePcm))
                {
                    if (VoiceMic.Rms >= threshold) m_VoiceSendUntil = Time.unscaledTime + VoiceAutoHold;
                    if (Time.unscaledTime <= m_VoiceSendUntil) SendVoiceFrame();
                }
                VoiceTransmitting = Time.unscaledTime <= m_VoiceSendUntil;
                return;
            }

            bool held = GameUI.GameplayInputAllowed && GameInputSettings.IsHeld(GameAction.PushToTalk);
            if (held && !VoiceTransmitting && Time.unscaledTime > m_VoiceSendUntil) VoiceMic.Drain();
            if (held) m_VoiceSendUntil = Time.unscaledTime + VoiceTail;
            VoiceTransmitting = Time.unscaledTime <= m_VoiceSendUntil;

            if (!VoiceTransmitting) { VoiceMic.Drain(); return; }
            int guard = 0;
            while (guard++ < 4 && VoiceMic.TryReadFrame(m_VoicePcm)) SendVoiceFrame();
        }

        void SendVoiceFrame()
        {
            var bytes = new byte[VoiceCodec.FrameBytes];
            VoiceCodec.Encode(m_VoicePcm, bytes);
            VoiceUpRpc(bytes, m_VoiceSeq++);
        }

        void ShutdownVoice()
        {
            if (IsOwner && !IsBot) { VoiceMic.Stop(); VoiceTransmitting = false; }
            if (m_VoicePlayback != null) Destroy(m_VoicePlayback.gameObject);
            m_VoicePlayback = null;
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        void VoiceUpRpc(byte[] data, ushort seq)
        {
            if (data == null || data.Length != VoiceCodec.FrameBytes || IsBot) return;
            double now = NetworkManager.ServerTime.Time;
            if (now - m_ServerVoiceWindow >= 1.0) { m_ServerVoiceWindow = now; m_ServerVoiceCount = 0; }
            if (++m_ServerVoiceCount > VoiceMaxPacketsPerSecond) return;
            VoiceDownRpc(data, seq);
        }

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Server)]
        void VoiceDownRpc(byte[] data, ushort seq)
        {
            if (data == null || data.Length != VoiceCodec.FrameBytes) return;
            if (m_VoicePlayback == null) m_VoicePlayback = VoicePlayback.Create(this);
            m_VoicePlayback.Push(data);
        }
    }
}
