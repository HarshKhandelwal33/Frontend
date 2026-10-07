using UnityEditor;
using UnityEngine;

namespace GearboxDemo.Editor
{
    [CustomEditor(typeof(GearboxGameSimulation))]
    public sealed class GearboxCollaborationInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var game = (GearboxGameSimulation)target;
            if (!Application.isPlaying || !game.Ready) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live collaboration", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Phase", game.Phase);
                EditorGUILayout.Toggle("Protective stop", game.ProtectiveStopped);
                EditorGUILayout.Toggle("Emergency stop", game.EmergencyStopped);
                EditorGUILayout.FloatField("Body clearance (m)", game.Separation);
                EditorGUILayout.ObjectField("Payload", game.Payload, typeof(Transform), true);
                EditorGUILayout.ObjectField("Owner", game.Holder, typeof(Transform), true);
            }
            if (!string.IsNullOrEmpty(game.SafetyReason)) EditorGUILayout.HelpBox(game.SafetyReason, MessageType.Info);
            if (GUILayout.Button("Emergency Stop")) game.EmergencyStop();
            if (GUILayout.Button("Cancel / Protective Stop")) game.CancelOperation();
            if (GUILayout.Button("Confirm Worker Grasp (simulation event)")) game.ConfirmWorkerGrasp();
            Repaint();
        }
    }
}
