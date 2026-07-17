namespace Loom.Unity.Samples.HordeRush
{
    static class GameConfig
    {
        public const float ArenaWidth = 960f;
        public const float ArenaHeight = 540f;

        public const float PlayerSpeed = 220f;
        public const float PlayerRadius = 14f;
        public const int PlayerMaxHealth = 5;
        public const float FireInterval = 0.12f;
        public const float BulletSpeed = 520f;
        public const float BulletRadius = 4f;
        public const float BulletLife = 1.1f;
        public const int BulletDamage = 1;

        public const float HitFlashDuration = 0.18f;
        public const float WaveInterval = 2.2f;
        public const float EnemySpawnPadding = 40f;

        /// <summary>How many pair-resolve passes per frame (2 softens stacking in dense packs).</summary>
        public const int EnemySeparationIterations = 2;
        /// <summary>Extra gap beyond radius+radius so sprites don't visually kiss.</summary>
        public const float EnemySeparationPadding = 1.5f;

        // Linear difficulty vs wave (wave starts at 1).
        public const int EnemiesWaveBase = 6;
        public const int EnemiesPerWave = 3;
        public const int EnemiesWaveCap = 120;

        public const float EnemySpeedBase = 85f;
        public const float EnemySpeedPerWave = 7f;
        public const float EnemySpeedCap = 280f;

        public const float EnemyRadiusBase = 12f;
        public const float EnemyRadiusPerWave = 0.25f;
        public const float EnemyRadiusCap = 22f;

        public const float EnemyHpPerWave = 0.35f; // hp = 1 + (wave-1)*HpPerWave
        public const int EnemyScoreBase = 10;
        public const int EnemyScorePerWave = 2;
    }
}
