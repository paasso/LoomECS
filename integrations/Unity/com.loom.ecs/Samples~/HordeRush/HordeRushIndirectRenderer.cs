using Loom;
using UnityEngine;
using UnityEngine.Rendering;

namespace Loom.Unity.Samples.HordeRush
{
    /// <summary>
    /// Arena + units from textures via <see cref="Graphics.DrawMeshInstanced"/>
    /// (GPU instancing, Built-in and URP).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class HordeRushIndirectRenderer : MonoBehaviour
    {
        // Unity DrawMeshInstanced limit per call.
        private const int BatchSize = 1023;
        private const int MaxInstances = 16_384;
        private const string ResourcesRoot = "HordeRush/";

        private static readonly Color Bg = new Color(0.08f, 0.07f, 0.1f, 1f);
        private static readonly Color FlashTint = new Color(1.4f, 1.4f, 1.4f, 1f);

        [Header("Environment")]
        [SerializeField] private Texture2D? groundTexture;
        [SerializeField] private Texture2D? vignetteTexture;

        [Header("Units / weapons")]
        [SerializeField] private Texture2D? playerTexture;
        [SerializeField] private Texture2D? enemyTexture;
        [SerializeField] private Texture2D? bulletTexture;
        [SerializeField] private Texture2D? gunTexture;

        private Camera _camera = null!;
        private Mesh _quad = null!;
        private Material? _groundMat;
        private Material? _vignetteMat;
        private Material? _playerMat;
        private Material? _enemyMat;
        private Material? _bulletMat;
        private Material? _gunMat;
        private MaterialPropertyBlock _block = null!;
        private InstanceData[] _instances = new InstanceData[2048];
        private readonly InstanceData[] _gunScratch = new InstanceData[4];
        private readonly Matrix4x4[] _matrices = new Matrix4x4[BatchSize];
        private readonly Vector4[] _colors = new Vector4[BatchSize];
        private bool _ready;
        private GUIStyle? _hudStyle;
        private GUIStyle? _centerStyle;

        public World? World { get; set; }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            ResolveTextures();
            EnsureCamera();
            _quad = CreateUnitQuad();

            if (groundTexture == null || playerTexture == null || enemyTexture == null ||
                bulletTexture == null || gunTexture == null)
            {
                Debug.LogWarning(
                    "HordeRush: missing textures. Assign them on HordeRushIndirectRenderer, " +
                    "or import Resources/HordeRush from the sample.");
            }

            var shader = Shader.Find("Loom/HordeRushEnvironment")
                         ?? Shader.Find("Unlit/Transparent")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("HordeRush: no transparent unlit shader found.");
                enabled = false;
                return;
            }

