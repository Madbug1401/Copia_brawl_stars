namespace FPSLAN
{
    /// <summary>Concise English adaptations of Better_Together_Historias_das_Personagens.pdf.</summary>
    public static class CharacterStories
    {
        public static string Role(string id)
        {
            switch (id)
            {
                case "shotgunner": return "THE BREACHER";
                case "sniper": return "THE SHARPSHOOTER";
                case "sprayer": return "THE STREET ARTIST";
                case "artillery": return "THE DEMOLITIONIST";
                case "trapper": return "THE SABOTEUR";
                case "gunslinger": return "THE GUNSLINGER";
                default: return "";
            }
        }
        public static string Summary(string id)
        {
            switch (id)
            {
                case "shotgunner": return "Fearless and impulsive, Riot breaks through at close range. She trusts brute force — and Lince.";
                case "sniper": return "A patient, calculating marksman. Lince controls the fight from a distance and always watches Riot's back.";
                case "sprayer": return "An inventive street artist who turns paint into a weapon. MS controls space with color and creativity.";
                case "artillery": return "A curious explorer with a talent for trouble. Lupus Marinus uses heavy weapons to clear whole areas.";
                case "trapper": return "A patient schemer who lets enemies come to him. N.Cage controls the battlefield with traps and sabotage.";
                case "gunslinger": return "Quick on the draw and fiercely loyal. Ekko brings fast reflexes, precise pistols and a fearless grin.";
                default: return "";
            }
        }
    }
}
