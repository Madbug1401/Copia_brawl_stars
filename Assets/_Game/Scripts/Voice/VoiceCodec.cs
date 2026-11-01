using UnityEngine;

namespace FPSLAN
{
    public enum VoiceMode { PushToTalk = 0, Automatic = 1, Off = 2 }

    /// <summary>
    /// Codec de voz simples (μ-law G.711, 8 bits por amostra, 16 kHz mono): sem dependências e mais do que
    /// suficiente para LAN (~16 KB/s por jogador a falar). Para o online, trocar por Opus mantendo o mesmo tamanho de frame.
    /// </summary>
    public static class VoiceCodec
    {
        public const int SampleRate = 16000;
        /// <summary>40 ms por pacote.</summary>
        public const int FrameSamples = 640;
        public const int FrameBytes = FrameSamples;

        const int Bias = 0x84, Clip = 32635;
        static readonly float[] s_Decode = BuildDecodeTable();

        public static void Encode(float[] pcm, byte[] dst)
        {
            for (int i = 0; i < FrameSamples; i++) dst[i] = EncodeSample(pcm[i]);
        }

        public static void Decode(byte[] src, float[] dst)
        {
            for (int i = 0; i < FrameSamples; i++) dst[i] = s_Decode[src[i]];
        }

        static byte EncodeSample(float f)
        {
            int s = (int)(Mathf.Clamp(f, -1f, 1f) * 32767f);
            int sign = (s >> 8) & 0x80;
            if (sign != 0) s = -s;
            if (s > Clip) s = Clip;
            s += Bias;
            int exponent = 7;
            for (int mask = 0x4000; (s & mask) == 0 && exponent > 0; mask >>= 1) exponent--;
            int mantissa = (s >> (exponent + 3)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        static float[] BuildDecodeTable()
        {
            var t = new float[256];
            for (int i = 0; i < 256; i++)
            {
                int u = ~i & 0xFF;
                int sign = u & 0x80, exponent = (u >> 4) & 0x07, mantissa = u & 0x0F;
                int s = ((mantissa << 3) + Bias) << exponent;
                s -= Bias;
                t[i] = (sign != 0 ? -s : s) / 32768f;
            }
            return t;
        }
    }
}
