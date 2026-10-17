namespace FPSLAN
{
    /// <summary>
    /// Valores de referência dos 6 Brawlers ("Game Experience"). O gerador de dados do editor
    /// (FPS-LAN → Gerar dados dos Brawlers) escreve-os nos BrawlerDefinition/AttackDefinition, e os testes verificam
    /// que os assets e as contas de dano batem com eles. Em jogo só contam os assets (ajustáveis no Inspector);
    /// esta classe nunca altera dados em runtime.
    /// </summary>
    public static class BrawlerTuning
    {
        /// <summary>Versão dos dados gerados. Subir este número faz o editor regenerar os assets dos Brawlers uma vez.</summary>
        public const int DataVersion = 3;

        /// <summary>Ekko (id técnico: gunslinger) — duas metralhadoras compactas; Super: Minigun temporária.</summary>
        public static class Ekko
        {
            public const string Id = "gunslinger";
            public const string Name = "Ekko";
            public const int MaxHealth = 120;
            public const int BulletsPerAttack = 3;
            public const float BulletInterval = 0.07f;
            public const int BulletDamage = 11;
            public const float NearMultiplier = 1.15f;
            public const float FarMultiplier = 0.6f;
            public const float SpreadDegrees = 1.6f;
            public const float Range = 26f;
            public const float BulletSpeed = 90f;
            public const int Charges = 4;
            public const float ReloadPerCharge = 0.95f;
            public const float Cooldown = 0.3f;
            public const float SuperChargeRequired = 260f;

            public const int MinigunBulletsPerAttack = 4;
            public const int MinigunBulletDamage = 5;
            public const int MinigunAttacks = 10;
            public const float MinigunFireInterval = 0.3f;
            public const float MinigunDuration = 3.6f;
            /// <summary>Dano total potencial do Super = ataques × balas × dano (200).</summary>
            public const int MinigunPotentialDamage = MinigunAttacks * MinigunBulletsPerAttack * MinigunBulletDamage;
        }

        /// <summary>Mr.S (id técnico: sprayer) — duas latas de spray em cone curto; Super: granadas de veneno.</summary>
        public static class MrS
        {
            public const string Id = "sprayer";
            public const string Name = "Mr.S";
            public const int MaxHealth = 160;
            public const float ConeRange = 7f;
            public const float ConeAngle = 38f;
            public const float ConeDuration = 0.6f;
            public const float ConeTickInterval = 0.2f;
            public const float ConeExpandTime = 0.25f;
            /// <summary>Ticks de dano por ataque com estes tempos (0.15 s, 0.35 s, 0.55 s).</summary>
            public const int ConeTicks = 3;
            public const int TickDamage = 20;
            public const float NearMultiplier = 1f;
            public const float FarMultiplier = 0.84f;
            public const int MaxTotalPerTarget = 60;
            public const int Charges = 3;
            public const float ReloadPerCharge = 1.7f;
            public const float Cooldown = 0.65f;
            public const float SuperChargeRequired = 170f;

            public const int GrenadeCount = 2;
            public const float ZoneDuration = 12f;
            public const float ZoneRadius = 2.8f;
            public const float ZoneTickInterval = 1f;
            public const int ZoneTickDamage = 6;
            public const float ZoneSlowFactor = 0.85f;
        }

        /// <summary>Riot (id técnico: shotgunner) — caçadeira de bagos; mantém o Super e a vida do Brasa.</summary>
        public static class Riot
        {
            public const string Id = "shotgunner";
            public const string Name = "Riot";
            public const int MaxHealth = 135;
            public const int Pellets = 8;
            public const int PelletDamage = 7;
            public const float NearMultiplier = 1f;
            public const float FarMultiplier = 0.33f;
            public const float FanDegrees = 16f;
            public const float SpreadDegrees = 2.2f;
            public const float Range = 13f;
            public const int MaxTotalPerTarget = 60;
            public const int Charges = 3;
            public const float ReloadPerCharge = 1.5f;
            public const float Cooldown = 0.6f;
        }

        /// <summary>Lupus Marinus / L.M (id técnico: artillery) — TNT em arco; Super: 5 minas de proximidade.</summary>
        public static class LupusMarinus
        {
            public const string Id = "artillery";
            public const string Name = "Lupus Marinus (L.M)";
            public const string ShortName = "L.M";
            public const int MaxHealth = 100;
            public const int TntDamage = 40;
            public const float TntRadius = 2.2f;
            public const float TntEdgeMultiplier = 0.35f;
            public const float TntFuse = 0.35f;
            public const int Charges = 3;
            public const float ReloadPerCharge = 1.45f;
            public const float Cooldown = 0.55f;
            public const float SuperChargeRequired = 160f;

            public const int Mines = 5;
            public const int MineDamage = 45;
            public const float MineRadius = 2.6f;
            public const float MineArmTime = 1f;
            public const float MineTriggerRadius = 1.7f;
            public const float MineWarning = 0.5f;
            public const float MineLifetime = 25f;
            public const int MaxActiveMines = 10;
        }

        /// <summary>N.Cage (id técnico: trapper) — três flechas; Super: três armadilhas que prendem.</summary>
        public static class NCage
        {
            public const string Id = "trapper";
            public const string Name = "N.Cage";
            public const int MaxHealth = 110;
            public const int Arrows = 3;
            public const int ArrowDamage = 34;
            /// <summary>Total máximo no mesmo alvo com as 3 flechas.</summary>
            public const int MaxTotalPerTarget = 100;
            public const float FanDegrees = 9f;
            public const float Range = 24f;
            public const int Charges = 3;
            public const float ReloadPerCharge = 1.7f;
            public const float Cooldown = 0.6f;
            public const float SuperChargeRequired = 200f;

            public const int Snares = 3;
            public const float RootDuration = 5f;
            public const int PoisonTotal = 40;
            public const float RootImmunity = 2.5f;
            public const float SnareLifetime = 20f;
            public const int MaxActiveSnares = 6;
        }

        /// <summary>Lince (id técnico: sniper) — atiradora; Super: Tiro fatal (carga automática).</summary>
        public static class Lince
        {
            public const string Id = "sniper";
            public const string Name = "Lince";
            public const int MaxHealth = 85;
            public const int BodyDamage = 38;
            public const int HeadDamage = 50;
            public const float Range = 55f;
            public const int Charges = 3;
            public const float ReloadPerCharge = 2.2f;
            public const float Cooldown = 1.1f;

            /// <summary>Fração de carga por segundo (≈ 30 s até ficar pronto).</summary>
            public const float FatalAutoCharge = 1f / 30f;
            public const int FatalBodyDamage = 45;
            public const float FatalRange = 70f;
            public const float FatalArmedWindow = 8f;
        }
    }
}
