using System;
using UnityEngine;

namespace FPSLAN
{
    /// <summary>Como um pet, skin ou cosmético é desbloqueado.</summary>
    public enum UnlockMethod : byte
    {
        /// <summary>Disponível desde o início.</summary>
        Default,
        /// <summary>Ao chegar ao nível de jogador <c>value</c>.</summary>
        PlayerLevel,
        /// <summary>Recompensa de uma missão (refId = id da missão).</summary>
        Mission,
        /// <summary>Recompensa de uma conquista (refId = id da conquista).</summary>
        Achievement,
        /// <summary>Compra com <c>value</c> moedas.</summary>
        Coins,
        /// <summary>Recompensa de um desafio (refId = id do desafio).</summary>
        Challenge,
        /// <summary>Evento futuro (refId = id do evento); fica bloqueado até o evento existir.</summary>
        Event,
        /// <summary>Só por recompensa (níveis de pet, etc.); não aparece à venda.</summary>
        RewardOnly,
    }

    [Serializable]
    public class UnlockRule
    {
        public UnlockMethod method = UnlockMethod.Default;
        [Tooltip("Nível de jogador (PlayerLevel) ou preço em moedas (Coins).")]
        public int value;
        [Tooltip("Id da missão, conquista, desafio ou evento.")]
        public string refId = "";

        public UnlockRule() { }
        public UnlockRule(UnlockMethod method, int value = 0, string refId = "")
        {
            this.method = method;
            this.value = value;
            this.refId = refId ?? "";
        }

        public string Describe()
        {
            switch (method)
            {
                case UnlockMethod.Default: return "Disponível";
                case UnlockMethod.PlayerLevel: return "Nível de jogador " + value;
                case UnlockMethod.Mission: return "Recompensa de missão";
                case UnlockMethod.Achievement: return "Conquista";
                case UnlockMethod.Coins: return value + " moedas";
                case UnlockMethod.Challenge: return "Desafio";
                case UnlockMethod.Event: return "Evento (em breve)";
                default: return "Recompensa";
            }
        }
    }
}
