using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class JumpingMeasurements : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private WidgetManager widgetManager;
        public GenericValueEditor valueEditor;

        private SliderFieldWidgetData gravityStrengthSlider;
        private SliderFieldWidgetData jumpImpulseSlider;
        private LabelWidgetData jumpInfoLabel;
        private ToggleFieldWidgetData useDeltaTimeForLookToggle;
        private LabelWidgetData lookInfoLabel;

        private float inputLookHorizontal = 0f;
        private float accumulatedLookHorizontalValue = 0f;
        private float accumulatedLookHorizontalValueMin = 0f;
        private float accumulatedLookHorizontalValueMax = 0f;

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
                jumpInfoLabel = widgetManager.NewLabel(""),

                widgetManager.NewSpace(),

                useDeltaTimeForLookToggle = widgetManager
                    .NewLeftToggleField("Multiply Look Values With Delta Time", true),
                widgetManager
                    .NewButton("Reset Look Values")
                    .SetListener(this, nameof(OnResetLookValuesClick)),
                lookInfoLabel = widgetManager.NewLabel(""),
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
            UpdateJumpTesting();
            UpdateLookTesting();
        }

        private void UpdateJumpTesting()
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
            jumpInfoLabel.Label = $"Highest Y {highestY:f3}, Last Air Time: {lastAirTime:f3}";
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

        public void OnResetLookValuesClick()
        {
            accumulatedLookHorizontalValue = 0f;
            accumulatedLookHorizontalValueMin = 0f;
            accumulatedLookHorizontalValueMax = 0f;
        }

        public override void InputLookHorizontal(float value, UdonInputEventArgs args)
        {
            inputLookHorizontal = value;
        }

        private void UpdateLookTesting()
        {
            if (useDeltaTimeForLookToggle.Value)
                accumulatedLookHorizontalValue += inputLookHorizontal * Time.deltaTime;
            else
                accumulatedLookHorizontalValue += inputLookHorizontal;
            accumulatedLookHorizontalValueMin = Mathf.Min(accumulatedLookHorizontalValueMin, accumulatedLookHorizontalValue);
            accumulatedLookHorizontalValueMax = Mathf.Max(accumulatedLookHorizontalValueMax, accumulatedLookHorizontalValue);
            lookInfoLabel.Label = $"Horizontal Look:\n"
                + $"Current Raw: {inputLookHorizontal}\n"
                + $"Accumulative: {accumulatedLookHorizontalValue}\n"
                + $"Min: {accumulatedLookHorizontalValueMin}\n"
                + $"Max: {accumulatedLookHorizontalValueMax}";

            // Measurement results:
            // In VR, with delta time, 360 rotation == ~1.65
            // In Desktop, without delta time, 360 rotation == ~180
            // Except no it isn't, desktop is so random, whether delta time is used or not, moving the mouse
            // quickly in one direction and slowly back to its original position, VRChat's view direction is
            // also back to its original, however the current value as tracked by the script here is not 0.
            // Moving slowly makes it count much higher, quick movement results in a low difference.
            // It's just nonsense.
        }
    }
}
