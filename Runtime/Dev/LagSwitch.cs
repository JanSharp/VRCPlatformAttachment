using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LagSwitch : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private WidgetManager widgetManager;
        public GenericValueEditor valueEditor;

        private ToggleFieldWidgetData toggleWidget;
        private SliderFieldWidgetData fpsSliderWidget;
        private SliderFieldWidgetData lagSpikeSliderWidget;
        private System.Diagnostics.Stopwatch stopwatch = new System.Diagnostics.Stopwatch();

        public void Start()
        {
            valueEditor.Draw(new WidgetData[]
            {
                toggleWidget = widgetManager.NewToggleField("Lag Switch", false),
                fpsSliderWidget = widgetManager.NewSliderField("FPS", 30f, 1f, 120f),
                widgetManager.NewSpace().StdMoveWidget(),
                widgetManager.NewButton("Lag Spike")
                    .SetListener(this, nameof(OnLagSpikeClick))
                    .StdMoveWidget(),
                lagSpikeSliderWidget = widgetManager.NewSliderField("Lag Spike Seconds", 5f, 0f, 9f),
            });
        }

        public void Update()
        {
            if (!toggleWidget.Value)
                return;
            LagSpike(1d / fpsSliderWidget.Value);
        }

        public void OnLagSpikeClick()
        {
            LagSpike(lagSpikeSliderWidget.Value);
        }

        private void LagSpike(double seconds)
        {
            stopwatch.Reset();
            stopwatch.Start();
            while (stopwatch.Elapsed.TotalSeconds < seconds)
                ; // Busy wait.
        }
    }
}
