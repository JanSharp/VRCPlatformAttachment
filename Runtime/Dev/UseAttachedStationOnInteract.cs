using UdonSharp;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class UseAttachedStationOnInteract : UdonSharpBehaviour
    {
        public override void Interact()
        {
            Networking.LocalPlayer.UseAttachedStation();
        }
    }
}
