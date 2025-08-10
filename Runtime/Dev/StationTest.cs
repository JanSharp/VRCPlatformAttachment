using UdonSharp;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class StationTest : UdonSharpBehaviour
    {
        public VRC.SDK3.Components.VRCStation station;
        private VRCPlayerApi usedByPlayer;

        public override void OnStationEntered(VRCPlayerApi player)
        {
            station.PlayerMobility = player.isLocal
                ? VRCStation.Mobility.Mobile
                : VRCStation.Mobility.ImmobilizeForVehicle;
        }

        public override void Interact()
        {
            if (Utilities.IsValid(usedByPlayer))
            {
                station.ExitStation(usedByPlayer);
                usedByPlayer = null;
                return;
            }
            VRCPlayerApi[] players = VRCPlayerApi.GetPlayers(new VRCPlayerApi[VRCPlayerApi.GetPlayerCount()]);
            foreach (var player in players)
            {
                if (player.isLocal)
                    continue;
                usedByPlayer = player;
                station.UseStation(player); // Can't put other players into stations...
                break;
            }
        }
    }
}
