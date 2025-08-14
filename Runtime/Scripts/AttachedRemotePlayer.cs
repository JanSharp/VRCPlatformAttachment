using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AttachedRemotePlayer : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] PlatformAttachmentManager manager;
        [HideInInspector][SerializeField][SingletonReference] InterpolationManager interpolation;

        public VRC.SDK3.Components.VRCStation station;
        public Transform stationPlayerPosition;

        private const float InterpolationDuration = 0.4f;
        private const float SyncLoopInterval = 0.3f;

        private VRCPlayerApi player;
        // Local
        private bool shouldSyncLoopRunning = false;
        private bool isSyncLoopRunning = false;
        private Transform attachedPlatform;
        // Local to Remote
        [UdonSynced] private uint syncedAttachedPlatformId = 0u;
        [UdonSynced] private Vector3 syncedLocalPosition;
        [UdonSynced] private Quaternion syncedLocalRotation;
        // Remote
        private uint attachedPlatformId = 0u;

        public void Start()
        {
            player = Networking.GetOwner(this.gameObject);
            bool isLocal = player.isLocal;
            station.PlayerMobility = VRCStation.Mobility.ImmobilizeForVehicle;
            if (isLocal)
                manager.SetLocalAttachedPlayerSync(this);
        }

        #region Local

        public void BeginSyncLoop(AttachablePlatform attachedPlatform)
        {
            syncedAttachedPlatformId = attachedPlatform.id;
            this.attachedPlatform = attachedPlatform.transform;
            manager.UseLocalStation();
            RequestSerialization();
            if (isSyncLoopRunning)
                return;
            shouldSyncLoopRunning = true;
            isSyncLoopRunning = true;
            SendCustomEventDelayedSeconds(nameof(SyncLoop), SyncLoopInterval);
        }

        public void StopSyncLoop()
        {
            station.ExitStation(player);
            syncedAttachedPlatformId = 0u;
            attachedPlatform = null;
            shouldSyncLoopRunning = false;
            RequestSerialization();
        }

        public void SyncLoop()
        {
            if (!shouldSyncLoopRunning)
            {
                isSyncLoopRunning = false;
                return;
            }
            RequestSerialization();
            SendCustomEventDelayedSeconds(nameof(SyncLoop), SyncLoopInterval);
        }

        public override void OnPreSerialization()
        {
            if (syncedAttachedPlatformId == 0u)
                return;
            if (attachedPlatform == null)
            {
                StopSyncLoop();
                return;
            }
            syncedLocalPosition = attachedPlatform.InverseTransformPoint(player.GetPosition());
            syncedLocalRotation = Quaternion.Inverse(attachedPlatform.rotation) * player.GetRotation();
        }

        #endregion

        #region Remote

        private void UpdateAttachment()
        {
            interpolation.InterpolateLocalPosition(stationPlayerPosition, syncedLocalPosition, InterpolationDuration);
            interpolation.InterpolateLocalRotation(stationPlayerPosition, syncedLocalRotation, InterpolationDuration);
        }

        private void Attach(uint platformId)
        {
            attachedPlatformId = platformId;
            AttachablePlatform platform = manager.GetPlatformFromId(platformId);
            stationPlayerPosition.SetParent(platform.transform, worldPositionStays: false);
            stationPlayerPosition.localPosition = syncedLocalPosition;
            stationPlayerPosition.localRotation = syncedLocalRotation;
        }

        private void Detach()
        {
            attachedPlatformId = 0u;
            interpolation.CancelLocalPositionInterpolation(stationPlayerPosition);
            interpolation.CancelLocalRotationInterpolation(stationPlayerPosition);
            stationPlayerPosition.SetParent(null, worldPositionStays: false);
        }

        public override void OnDeserialization()
        {
            if (!Utilities.IsValid(player))
                return;
            if (syncedAttachedPlatformId == attachedPlatformId)
            {
                UpdateAttachment();
                return;
            }
            if (attachedPlatformId != 0u)
                Detach();
            if (syncedAttachedPlatformId != 0u)
                Attach(syncedAttachedPlatformId);
        }

        #endregion
    }
}
