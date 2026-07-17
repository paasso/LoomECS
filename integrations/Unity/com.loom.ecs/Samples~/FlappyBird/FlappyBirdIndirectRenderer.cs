using Loom;
using UnityEngine;
using UnityEngine.Rendering;

namespace Loom.Unity.Samples.FlappyBird
{
    /// <summary>
    /// Flappy Bird view from project textures (sky, hills, clouds, ground, bird, pipes).
    /// Logical game space is Y-down; draws are converted to Unity Y-up for an orthographic camera.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FlappyBirdIndirectRenderer : MonoBehaviour
    {
        private const string ResourcesRoot = "FlappyBird/";
        private const float PipeCapHeight = 18f;
        private const float PipeCapOverhang = 4f;

        private static readonly Color Letterbox = new Color32(12, 16, 22, 255);

        [Header("Environment")]
        [SerializeField] private Texture2D? skyTexture;
        [SerializeField] private Texture2D? hillsTexture;
        [SerializeField] private Texture2D? cloudTexture;
        [SerializeField] private Texture2D? groundTexture;

        [Header("Gameplay")]
        [SerializeField] private Texture2D? birdTexture;
        [SerializeField] private Texture2D? pipeBodyTexture;
        [SerializeField] private Texture2D? pipeCapTexture;
        [SerializeField] private Texture2D? whiteTexture;

        private Camera _camera = null!;
        private Camera _letterboxCamera = null!;
        private Mesh _quad = null!;
        private Material _material = null!;
        private MaterialPropertyBlock _block;
        private bool _ready;
        private float _scroll;
        private GUIStyle? _scoreStyle;
        private GUIStyle? _centerStyle;

        public World? World { get; set; }

        private void Awake()
        {
            ResolveTextures();
            EnsureCameras();
            _quad = CreateUnitQuad();

            var shader = Shader.Find("Loom/FlappyBirdEnvironment")
                         ?? Shader.Find("Unlit/Transparent")
                         ?? Shader.Find("Sprites/Default");
            if (shader == null)
            {
                Debug.LogError("FlappyBird: no transparent unlit shader found for textures.");
                enabled = false;
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.color = Color.white;
            _ready = true;
        }

        private void OnDestroy()
        {
            if (_material != null)
                Destroy(_material);
            if (_quad != null)
                Destroy(_quad);
            if (_letterboxCamera != null)
                Destroy(_letterboxCamera.gameObject);
        }

        private void LateUpdate()
        {
            if (!_ready || World == null)
                return;

            UpdateCameraLetterbox();

            ref var session = ref World.GetSingleton<GameSession>();
            if (session.Phase == GamePhase.Playing)
                _scroll += GameConfig.PipeSpeed * Time.deltaTime;
            else if (session.Phase == GamePhase.Ready)
                _scroll += GameConfig.PipeSpeed * 0.35f * Time.deltaTime;

            DrawEnvironment();
            DrawPipes(World);
            DrawBird(World);

            if (session.Phase == GamePhase.Dead)
            {
                DrawTextured(
                    whiteTexture,
                    GameConfig.LogicalWidth * 0.5f,
                    GameConfig.LogicalHeight * 0.5f,
                    GameConfig.LogicalWidth,
                    GameConfig.LogicalHeight,
                    new Color(0f, 0f, 0f, 0.4f),
                    1f, 1f, 0f, z: -1f);
            }
        }

        private void OnGUI()
        {
            if (World == null)
                return;
            DrawHud(World);
        }

        private void ResolveTextures()
        {
            _block = new MaterialPropertyBlock();
            skyTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "sky");
            hillsTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "hills");
            cloudTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "cloud");
            groundTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "ground");
            birdTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "bird");
            pipeBodyTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "pipe_body");
            pipeCapTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "pipe_cap");
            whiteTexture ??= Resources.Load<Texture2D>(ResourcesRoot + "white");

            if (skyTexture == null || birdTexture == null || pipeBodyTexture == null || pipeCapTexture == null)
            {
                Debug.LogWarning(
                    "FlappyBird: missing textures. Assign them on FlappyBirdIndirectRenderer " +
                    "or import the sample so Resources/FlappyBird/*.png are available.");
            }
        }

        private void EnsureCameras()
        {
            _camera = GetComponent<Camera>();
            if (_camera == null)
                _camera = gameObject.AddComponent<Camera>();

            _camera.orthographic = true;
            _camera.orthographicSize = GameConfig.LogicalHeight * 0.5f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 100f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = skyTexture != null
                ? new Color32(135, 206, 235, 255)
                : Letterbox;
            _camera.depth = 0;
            _camera.allowHDR = false;
            _camera.allowMSAA = true;
            transform.SetPositionAndRotation(
                new Vector3(GameConfig.LogicalWidth * 0.5f, GameConfig.LogicalHeight * 0.5f, -10f),
                Quaternion.identity);

            var letterboxGo = new GameObject("FlappyBirdLetterboxCamera");
            letterboxGo.transform.SetParent(transform, false);
            _letterboxCamera = letterboxGo.AddComponent<Camera>();
            _letterboxCamera.orthographic = true;
            _letterboxCamera.orthographicSize = 1f;
            _letterboxCamera.nearClipPlane = 0.1f;
            _letterboxCamera.farClipPlane = 10f;
            _letterboxCamera.clearFlags = CameraClearFlags.SolidColor;
            _letterboxCamera.backgroundColor = Letterbox;
            _letterboxCamera.cullingMask = 0;
            _letterboxCamera.depth = _camera.depth - 1;
            _letterboxCamera.rect = new Rect(0f, 0f, 1f, 1f);
            _letterboxCamera.allowHDR = false;
            _letterboxCamera.allowMSAA = false;
        }

        private void UpdateCameraLetterbox()
        {
            float target = GameConfig.LogicalWidth / GameConfig.LogicalHeight;
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

        private void DrawEnvironment()
        {
            float groundH = GameConfig.LogicalHeight - GameConfig.GroundY;

            DrawTextured(
                skyTexture,
                GameConfig.LogicalWidth * 0.5f,
                GameConfig.LogicalHeight * 0.5f,
                GameConfig.LogicalWidth,
                GameConfig.LogicalHeight,
                Color.white, 1f, 1f, 0f, z: 5f);

            float hillsH = 72f;
            float hillsTile = GameConfig.LogicalWidth / 220f;
            DrawTextured(
                hillsTexture,
                GameConfig.LogicalWidth * 0.5f,
                GameConfig.GroundY - hillsH * 0.5f,
                GameConfig.LogicalWidth,
                hillsH,
                new Color(1f, 1f, 1f, 0.95f),
                hillsTile, 1f, _scroll * 0.15f / 220f, z: 4f);

            DrawCloud(40f + Mathf.Sin(Time.time * 0.2f) * 10f, 70f, 1f);
            DrawCloud(220f + Mathf.Cos(Time.time * 0.15f) * 12f, 120f, 0.85f);
            DrawCloud(320f, 50f, 0.7f);

            float groundTile = GameConfig.LogicalWidth / 64f;
            DrawTextured(
                groundTexture,
                GameConfig.LogicalWidth * 0.5f,
                GameConfig.GroundY + groundH * 0.5f,
                GameConfig.LogicalWidth,
                groundH,
                Color.white, groundTile, 1f, _scroll / 64f, z: 1f);
        }

        private void DrawCloud(float gameX, float gameY, float scale)
        {
            DrawTextured(
                cloudTexture, gameX, gameY, 96f * scale, 48f * scale,
                new Color(1f, 1f, 1f, 0.92f), 1f, 1f, 0f, z: 3f);
        }

        private void DrawPipes(World world)
        {
            world.Query().With<Pipe>().Each<Position, Pipe>((Entity _, ref Position pos, ref Pipe pipe) =>
            {
                float left = pos.X;
                float width = pipe.Width;
                float gapTop = pipe.GapCenterY - pipe.GapSize * 0.5f;
                float gapBottom = pipe.GapCenterY + pipe.GapSize * 0.5f;
                float capW = width + PipeCapOverhang * 2f;

                // Top column + lip at the gap.
                float topH = gapTop;
                if (topH > 0f)
                {
                    float tileY = topH / 64f;
                    DrawTextured(
                        pipeBodyTexture,
                        left + width * 0.5f,
                        topH * 0.5f,
                        width,
                        topH,
                        Color.white, 1f, tileY, 0f, z: 0.5f);
                }

                DrawTextured(
                    pipeCapTexture,
                    left + width * 0.5f,
                    gapTop - PipeCapHeight * 0.5f,
                    capW,
                    PipeCapHeight,
                    Color.white, 1f, 1f, 0f, z: 0.4f);

                // Bottom column + lip at the gap.
                float bottomH = GameConfig.GroundY - gapBottom;
                if (bottomH > 0f)
                {
                    float tileY = bottomH / 64f;
                    DrawTextured(
                        pipeBodyTexture,
                        left + width * 0.5f,
                        gapBottom + bottomH * 0.5f,
                        width,
                        bottomH,
                        Color.white, 1f, tileY, 0f, z: 0.5f);
                }

                DrawTextured(
                    pipeCapTexture,
                    left + width * 0.5f,
                    gapBottom + PipeCapHeight * 0.5f,
                    capW,
                    PipeCapHeight,
                    Color.white, 1f, 1f, 0f, z: 0.4f);
            });
        }

        private void DrawBird(World world)
        {
            world.Query().With<Bird>().Each<Bird, Position, Velocity>(
                (Entity _, ref Bird bird, ref Position pos, ref Velocity vel) =>
                {
                    float size = bird.Radius * 2.4f;
                    // Game Y-down: rising (neg vel) → nose up (positive Unity Z rotation).
                    float angle = Mathf.Clamp(-vel.Y / 500f, -0.9f, 0.7f);

                    // Soft shadow under the bird.
                    DrawTextured(
                        birdTexture,
                        pos.X + 2f,
                        pos.Y + 3f,
                        size,
                        size,
                        new Color(0f, 0f, 0f, 0.3f),
                        1f, 1f, 0f, z: 0.2f, rotationRadians: angle);

                    DrawTextured(
                        birdTexture,
                        pos.X,
                        pos.Y,
                        size,
                        size,
                        Color.white,
                        1f, 1f, 0f, z: 0f, rotationRadians: angle);
                });
        }

        /// <summary>
        /// Draws a textured quad. <paramref name="gameX"/>/<paramref name="gameY"/> are center in Y-down space.
        /// <paramref name="rotationRadians"/> is applied in Unity Y-up space (positive = counter-clockwise).
        /// </summary>
        private void DrawTextured(
            Texture2D? texture,
            float gameX,
            float gameY,
            float width,
            float height,
            Color tint,
            float tilingX,
            float tilingY,
            float offsetX,
            float z,
            float rotationRadians = 0f)
        {
            if (texture == null || width <= 0f || height <= 0f)
                return;

            float ux = gameX;
            float uy = GameConfig.LogicalHeight - gameY;
            var matrix = Matrix4x4.TRS(
                new Vector3(ux, uy, z),
                Quaternion.Euler(0f, 0f, rotationRadians * Mathf.Rad2Deg),
                new Vector3(width, height, 1f));

            _block.Clear();
            _block.SetTexture("_MainTex", texture);
            _block.SetColor("_Color", tint);
            _block.SetVector("_MainTex_ST", new Vector4(tilingX, tilingY, -offsetX, 0f));

            Graphics.DrawMesh(
                _quad,
                matrix,
                _material,
                gameObject.layer,
                _camera,
                0,
                _block,
                ShadowCastingMode.Off,
                false,
                null,
                false);
        }

        private void DrawHud(World world)
        {
            EnsureHudStyles();

            float scale = Mathf.Min(
                Screen.width / GameConfig.LogicalWidth,
                Screen.height / GameConfig.LogicalHeight);
            float viewW = GameConfig.LogicalWidth * scale;
            float viewH = GameConfig.LogicalHeight * scale;
            float originX = (Screen.width - viewW) * 0.5f;
            float originY = (Screen.height - viewH) * 0.5f;

            var prev = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(
                new Vector3(originX, originY, 0f),
                Quaternion.identity,
                new Vector3(scale, scale, 1f));

            ref var session = ref world.GetSingleton<GameSession>();
            GUI.Label(new Rect(0f, 36f, GameConfig.LogicalWidth, 56f), session.Score.ToString(), _scoreStyle);

            if (session.Phase == GamePhase.Ready)
            {
                DrawCentered("SPACE / CLICK to flap", GameConfig.LogicalHeight * 0.5f - 10f, 22, new Color(0.1f, 0.2f, 0.45f));
                DrawCentered("Loom demo", GameConfig.LogicalHeight * 0.5f + 24f, 18, new Color(0.15f, 0.35f, 0.75f));
            }
            else if (session.Phase == GamePhase.Dead)
            {
                DrawCentered("GAME OVER", GameConfig.LogicalHeight * 0.5f - 40f, 36, Color.white);
                DrawCentered($"Best {session.Best}", GameConfig.LogicalHeight * 0.5f + 4f, 22, Color.white);
                DrawCentered("R or SPACE to retry", GameConfig.LogicalHeight * 0.5f + 40f, 20, new Color(0.85f, 0.85f, 0.85f));
            }

            GUI.matrix = prev;
        }

        private void DrawCentered(string text, float y, int size, Color color)
        {
            _centerStyle!.fontSize = size;
            _centerStyle.normal.textColor = color;
            GUI.Label(new Rect(0f, y, GameConfig.LogicalWidth, size + 8f), text, _centerStyle);
        }

        private void EnsureHudStyles()
        {
            if (_scoreStyle == null)
            {
                _scoreStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontSize = 48,
                    fontStyle = FontStyle.Bold,
                };
                _scoreStyle.normal.textColor = Color.white;
            }

            if (_centerStyle == null)
            {
                _centerStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontStyle = FontStyle.Bold,
                };
            }
        }

        private static Mesh CreateUnitQuad()
        {
            var mesh = new Mesh { name = "FlappyBirdUnitQuad" };
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
    }
}
