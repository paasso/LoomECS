using System.Runtime.InteropServices;
using Loom;
using UnityEngine;
using UnityEngine.Rendering;

namespace Loom.Unity.Samples.SpeedDemo
{
    /// <summary>Particles via <see cref="Graphics.DrawMeshInstancedIndirect"/> (circle quads).</summary>
    [DisallowMultipleComponent]
    public sealed class SpeedDemoIndirectRenderer : MonoBehaviour
    {
        private const int MaxInstances = 65_536;

        private static readonly Color Normal = new Color(80 / 255f, 220 / 255f, 200 / 255f, 180 / 255f);
        private static readonly Color Pulsed = new Color(1f, 180 / 255f, 70 / 255f, 220 / 255f);
        private static readonly Color Bg = new Color(12 / 255f, 16 / 255f, 22 / 255f, 1f);

        private Camera _camera = null!;
        private Mesh _quad = null!;
        private Material _material = null!;
        private ComputeBuffer? _instanceBuffer;
        private ComputeBuffer? _argsBuffer;
        private InstanceData[] _instances = new InstanceData[4096];
        private readonly uint[] _args = new uint[5];
        private Bounds _bounds;
        private bool _ready;
        private GUIStyle? _hudStyle;

        public World? World { get; set; }

        private void Awake()
        {
            EnsureCamera();
            _quad = CreateUnitQuad();
            var shader = Shader.Find("Loom/SpeedDemoInstanced");
            if (shader == null)
            {
                Debug.LogError("SpeedDemo: shader 'Loom/SpeedDemoInstanced' not found.");
                enabled = false;
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _instanceBuffer = new ComputeBuffer(MaxInstances, InstanceData.Stride);
            _argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            _args[0] = _quad.GetIndexCount(0);
            _args[1] = 0;
            _args[2] = _quad.GetIndexStart(0);
            _args[3] = _quad.GetBaseVertex(0);
            _args[4] = 0;
            _argsBuffer.SetData(_args);
            _ready = true;
        }

        private void OnDestroy()
        {
            _instanceBuffer?.Release();
            _argsBuffer?.Release();
            if (_material != null) Destroy(_material);
            if (_quad != null) Destroy(_quad);
        }

        private void LateUpdate()
        {
            if (!_ready || World == null || _instanceBuffer == null || _argsBuffer == null)
                return;

            ref var cfg = ref World.GetSingleton<DemoConfig>();
            if (!cfg.Draw)
                return;

            FitCamera(cfg.Width, cfg.Height);
            int budget = Mathf.Clamp(cfg.DrawBudget, 0, MaxInstances);
            int count = World.EntityCount;
            if (budget <= 0 || count <= 0)
                return;

            int stride = count <= budget ? 1 : (count + budget - 1) / budget;
            int drawn = 0;
            int index = 0;
            EnsureInstanceCapacity(Mathf.Min(budget, count));

            World.Query().Each<Position>((Entity entity, ref Position pos) =>
            {
                if ((index++ % stride) != 0 || drawn >= budget)
                    return;

                bool pulse = World.Has<Pulse>(entity);
                float diameter = pulse ? 4.8f : 3.2f;
                // Game Y-down → Unity Y-up.
                float uy = cfg.Height - pos.Y;
                _instances[drawn++] = new InstanceData
                {
                    PosSize = new Vector4(pos.X, uy, diameter, diameter),
                    Color = pulse ? Pulsed : Normal,
                    Misc = Vector4.zero,
                };
            });

            if (drawn == 0)
                return;

            _instanceBuffer.SetData(_instances, 0, 0, drawn);
            _material.SetBuffer("_Instances", _instanceBuffer);
            _args[1] = (uint)drawn;
            _argsBuffer.SetData(_args);

            Graphics.DrawMeshInstancedIndirect(
                _quad, 0, _material, _bounds, _argsBuffer, 0, null,
                ShadowCastingMode.Off, false, gameObject.layer, _camera);
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
            GUI.Label(new Rect(16, 12, 520, 200), DemoBootstrap.FormatHud(World), _hudStyle);
            GUI.Label(new Rect(16, Screen.height - 28, 700, 24),
                "[Space] pause  [+/-] spawn  [P] parallel  [C] sparse  [D] draw  [R] reset",
                _hudStyle);
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
        }

        private void FitCamera(float width, float height)
        {
            _camera.orthographicSize = height * 0.5f;
            transform.SetPositionAndRotation(new Vector3(width * 0.5f, height * 0.5f, -10f), Quaternion.identity);
            _bounds = new Bounds(new Vector3(width * 0.5f, height * 0.5f, 0f), new Vector3(width + 40f, height + 40f, 4f));

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

        private void EnsureInstanceCapacity(int needed)
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
            var mesh = new Mesh { name = "SpeedDemoUnitQuad" };
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

        [StructLayout(LayoutKind.Sequential)]
        private struct InstanceData
        {
            public Vector4 PosSize;
            public Vector4 Color;
            public Vector4 Misc;
            public const int Stride = sizeof(float) * 12;
        }
    }
}
