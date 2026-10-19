using System;
using UnityEngine;

namespace FPSLAN
{
    [CreateAssetMenu(menuName = "FPS-LAN/Audio/Sfx Catalog")]
    public sealed class SfxCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Cue
        {
            public Sound sound;
            public AudioClip[] clips;
            [Range(0f, 1f)] public float volume = 1f;
            [Range(0f, 0.15f)] public float pitchVariation;
        }

        public Cue[] cues = Array.Empty<Cue>();
    }
}
