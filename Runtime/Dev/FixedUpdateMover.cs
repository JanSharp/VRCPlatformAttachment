using UdonSharp;
using UnityEngine;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class FixedUpdateMover : UdonSharpBehaviour
    {
        public Transform referenceTransform;
        public Transform platformTransform;
        private Vector3 offset;

        private void Start()
        {
            offset = platformTransform.position - referenceTransform.position;
        }

        private void FixedUpdate()
        {
            platformTransform.position = referenceTransform.position + offset;
        }
    }
}
