using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("2de4fe20374742decb465a3d8fd49063")] // Runtime/Dev/RemoteSmoothingUI.prefab
    public class RemoteSmoothingUI : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private WidgetManager widgetManager;
        public GenericValueEditor valueEditor;

        public float InterpolationDuration = 0.15f;
        public float DesiredVelocityMultiplier = 0.4f;
        public float SyncLoopInterval = 0.3f;
        public float VelocityDiffCutoff = 0.0f;
        public float AngularVelocityDiffCutoff = 0.0f;

        private void Start()
        {
            string[] fieldNames = new string[]
            {
                nameof(InterpolationDuration),
                nameof(DesiredVelocityMultiplier),
                nameof(SyncLoopInterval),
                nameof(VelocityDiffCutoff),
                nameof(AngularVelocityDiffCutoff),
            };
            WidgetData[] widgets = new WidgetData[fieldNames.Length];
            for (int i = 0; i < fieldNames.Length; i++)
                widgets[i] = widgetManager.NewFloatField(fieldNames[i], (float)GetProgramVariable(fieldNames[i]))
                    .SetCustomData(nameof(changedFieldName), fieldNames[i])
                    .SetListener(this, nameof(OnValueChanged))
                    .StdMoveWidget();
            valueEditor.Draw(widgets);
        }

        private string changedFieldName;
        public void OnValueChanged()
        {
            SetProgramVariable(changedFieldName, valueEditor.GetSendingDecimalField().FloatValue);
        }
    }
}
