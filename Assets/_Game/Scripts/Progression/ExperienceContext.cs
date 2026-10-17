using System;
using System.Collections.Generic;

namespace FPSLAN
{
    public enum ExperienceEventType : byte
    {
        PlayerLevelUp,
        BrawlerLevelUp,
        PetLevelUp,
        PetEvolved,
        PetUnlocked,
        SkinUnlocked,
        CosmeticUnlocked,
        QuestCompleted,
        RewardClaimed,
        CoinsGained,
        UpgradeBought,
    }

    /// <summary>Algo que o jogador deve ver/ouvir (subida de nível, desbloqueio, evolução, missão...).</summary>
    public struct ExperienceEvent
    {
        public ExperienceEventType type;
        public string title;
        public string detail;
        public string itemId;
    }

    /// <summary>
    /// Estado partilhado pelos managers de progressão: o perfil gravado, os dados e a fila de eventos para a UI.
    /// Sem Unity (testável). O relógio é injetável para testar recomeços diários/semanais.
    /// </summary>
    public sealed class ExperienceContext
    {
        public PlayerProfileData profile;
        public ExperienceCatalog catalog;
        public Func<DateTime> utcNow = () => DateTime.UtcNow;
        public readonly List<ExperienceEvent> events = new List<ExperienceEvent>();
        /// <summary>Marcado quando algo mudou e deve ser gravado.</summary>
        public bool dirty;

        public ExperienceContext(PlayerProfileData profile, ExperienceCatalog catalog)
        {
            this.profile = profile ?? new PlayerProfileData();
            this.catalog = catalog ?? new ExperienceCatalog();
        }

        public ProgressionSettings Settings => catalog.progression ?? (catalog.progression = ProgressionSettings.CreateDefault());

        public void Raise(ExperienceEventType type, string title, string detail = "", string itemId = "")
        {
            events.Add(new ExperienceEvent { type = type, title = title, detail = detail ?? "", itemId = itemId ?? "" });
            // A fila não cresce sem limite se a UI não a ler (ex.: servidor dedicado)
            if (events.Count > 64) events.RemoveAt(0);
        }

        public void MarkDirty() => dirty = true;
    }
}
