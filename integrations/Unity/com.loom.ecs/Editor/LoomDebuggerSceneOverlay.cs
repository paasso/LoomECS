using Loom;
using Loom.Entities;
using UnityEditor;
using UnityEngine;

namespace Loom.Unity.Editor
{
    /// <summary>Draws a Scene-view marker for the entity selected in <see cref="LoomEntityDebuggerWindow"/>.</summary>
    [InitializeOnLoad]
    internal static class LoomDebuggerSceneOverlay
    {
        private static Entity _selected;
        private static LoomRunner? _runner;

        static LoomDebuggerSceneOverlay()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        internal static void SetSelection(LoomRunner? runner, Entity entity)
        {
            _runner = runner;
            _selected = entity;
            SceneView.RepaintAll();
        }

        private static void OnSceneGui(SceneView view)
        {
            if (!Application.isPlaying || _runner == null || _runner.World == null)
                return;
            if (_selected.IsNull || !_runner.World.IsAlive(_selected))
                return;

            Vector3 position;
            if (_runner.World.Has<UnityPosition>(_selected))
            {
                ref var pos = ref _runner.World.Get<UnityPosition>(_selected);
                position = new Vector3(pos.X, pos.Y, pos.Z);
            }
            else
            {
                var behaviours = Object.FindObjectsOfType<EntityBehaviour>();
                EntityBehaviour? match = null;
                for (int i = 0; i < behaviours.Length; i++)
                {
                    var b = behaviours[i];
                    if (b != null && b.IsBound && b.Entity == _selected && b.Runner == _runner)
                    {
                        match = b;
                        break;
                    }
                }

                if (match == null)
                    return;
                position = match.transform.position;
            }

            Handles.color = new Color(0.2f, 0.85f, 0.55f, 0.9f);
            Handles.DrawWireDisc(position, Vector3.up, 0.35f);
            Handles.DrawWireDisc(position, Vector3.forward, 0.35f);
            Handles.Label(position + Vector3.up * 0.4f, $"E{_selected.Id}:{_selected.Version}");
        }
    }
}