            // One material per texture — DrawMesh* defers draws, so mutating a shared
            // material's _MainTex makes every batch render with the last texture.
            _groundMat = CreateTexturedMaterial(shader, groundTexture);
            _vignetteMat = CreateTexturedMaterial(shader, vignetteTexture);
            _playerMat = CreateTexturedMaterial(shader, playerTexture);
            _enemyMat = CreateTexturedMaterial(shader, enemyTexture);
            _bulletMat = CreateTexturedMaterial(shader, bulletTexture);
            _gunMat = CreateTexturedMaterial(shader, gunTexture);
            _ready = true;
        }

        private static Material? CreateTexturedMaterial(Shader shader, Texture2D? texture)
        {
            if (texture == null)
                return null;
            var mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            mat.enableInstancing = true;
            mat.SetTexture("_MainTex", texture);
            mat.SetVector("_MainTex_ST", new Vector4(1f, 1f, 0f, 0f));
            mat.SetColor("_Color", Color.white);
            return mat;
        }

        private void ResolveTextures()
        {
            groundTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "ground");
            vignetteTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "vignette");
            playerTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "player");
            enemyTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "enemy");
            bulletTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "bullet");
            gunTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "gun");
        }

        private void OnDestroy()
        {
            DestroyMat(_groundMat);
            DestroyMat(_vignetteMat);
            DestroyMat(_playerMat);
            DestroyMat(_enemyMat);
            DestroyMat(_bulletMat);
            DestroyMat(_gunMat);
            if (_quad != null) Destroy(_quad);
        }

        private static void DestroyMat(Material? mat)
        {
            if (mat != null) Destroy(mat);
        }

        private void LateUpdate()
        {
            if (!_ready || World == null)
                return;

            ref var arena = ref World.GetSingleton<ArenaConfig>();
            FitCamera(arena.Width, arena.Height);
            DrawEnvironment(arena.Width, arena.Height);

            EnsureCapacity(Mathf.Min(MaxInstances, World.EntityCount + 16));

            int count = 0;
            World.Query().With<Enemy>().Each<Position, Enemy>((Entity entity, ref Position pos, ref Enemy enemy) =>
            {
                if (count >= MaxInstances) return;
                Color tint = Color.white;
                if (World.Has<HitFlash>(entity))
                    tint = FlashTint;
                else if (World.Has<EnemyKind>(entity))
                {
                    // Soft tint — keep sprite readable (EnemyKind RGB is 0..1).
                    ref var kind = ref World.Get<EnemyKind>(entity);
                    tint = Color.Lerp(Color.white, new Color(kind.R, kind.G, kind.B, 1f), 0.45f);
                }

                float d = enemy.Radius * 2.2f;
                _instances[count++] = Make(pos.X, pos.Y, d, d, tint, 0f);
            });
            FlushInstanced(_enemyMat, count, z: 0.5f);

            count = 0;
            World.Query().With<Bullet>().Each<Position, Velocity, Bullet>(
                (Entity _, ref Position pos, ref Velocity vel, ref Bullet bullet) =>
                {
                    if (count >= MaxInstances) return;
                    float ang = Mathf.Atan2(vel.Y, vel.X);
                    float w = bullet.Radius * 5f;
                    float h = bullet.Radius * 2.5f;
                    _instances[count++] = Make(pos.X, pos.Y, w, h, Color.white, ang);
                });
            FlushInstanced(_bulletMat, count, z: 0.2f);

            count = 0;
            int gunCount = 0;
            World.Query().With<Player>().Each<Position, Player>(
                (Entity entity, ref Position pos, ref Player player) =>
                {
                    if (count >= MaxInstances) return;
                    float ang = Mathf.Atan2(player.AimY, player.AimX);
                    Color tint = World.Has<HitFlash>(entity) ? FlashTint : Color.white;
                    float d = GameConfig.PlayerRadius * 2.4f;
                    _instances[count++] = Make(pos.X, pos.Y, d, d, tint, ang);

                    float gx = pos.X + player.AimX * (GameConfig.PlayerRadius + 4f);
                    float gy = pos.Y + player.AimY * (GameConfig.PlayerRadius + 4f);
                    if (gunCount < _gunScratch.Length)
                        _gunScratch[gunCount++] = Make(gx, gy, 28f, 14f, Color.white, ang);
                });
            FlushInstanced(_playerMat, count, z: 0f);

            if (gunCount > 0)
            {
                for (int i = 0; i < gunCount; i++)
                    _instances[i] = _gunScratch[i];
                FlushInstanced(_gunMat, gunCount, z: -0.1f);
            }
        }

        private void OnGUI()
        {
            if (World == null)
                return;

            _hudStyle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };
            _centerStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
            };

            ref var session = ref World.GetSingleton<GameSession>();
            int hp = 0;
            World.Query().With<Player>().Each<Health>((Entity _, ref Health h) => hp = h.Current);

            GUI.Label(new Rect(16, 12, 640, 28),
                $"Score {session.Score}   Kills {session.Kills}   Wave {session.Wave}   HP {hp}   Entities {World.EntityCount}",
                _hudStyle);
            GUI.Label(new Rect(16, 36, 700, 24),
                $"[WASD] move  [Mouse] aim  [LMB/Space] fire  [P] parallel={(session.UseParallel ? "ON" : "OFF")}  [R] restart",
                _hudStyle);

            if (session.Phase == GamePhase.Dead)
            {
                GUI.Label(new Rect(0, Screen.height * 0.42f, Screen.width, 40), "YOU DIED", _centerStyle);
                GUI.Label(new Rect(0, Screen.height * 0.42f + 40, Screen.width, 32),
                    $"Score {session.Score} — R / Space / Click to retry", _hudStyle);
            }
        }

        private void DrawEnvironment(float width, float height)
        {
            float tile = width / 64f;
            DrawSingle(
                _groundMat,
                width * 0.5f, height * 0.5f, width, height,
                Color.white, tile, height / 64f, 0f, z: 5f, rotationRadians: 0f);
            DrawSingle(
                _vignetteMat,
                width * 0.5f, height * 0.5f, width, height,
                Color.white, 1f, 1f, 0f, z: 4.5f, rotationRadians: 0f);
        }

        private void FlushInstanced(Material? material, int count, float z)
        {
            if (count <= 0 || material == null)
                return;

            int offset = 0;
            while (offset < count)
            {
                int batch = Mathf.Min(BatchSize, count - offset);
                for (int i = 0; i < batch; i++)
                {
                    ref var inst = ref _instances[offset + i];
                    _matrices[i] = Matrix4x4.TRS(
                        new Vector3(inst.X, inst.Y, z),
                        Quaternion.Euler(0f, 0f, inst.Rotation * Mathf.Rad2Deg),
                        new Vector3(inst.W, inst.H, 1f));
                    _colors[i] = inst.Color;
                }

                _block.Clear();
                _block.SetVectorArray("_Color", _colors);
                Graphics.DrawMeshInstanced(
                    _quad, 0, material, _matrices, batch, _block,
                    ShadowCastingMode.Off, false, gameObject.layer, _camera);
                offset += batch;
            }
        }

        private void DrawSingle(
            Material? material,
            float x, float y, float w, float h,
            Color tint, float tileX, float tileY, float offsetX, float z, float rotationRadians)
        {
            if (material == null || w <= 0f || h <= 0f)
                return;

            var matrix = Matrix4x4.TRS(
                new Vector3(x, y, z),
                Quaternion.Euler(0f, 0f, rotationRadians * Mathf.Rad2Deg),
                new Vector3(w, h, 1f));

            _block.Clear();
            _block.SetColor("_Color", tint);
            _block.SetVector("_MainTex_ST", new Vector4(tileX, tileY, -offsetX, 0f));

            Graphics.DrawMesh(
                _quad, matrix, material, gameObject.layer, _camera, 0, _block,
                ShadowCastingMode.Off, false, null, false);
        }

        private static InstanceData Make(float x, float y, float w, float h, Color color, float rotation)
        {
            return new InstanceData
            {
                X = x,
                Y = y,
                W = w,
                H = h,
                Color = color,
                Rotation = rotation,
            };
        }

        private void EnsureCamera()
        {
            _camera = GetComponent<Camera>() ?? gameObject.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Bg;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100f;
            _camera.allowHDR = false;
            FitCamera(GameConfig.ArenaWidth, GameConfig.ArenaHeight);
        }

        private void FitCamera(float width, float height)
        {
            _camera.orthographicSize = height * 0.5f;
            transform.SetPositionAndRotation(
                new Vector3(width * 0.5f, height * 0.5f, -10f),
                Quaternion.identity);

            float target = width / height;
            float screen = (float)Screen.width / Mathf.Max(1, Screen.height);
            if (screen > target)
            {
                float w = target / screen;
                _camera.rect = new Rect((1f - w) * 0.5f, 0f, w, 1f);
            }
            else
            {
                float h = screen / target;
                _camera.rect = new Rect(0f, (1f - h) * 0.5f, 1f, h);
            }
        }

        private void EnsureCapacity(int needed)
        {
            if (needed <= _instances.Length)
                return;
            int n = _instances.Length;
            while (n < needed)
                n *= 2;
            _instances = new InstanceData[Mathf.Min(n, MaxInstances)];
        }

        private static Mesh CreateUnitQuad()
        {
            var mesh = new Mesh { name = "HordeRushUnitQuad" };
            mesh.vertices = new[]
            {
                new Vector3(-0.5f, -0.5f, 0f),
                new Vector3(0.5f, -0.5f, 0f),
                new Vector3(0.5f, 0.5f, 0f),
                new Vector3(-0.5f, 0.5f, 0f),
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(1f, 0f),
                new Vector2(1f, 1f),
                new Vector2(0f, 1f),
            };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            mesh.RecalculateBounds();
            return mesh;
        }

        private struct InstanceData
        {
            public float X, Y, W, H;
            public Color Color;
            public float Rotation;
        }
    }
}
