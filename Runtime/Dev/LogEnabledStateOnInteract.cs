using UdonSharp;
using UnityEngine;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class LogEnabledStateOnInteract : UdonSharpBehaviour
    {
        public Collider[] colliders;
        public UdonBehaviour[] ubs;

        public override void Interact()
        {
            foreach (Collider collider in colliders)
                Debug.Log($"<dlt> {collider.name} collider enabled: {collider.enabled}", collider);
            foreach (UdonBehaviour ub in ubs)
                Debug.Log($"<dlt> {ub.name} UdonBehaviour DisableInteractive: {ub.DisableInteractive}", ub);
        }
    }
}
