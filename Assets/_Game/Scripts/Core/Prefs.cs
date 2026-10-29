using System.Collections.Generic;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>
    /// PlayerPrefs com cache em memória. No Windows cada PlayerPrefs.GetInt/HasKey vai ao registo (~16 µs medidos) e o
    /// input, o HUD e as definições liam teclas e opções dezenas de vezes por frame. Aqui cada chave é lida do registo
    /// uma vez; as escritas atualizam a cache e o registo. Mesma semântica do PlayerPrefs: uma chave que não existe
    /// devolve o valor por omissão da chamada (que não fica guardado).
    /// Todo o código do jogo deve usar esta classe em vez de PlayerPrefs, para a cache nunca ficar desatualizada.
    /// (Duas janelas do jogo no mesmo PC só veem as alterações uma da outra ao reiniciar.)
    /// </summary>
    public static class Prefs
    {
        struct Entry<T> { public bool exists; public T value; }

        static readonly Dictionary<string, Entry<int>> s_Ints = new Dictionary<string, Entry<int>>();
        static readonly Dictionary<string, Entry<float>> s_Floats = new Dictionary<string, Entry<float>>();
        static readonly Dictionary<string, Entry<string>> s_Strings = new Dictionary<string, Entry<string>>();
        static readonly Dictionary<string, bool> s_HasKey = new Dictionary<string, bool>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Clear();

        /// <summary>Esquece tudo o que está em cache (a próxima leitura vai ao registo).</summary>
        public static void Clear()
        {
            s_Ints.Clear(); s_Floats.Clear(); s_Strings.Clear(); s_HasKey.Clear();
        }

        public static bool HasKey(string key)
        {
            if (!s_HasKey.TryGetValue(key, out bool has)) s_HasKey[key] = has = PlayerPrefs.HasKey(key);
            return has;
        }

        public static int GetInt(string key, int defaultValue = 0)
        {
            if (!s_Ints.TryGetValue(key, out var e))
            {
                // Chave de outro tipo: o PlayerPrefs devolve o valor por omissão, por isso conta como inexistente
                // (só se testa quando a chave existe: uma chave nova custa um acesso ao registo, não três)
                if (HasKey(key))
                {
                    int a = PlayerPrefs.GetInt(key, 0), b = PlayerPrefs.GetInt(key, 1);
                    e.exists = a == b;
                    e.value = a;
                }
                s_Ints[key] = e;
            }
            return e.exists ? e.value : defaultValue;
        }

        public static float GetFloat(string key, float defaultValue = 0f)
        {
            if (!s_Floats.TryGetValue(key, out var e))
            {
                if (HasKey(key))
                {
                    float a = PlayerPrefs.GetFloat(key, 0f), b = PlayerPrefs.GetFloat(key, 1f);
                    e.exists = a == b || float.IsNaN(a) && float.IsNaN(b);   // um NaN guardado continua a existir
                    e.value = a;
                }
                s_Floats[key] = e;
            }
            return e.exists ? e.value : defaultValue;
        }

        public static string GetString(string key, string defaultValue = "")
        {
            if (!s_Strings.TryGetValue(key, out var e))
            {
                if (HasKey(key))
                {
                    string a = PlayerPrefs.GetString(key, "\u0001"), b = PlayerPrefs.GetString(key, "\u0002");
                    e.exists = a == b;
                    e.value = a;
                }
                s_Strings[key] = e;
            }
            return e.exists ? e.value : defaultValue;
        }

        public static void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            Written(key);
            s_Ints[key] = new Entry<int> { exists = true, value = value };
        }

        public static void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            Written(key);
            s_Floats[key] = new Entry<float> { exists = true, value = value };
        }

        public static void SetString(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            Written(key);
            s_Strings[key] = new Entry<string> { exists = true, value = value };
        }

        public static void DeleteKey(string key)
        {
            PlayerPrefs.DeleteKey(key);
            Forget(key);
            s_HasKey[key] = false;
        }

        public static void Save() => PlayerPrefs.Save();

        // Uma chave guarda um só tipo: escrever num tipo invalida as leituras em cache dos outros
        static void Written(string key)
        {
            Forget(key);
            s_HasKey[key] = true;
        }

        static void Forget(string key)
        {
            s_Ints.Remove(key); s_Floats.Remove(key); s_Strings.Remove(key);
        }
    }
}
