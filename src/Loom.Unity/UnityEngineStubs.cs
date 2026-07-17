#if LOOM_UNITY_STUBS
// Minimal UnityEngine surface so Loom.Unity compiles outside the Unity Editor (CI / solution build).

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; } = string.Empty;
    }

    public class Transform : Object
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; } = Quaternion.identity;
    }

    public class Component : Object
    {
        private Transform? _transform;
        public Transform transform
        {
            get => _transform ??= new Transform();
            set => _transform = value;
        }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
    }

    /// <summary>Unity invokes Awake/Update/OnDestroy by name — they are not virtual in the real engine.</summary>
    public class MonoBehaviour : Behaviour
    {
    }

    public struct Vector3
    {
        public float x, y, z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public struct Quaternion
    {
        public float x, y, z, w;

        public static readonly Quaternion identity = new Quaternion { x = 0, y = 0, z = 0, w = 1 };
    }

    public static class Time
    {
        public static float deltaTime { get; set; } = 0.016f;
    }
}
#endif
