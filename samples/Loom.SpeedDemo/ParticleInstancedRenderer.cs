using System.Numerics;
using Loom;
using Raylib_cs;

namespace Loom.SpeedDemo;

/// <summary>
/// GPU-instanced particle draw via Raylib <see cref="Raylib.DrawMeshInstanced"/>.
/// One unit disc mesh, two transform batches (normal / pulse), orthographic screen space.
/// </summary>
sealed class ParticleInstancedRenderer : IDisposable
{
    private const int InitialCapacity = 16_384;

    private Mesh _mesh;
    private Shader _shader;
    private Material _material;
    private int _colDiffuseLoc;
    private Camera3D _camera;
    private Matrix4x4[] _normalTransforms = new Matrix4x4[InitialCapacity];
    private Matrix4x4[] _pulseTransforms = new Matrix4x4[InitialCapacity];
    private bool _ready;

    private static readonly string VertexShader = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec4 vertexColor;
        in mat4 instanceTransform;
        uniform mat4 mvp;
        out vec2 fragTexCoord;
        out vec4 fragColor;
        void main()
        {
            fragTexCoord = vertexTexCoord;
            fragColor = vertexColor;
            gl_Position = mvp * instanceTransform * vec4(vertexPosition, 1.0);
        }
        """;

    private static readonly string FragmentShader = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        out vec4 finalColor;
        void main()
        {
            vec4 texel = texture(texture0, fragTexCoord);
            finalColor = texel * colDiffuse * fragColor;
        }
        """;

    public void Load(int screenWidth, int screenHeight)
    {
        // Unit disc; instance scale sets pixel radius.
        _mesh = Raylib.GenMeshPoly(16, 1f);
        Raylib.UploadMesh(ref _mesh, false);

        _shader = Raylib.LoadShaderFromMemory(VertexShader, FragmentShader);
        unsafe
        {
            _shader.Locs[(int)ShaderLocationIndex.MatrixMvp] =
                Raylib.GetShaderLocation(_shader, "mvp");
            _shader.Locs[(int)ShaderLocationIndex.MatrixModel] =
                Raylib.GetShaderLocationAttrib(_shader, "instanceTransform");
        }

        _colDiffuseLoc = Raylib.GetShaderLocation(_shader, "colDiffuse");
        _material = Raylib.LoadMaterialDefault();
        _material.Shader = _shader;
        unsafe
        {
            _material.Maps[(int)MaterialMapIndex.Albedo].Color = Color.White;
        }

        _camera = new Camera3D
        {
            Position = new Vector3(screenWidth * 0.5f, screenHeight * 0.5f, 100f),
            Target = new Vector3(screenWidth * 0.5f, screenHeight * 0.5f, 0f),
            // Y-down so world X/Y match Raylib 2D screen space.
            Up = new Vector3(0f, -1f, 0f),
            FovY = screenHeight,
            Projection = CameraProjection.Orthographic,
        };

        _ready = true;
    }

    public void Draw(World world, Color normal, Color pulsed)
    {
        if (!_ready)
            return;

        ref var cfg = ref world.GetSingleton<DemoConfig>();
        int budget = cfg.DrawBudget;
        int count = world.EntityCount;
        if (budget <= 0 || count <= 0)
            return;

        int stride = count <= budget ? 1 : (count + budget - 1) / budget;
        int normalCount = 0;
        int pulseCount = 0;
        int index = 0;

        world.Query().Each<Position>((Entity entity, ref Position pos) =>
        {
            if ((index++ % stride) != 0)
                return;

            if (normalCount + pulseCount >= budget)
                return;

            bool isPulse = world.Has<Pulse>(entity);
            float radius = isPulse ? 2.4f : 1.6f;
            var transform = Raymath.MatrixMultiply(
                Raymath.MatrixScale(radius, radius, 1f),
                Raymath.MatrixTranslate(pos.X, pos.Y, 0f));

            if (isPulse)
            {
                EnsureCapacity(ref _pulseTransforms, pulseCount + 1);
                _pulseTransforms[pulseCount++] = transform;
            }
            else
            {
                EnsureCapacity(ref _normalTransforms, normalCount + 1);
                _normalTransforms[normalCount++] = transform;
            }
        });

        Raylib.BeginMode3D(_camera);
        Rlgl.DisableDepthTest();

        if (normalCount > 0)
        {
            SetDiffuse(normal);
            Raylib.DrawMeshInstanced(_mesh, _material, _normalTransforms, normalCount);
        }

        if (pulseCount > 0)
        {
            SetDiffuse(pulsed);
            Raylib.DrawMeshInstanced(_mesh, _material, _pulseTransforms, pulseCount);
        }

        Rlgl.EnableDepthTest();
        Raylib.EndMode3D();
    }

    private void SetDiffuse(Color color)
    {
        var rgba = new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        Raylib.SetShaderValue(_shader, _colDiffuseLoc, rgba, ShaderUniformDataType.Vec4);
    }

    private static void EnsureCapacity(ref Matrix4x4[] buffer, int needed)
    {
        if (needed <= buffer.Length)
            return;
        int next = buffer.Length;
        while (next < needed)
            next *= 2;
        Array.Resize(ref buffer, next);
    }

    public void Dispose()
    {
        if (!_ready)
            return;
        _ready = false;
        Raylib.UnloadMaterial(_material);
        Raylib.UnloadShader(_shader);
        Raylib.UnloadMesh(_mesh);
    }
}
