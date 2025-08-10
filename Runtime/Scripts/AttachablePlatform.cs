using UdonSharp;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class AttachablePlatform : UdonSharpBehaviour
    {
        [System.NonSerialized] public uint id;
    }
}
