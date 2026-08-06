using UdonSharp;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UseStationOnInteract : UdonSharpBehaviour
    {
        public VRC.SDK3.Components.VRCStation station;

        public override void Interact()
        {
            station.UseStation(Networking.LocalPlayer);
        }
    }
}
