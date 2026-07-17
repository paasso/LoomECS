using Loom;
using UnityEditor;
using UnityEngine;

namespace Loom.Unity.Editor
{
    [CustomEditor(typeof(EntityBehaviour))]
    public sealed class EntityBehaviourEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var behaviour = (EntityBehaviour)target;
            EditorGUILayout.Space(4);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Bind an entity at runtime via EntityBehaviour.Bind.", MessageType.Info);
                return;
            }

            if (!behaviour.IsBound)
            {
                EditorGUILayout.HelpBox("Not bound to a live entity.", MessageType.Warning);
                return;
            }

            var entity = behaviour.Entity;
            EditorGUILayout.LabelField("Entity", $"{entity.Id}:{entity.Version}");

            using (new EditorGUI.DisabledScope(behaviour.Runner == null))
            {
                if (GUILayout.Button("Open in Loom Entity Debugger"))
                    LoomEntityDebuggerWindow.Open(behaviour.Runner!, entity);
            }
        }
    }
}
