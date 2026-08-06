using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class JumpingMeasurements : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private WidgetManager widgetManager;
        public GenericValueEditor valueEditor;

        private SliderFieldWidgetData gravityStrengthSlider;
        private SliderFieldWidgetData jumpImpulseSlider;
        private LabelWidgetData infoLabel;

        private float jumpStartTime;
        private float lastAirTime;
        private float highestY;
        private bool wasGrounded = false;
        private VRCPlayerApi localPlayer;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
            valueEditor.Draw(new WidgetData[]
            {
                gravityStrengthSlider = (SliderFieldWidgetData)widgetManager
                    .NewSliderField("Gravity Strength", localPlayer.GetGravityStrength(), 0f, 10f)
                    .SetListener(this, nameof(OnGravityStrengthValueChanged)),
                jumpImpulseSlider = (SliderFieldWidgetData)widgetManager
                    .NewSliderField("Jump Impulse", localPlayer.GetJumpImpulse(), 0f, 10f)
                    .SetListener(this, nameof(OnJumpImpulseValueChanged)),
                widgetManager
                    .NewButton("Reset Highest Y")
                    .SetListener(this, nameof(OnResetHighestYClick)),
                infoLabel = widgetManager.NewLabel(""),
            });
            SendCustomEventDelayedFrames(nameof(DelayedStart), 1);
        }

        public void DelayedStart()
        {
            gravityStrengthSlider.SetValueWithoutNotify(localPlayer.GetGravityStrength());
            jumpImpulseSlider.SetValueWithoutNotify(localPlayer.GetJumpImpulse());
        }

        private void Update()
        {
            if (wasGrounded != localPlayer.IsPlayerGrounded())
            {
                if (wasGrounded)
                    jumpStartTime = Time.time;
                else
                    lastAirTime = Time.time - jumpStartTime;
                wasGrounded = !wasGrounded;
            }
            highestY = Mathf.Max(highestY, localPlayer.GetPosition().y);
            infoLabel.Label = $"Highest Y {highestY:f3}, Last Air Time: {lastAirTime:f3}";
        }

        public void OnGravityStrengthValueChanged()
        {
            localPlayer.SetGravityStrength(gravityStrengthSlider.Value);
        }

        public void OnJumpImpulseValueChanged()
        {
            localPlayer.SetJumpImpulse(jumpImpulseSlider.Value);
        }

        public void OnResetHighestYClick()
        {
            highestY = localPlayer.GetPosition().y;
        }
    }
}
