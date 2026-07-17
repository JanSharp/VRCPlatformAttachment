using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StationFullBodyTest : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private WidgetManager widgetManager;
        public GenericValueEditor valueEditor;

        public VRCStation station;
        public Transform stationPlayerPosition;

        private ToggleFieldWidgetData matchPlayerPositionToggle;
        private ToggleFieldWidgetData setEnterVelocityToggle;
        private ToggleFieldWidgetData setEnterImmobilizeToggle;
        private ToggleFieldWidgetData moveAfterEnteringToggle;
        private ToggleFieldWidgetData setVelocityWhileInStationToggle;

        private Vector3FieldWidgetData enterPlayerPositionOffsetField;
        private Vector3FieldWidgetData enterVelocityField;
        private ToggleFieldWidgetData enterImmobilizeField;
        private Vector3FieldWidgetData movementVelocityAfterEnteringField;
        private IntegerFieldWidgetData movementFramesAfterEnteringField;
        private Vector3FieldWidgetData velocityWhileInStationField;

        private LabelWidgetData infoLabel;
        private int movementFramesRemaining = 0;

        private VRCPlayerApi localPlayer;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;

            valueEditor.Draw(new WidgetData[]
            {
                widgetManager
                    .NewButton($"Set Mobility To {nameof(VRCStation.Mobility.Mobile)}")
                    .SetListener(this, nameof(OnSetMobilityToMobileClick))
                    .StdMoveWidget(),
                widgetManager
                    .NewButton($"Set Mobility To {nameof(VRCStation.Mobility.Immobilize)}")
                    .SetListener(this, nameof(OnSetImmobilizeToImmobilizeClick))
                    .StdMoveWidget(),
                widgetManager
                    .NewButton($"Set Mobility To {nameof(VRCStation.Mobility.ImmobilizeForVehicle)}")
                    .SetListener(this, nameof(OnSetMobilityToImmobilizeForVehicleClick))
                    .StdMoveWidget(),

                widgetManager
                    .NewToggleField("Disable Station Exit", station.disableStationExit)
                    .SetListener(this, nameof(OnDisableStationExitValueChanged))
                    .StdMoveWidget(),
                widgetManager
                    .NewToggleField("Can Use Station From Station", station.canUseStationFromStation)
                    .SetListener(this, nameof(OnCanUseStationFromStationValueChanged))
                    .StdMoveWidget(),

                widgetManager.NewLine().StdMoveWidget(),

                infoLabel = widgetManager.NewLabel("<b>Upon Entering Station</b>"),
                widgetManager.NewSpace().StdMoveWidget(),

                matchPlayerPositionToggle = widgetManager
                    .NewToggleField("Match Player Position", false),
                setEnterVelocityToggle = widgetManager
                    .NewToggleField("Set Velocity", false),
                setEnterImmobilizeToggle = widgetManager
                    .NewToggleField("Set Immobilize", false),
                moveAfterEnteringToggle = widgetManager
                    .NewToggleField("Move After Entering", false),
                setVelocityWhileInStationToggle = widgetManager
                    .NewToggleField("Set Velocity While In Station", false),

                widgetManager.NewSpace().StdMoveWidget(),

                enterPlayerPositionOffsetField = widgetManager
                    .NewVector3Field("Player Position Offset", Vector3.zero),
                enterVelocityField = widgetManager
                    .NewVector3Field("Enter Velocity", Vector3.zero),
                enterImmobilizeField = widgetManager
                    .NewToggleField("Enter Immobilize", false),
                movementVelocityAfterEnteringField = widgetManager
                    .NewVector3Field("Movement Velocity After Entering", Vector3.zero),
                movementFramesAfterEnteringField = widgetManager
                    .NewIntField("Movement Frames After Entering", 1, minValue: 0),
                velocityWhileInStationField = widgetManager
                    .NewVector3Field("Velocity While In Station", Vector3.zero),

                widgetManager.NewLine().StdMoveWidget(),

                widgetManager
                    .NewButton("Enter Station")
                    .SetListener(this, nameof(OnEnterStationClick))
                    .StdMoveWidget(),
                widgetManager
                    .NewButton("Exit Station")
                    .SetListener(this, nameof(OnExitStationClick))
                    .StdMoveWidget(),

                widgetManager.NewSpace().StdMoveWidget(),

                infoLabel = widgetManager.NewLabel(""),
            });
        }

        private void Update()
        {
            infoLabel.Label
                = $"Station Mobility: {MobilityToString(station.PlayerMobility)}\n"
                + $"Station Seated: {station.seated}\n"
                + $"Station Disable Station Exit: {station.disableStationExit}\n"
                + $"Station Can Use Station From Station: {station.canUseStationFromStation}\n"
                + $"Velocity: {localPlayer.GetVelocity()}\n"
                + $"Grounded: {localPlayer.IsPlayerGrounded()}";

            DoMovementAfterEntering();
            SetVelocityWhileInStation();
        }

        private void DoMovementAfterEntering()
        {
            if (movementFramesRemaining == 0)
                return;
            movementFramesRemaining--;
            stationPlayerPosition.position += stationPlayerPosition.rotation * movementVelocityAfterEnteringField.Value * Time.deltaTime;
        }

        private void SetVelocityWhileInStation()
        {
            if (!setVelocityWhileInStationToggle.Value)
                return;
            localPlayer.SetVelocity(velocityWhileInStationField.Value);
        }

        private string MobilityToString(VRCStation.Mobility mobility)
        {
            switch (mobility)
            {
                case VRCStation.Mobility.Mobile:
                    return "Mobile";
                case VRCStation.Mobility.Immobilize:
                    return "Immobilize";
                case VRCStation.Mobility.ImmobilizeForVehicle:
                    return "ImmobilizeForVehicle";
                default:
                    return "Unknown";
            }
        }

        public void OnSetMobilityToMobileClick() => station.PlayerMobility = VRCStation.Mobility.Mobile;
        public void OnSetImmobilizeToImmobilizeClick() => station.PlayerMobility = VRCStation.Mobility.Immobilize;
        public void OnSetMobilityToImmobilizeForVehicleClick() => station.PlayerMobility = VRCStation.Mobility.ImmobilizeForVehicle;

        public void OnDisableStationExitValueChanged() => station.disableStationExit = valueEditor.GetSendingToggleField().Value;
        public void OnCanUseStationFromStationValueChanged() => station.canUseStationFromStation = valueEditor.GetSendingToggleField().Value;

        public void OnEnterStationClick()
        {
            if (matchPlayerPositionToggle.Value)
                stationPlayerPosition.SetPositionAndRotation(
                    localPlayer.GetPosition() + localPlayer.GetRotation() * enterPlayerPositionOffsetField.Value,
                    localPlayer.GetRotation());
            if (setEnterVelocityToggle.Value)
                localPlayer.SetVelocity(enterVelocityField.Value);
            if (setEnterImmobilizeToggle.Value)
                localPlayer.Immobilize(enterImmobilizeField.Value);
            station.UseStation(localPlayer);
            if (moveAfterEnteringToggle.Value)
                movementFramesRemaining = movementFramesAfterEnteringField.IntValue;
        }

        public void OnExitStationClick()
        {
            station.ExitStation(localPlayer);
        }
    }
}
