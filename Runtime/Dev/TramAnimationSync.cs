using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TramAnimationSync : UdonSharpBehaviour
    {
        public Animator animator;

        [UdonSynced] private int syncedTagHash;
        [UdonSynced] private float syncedLength;
        [UdonSynced] private float syncedNormalizedTime;

        public void Start()
        {
            if (Networking.IsOwner(this.gameObject))
                RequestSerialization();
        }

        private int lastProgressUpdate = 0;
        public void Update()
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            int progress = (int)((info.normalizedTime % 1f) * 10f);
            if (progress == lastProgressUpdate)
                return;
            lastProgressUpdate = progress;
            Debug.Log($"<dlt> Tram animation normalizedTime: {lastProgressUpdate / 10f}");
        }

        public override void OnPreSerialization()
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            syncedTagHash = info.tagHash;
            syncedLength = info.length;
            syncedNormalizedTime = info.normalizedTime % 1f;
        }

        public override void OnDeserialization(DeserializationResult result)
        {
            float transitTime = result.receiveTime - result.sendTime;
            float transitNormalizedTime = transitTime / syncedLength;
            animator.Play(syncedTagHash, 0, (syncedNormalizedTime + transitNormalizedTime) % 1f);
        }
    }
}
