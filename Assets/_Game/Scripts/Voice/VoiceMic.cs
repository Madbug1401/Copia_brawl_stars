using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// Captura do microfone por defeito, entregue em frames de 40 ms a 16 kHz (reamostra se o micro não suportar 16 kHz).
    /// O micro fica sempre a gravar durante a partida (sem atraso ao carregar no push-to-talk); só se envia com a tecla.
    /// </summary>
    public static class VoiceMic
    {
        public static bool IsRecording { get; private set; }
        public static string DeviceName { get; private set; }
        /// <summary>Pico do último frame lido (0..1), para o indicador de "a falar".</summary>
        public static float Level { get; private set; }
        /// <summary>Volume médio (RMS) do último frame lido, para o modo automático.</summary>
        public static float Rms { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
        public static bool Available => false;
        public static bool Start() => false;
        public static void Stop() { }
        public static bool TryReadFrame(float[] dst) => false;
#else
        static AudioClip s_Clip;
        static int s_Rate, s_ReadPos, s_ClipLen;
        static float[] s_Raw = new float[0];
        static float[] s_TmpA = new float[0], s_TmpB = new float[0];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { IsRecording = false; s_Clip = null; DeviceName = null; Level = 0f; Rms = 0f; }

        public static bool Available => Microphone.devices != null && Microphone.devices.Length > 0;

        public static bool Start()
        {
            if (IsRecording) return true;
#if UNITY_ANDROID
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                return false;
            }
#endif
            if (!Available) return false;
            DeviceName = Microphone.devices[0];
            Microphone.GetDeviceCaps(DeviceName, out int min, out int max);
            s_Rate = (min == 0 && max == 0) ? VoiceCodec.SampleRate : Mathf.Clamp(VoiceCodec.SampleRate, min, max);
            s_Clip = Microphone.Start(DeviceName, true, 1, s_Rate);
            if (s_Clip == null) return false;
            s_ClipLen = s_Clip.samples;
            s_ReadPos = 0;
            IsRecording = true;
            return true;
        }

        public static void Stop()
        {
            if (!IsRecording) return;
            Microphone.End(DeviceName);
            IsRecording = false;
            s_Clip = null;
            Level = 0f;
        }

        /// <summary>Lê o próximo frame de 40 ms (16 kHz) se já houver amostras suficientes.</summary>
        public static bool TryReadFrame(float[] dst)
        {
            if (!IsRecording || s_Clip == null) return false;
            int pos = Microphone.GetPosition(DeviceName);
            int available = (pos - s_ReadPos + s_ClipLen) % s_ClipLen;
            int need = Mathf.RoundToInt(VoiceCodec.FrameSamples * (s_Rate / (float)VoiceCodec.SampleRate));
            // Atrasado demais (ex.: frame longo): salta para perto do presente em vez de acumular latência
            if (available > s_Rate / 4) { s_ReadPos = (pos - need + s_ClipLen) % s_ClipLen; available = need; }
            if (available < need) return false;

            if (s_Raw.Length != need) s_Raw = new float[need];
            if (s_ReadPos + need <= s_ClipLen) s_Clip.GetData(s_Raw, s_ReadPos);
            else
            {
                int a = s_ClipLen - s_ReadPos, b = need - a;
                if (s_TmpA.Length != a) s_TmpA = new float[a];
                if (s_TmpB.Length != b) s_TmpB = new float[b];
                s_Clip.GetData(s_TmpA, s_ReadPos);
                s_Clip.GetData(s_TmpB, 0);
                System.Array.Copy(s_TmpA, 0, s_Raw, 0, a);
                System.Array.Copy(s_TmpB, 0, s_Raw, a, b);
            }
            s_ReadPos = (s_ReadPos + need) % s_ClipLen;

            float peak = 0f;
            if (need == VoiceCodec.FrameSamples) System.Array.Copy(s_Raw, dst, need);
            else
            {
                float step = (need - 1) / (float)(VoiceCodec.FrameSamples - 1);
                for (int i = 0; i < VoiceCodec.FrameSamples; i++)
                {
                    float x = i * step; int i0 = (int)x; int i1 = Mathf.Min(i0 + 1, need - 1);
                    dst[i] = Mathf.Lerp(s_Raw[i0], s_Raw[i1], x - i0);
                }
            }
            float sum = 0f;
            for (int i = 0; i < VoiceCodec.FrameSamples; i++) { float v = Mathf.Abs(dst[i]); if (v > peak) peak = v; sum += v * v; }
            Level = peak;
            Rms = Mathf.Sqrt(sum / VoiceCodec.FrameSamples);
            return true;
        }

        /// <summary>Descarta o que foi gravado sem push-to-talk (para não enviar áudio antigo ao carregar).</summary>
        public static void Drain()
        {
            if (!IsRecording) return;
            s_ReadPos = Microphone.GetPosition(DeviceName);
            Level = 0f;
            Rms = 0f;
        }
#endif
    }
}
