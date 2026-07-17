namespace Loom.Unity.Samples.FlappyBird
{
    static class GameConfig
    {
        public const float LogicalWidth = 400f;
        public const float LogicalHeight = 600f;

        public const float BirdStartX = 90f;
        public const float BirdStartY = LogicalHeight * 0.45f;
        public const float BirdRadius = 16f;
        public const float Gravity = 1600f;
        public const float FlapVelocity = -420f;
        public const float MaxFallSpeed = 700f;

        public const float PipeWidth = 58f;
        public const float PipeGap = 150f;
        public const float PipeSpeed = 170f;
        public const float PipeSpawnInterval = 1.45f;
        public const float PipeSpawnX = LogicalWidth + 40f;
        public const float PipeMinGapCenter = 120f;
        public const float PipeMaxGapCenter = LogicalHeight - 120f;

        public const float GroundY = LogicalHeight - 48f;
    }
}
