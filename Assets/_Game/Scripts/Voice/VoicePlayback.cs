using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Reprodução 3D da voz de um jogador remoto: AudioSource posicional na cabeça (proximidade: ouve-se até
    /// <see cref="MaxDistance"/> m, aliado ou inimigo) alimentada por um buffer com ~80 ms de folga contra jitter.
    /// </summary>
    public class VoicePlayback : MonoBehaviour
    {
        public const float MinDistance = 4f, MaxDistance = 32f;
        const int RingSize = VoiceCodec.SampleRate;                 // 1 s
        const int StartBuffer = VoiceCodec.SampleRate * 8 / 100;    // 80 ms antes de começar a tocar
        const int MaxBuffered = VoiceCodec.SampleRate * 4 / 10;     // >400 ms: descarta o mais antigo

        readonly float[] m_Ring = new float[RingSize];
        readonly float[] m_Frame = new float[VoiceCodec.FrameSamples];
        readonly object m_Lock = new object();
        int m_Write, m_Read, m_Count;
        bool m_Primed;
        AudioSource m_Source;
        PlayerNetwork m_Owner;

        /// <summary>Tempo (unscaled) do último pacote recebido.</summary>
        public float LastHeard { get; private set; } = -10f;

        public static VoicePlayback Create(PlayerNetwork owner)
        {
            var go = new GameObject("Voice");
            go.transform.SetParent(owner.transform, false);
            go.transform.localPosition = Vector3.up * 1.6f;
            var vp = go.AddComponent<VoicePlayback>();
            vp.m_Owner = owner;
            return vp;
        }

        void Awake()
        {
            m_Source = gameObject.AddComponent<AudioSource>();
            m_Source.clip = AudioClip.Create("voice", RingSize, 1, VoiceCodec.SampleRate, true, OnAudioRead);
            m_Source.loop = true;
            m_Source.playOnAwake = false;
            m_Source.spatialBlend = 1f;
            m_Source.rolloffMode = AudioRolloffMode.Linear;
            m_Source.minDistance = MinDistance;
            m_Source.maxDistance = MaxDistance;
            m_Source.dopplerLevel = 0f;
            m_Source.priority = 32;
            m_Source.Play();
        }

        public void Push(byte[] encoded)
        {
            VoiceCodec.Decode(encoded, m_Frame);
            lock (m_Lock)
            {
                for (int i = 0; i < m_Frame.Length; i++)
                {
                    m_Ring[m_Write] = m_Frame[i];
                    m_Write = (m_Write + 1) % RingSize;
                }
                m_Count += m_Frame.Length;
                if (m_Count > MaxBuffered)
                {
                    int drop = m_Count - StartBuffer;
                    m_Read = (m_Read + drop) % RingSize;
                    m_Count -= drop;
                }
            }
            LastHeard = Time.unscaledTime;
        }

        // Thread de áudio
        void OnAudioRead(float[] data)
        {
            lock (m_Lock)
            {
                if (!m_Primed && m_Count >= StartBuffer) m_Primed = true;
                int n = 0;
                if (m_Primed)
                {
                    n = Mathf.Min(data.Length, m_Count);
                    for (int i = 0; i < n; i++)
                    {
                        data[i] = m_Ring[m_Read];
                        m_Read = (m_Read + 1) % RingSize;
                    }
                    m_Count -= n;
                    if (m_Count == 0) m_Primed = false;
                }
                for (int i = n; i < data.Length; i++) data[i] = 0f;
            }
        }

        void LateUpdate()
        {
            if (m_Owner != null && m_Owner.Visuals != null) transform.position = m_Owner.Visuals.HeadPosition;
            m_Source.volume = GameSession.VoiceEnabled ? GameSession.VoiceVolume : 0f;
        }
    }
}
