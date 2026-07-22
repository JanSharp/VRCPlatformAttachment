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
#if PLATFORM_ATTACHMENT_DEBUG
        [HideInInspector][SerializeField][SingletonReference] QuickDebugUI qd;
#endif

        public VRC.SDK3.Components.VRCStation station;
        public Transform stationPlayerPosition;

        private const float InterpolationDuration = 0.4f;
        private const float SyncLoopInterval = 0.3f;

        private VRCPlayerApi player;
        // Local
        private bool shouldSyncLoopBeRunning = false;
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
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(Start)}");
#endif
            player = Networking.GetOwner(this.gameObject);
            bool isLocal = player.isLocal;
            station.PlayerMobility = VRCStation.Mobility.ImmobilizeForVehicle;
            if (isLocal)
                manager.SetLocalAttachedPlayerSync(this);
        }

        #region Local

        public override void OnStationExited(VRCPlayerApi player)
        {
            // TODO: Do something.
        }

        public void BeginSyncLoop(AttachablePlatform attachedPlatform)
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(BeginSyncLoop)} - attachedPlatform.id: {attachedPlatform.id}, shouldSyncLoopBeRunning: {shouldSyncLoopBeRunning}, isSyncLoopRunning: {isSyncLoopRunning}");
#endif
            syncedAttachedPlatformId = attachedPlatform.id;
            this.attachedPlatform = attachedPlatform.transform;
            // manager.UseLocalStation();
            RequestSerialization();
            shouldSyncLoopBeRunning = true;
            if (isSyncLoopRunning)
                return;
            isSyncLoopRunning = true;
            SendCustomEventDelayedSeconds(nameof(SyncLoop), SyncLoopInterval);
        }

        public void ChangeSyncedAttachedPlatform(AttachablePlatform attachedPlatform)
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(ChangeSyncedAttachedPlatform)} - attachedPlatform.id: {attachedPlatform.id}, shouldSyncLoopBeRunning: {shouldSyncLoopBeRunning}, isSyncLoopRunning: {isSyncLoopRunning}");
#endif
            syncedAttachedPlatformId = attachedPlatform.id;
            this.attachedPlatform = attachedPlatform.transform;
        }

        public void StopSyncLoop()
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(StopSyncLoop)} - syncedAttachedPlatformId: {syncedAttachedPlatformId}, shouldSyncLoopBeRunning: {shouldSyncLoopBeRunning}, isSyncLoopRunning: {isSyncLoopRunning}");
#endif
            // manager.TeleportPlayerOutOfStation();
            syncedAttachedPlatformId = 0u;
            attachedPlatform = null;
            shouldSyncLoopBeRunning = false;
            RequestSerialization();
        }

        public void SyncLoop()
        {
            // #if PLATFORM_ATTACHMENT_DEBUG
            //             Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(SyncLoop)} - shouldSyncLoopBeRunning: {shouldSyncLoopBeRunning}, isSyncLoopRunning: {isSyncLoopRunning}");
            // #endif
            if (!shouldSyncLoopBeRunning)
            {
                isSyncLoopRunning = false;
                return;
            }
            RequestSerialization();
            SendCustomEventDelayedSeconds(nameof(SyncLoop), SyncLoopInterval);
        }

        public override void OnPreSerialization()
        {
            // #if PLATFORM_ATTACHMENT_DEBUG
            //             Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(OnPreSerialization)} - syncedAttachedPlatformId: {syncedAttachedPlatformId}, syncedLocalPosition: {syncedLocalPosition}");
            // #endif
            if (syncedAttachedPlatformId == 0u)
                return;
            if (attachedPlatform == null)
            {
                StopSyncLoop();
                return;
            }
            syncedLocalPosition = attachedPlatform.InverseTransformPoint(stationPlayerPosition.position);
            syncedLocalRotation = Quaternion.Inverse(PlatformAttachmentManager.ProjectOntoYPlane(attachedPlatform.rotation)) * stationPlayerPosition.rotation;
            // #if PLATFORM_ATTACHMENT_DEBUG
            //             qd.ShowForOneFrame(this, "OnPreSerialization", $"syncedLocalPosition: {syncedLocalPosition}");
            // #endif
        }

        #endregion

        #region Remote

        private void UpdateRemoteAttachment()
        {
            interpolation.LerpLocalPosition(stationPlayerPosition, syncedLocalPosition, InterpolationDuration);
            interpolation.LerpLocalRotation(stationPlayerPosition, syncedLocalRotation, InterpolationDuration);
#if PLATFORM_ATTACHMENT_DEBUG
            qd.ShowForOneFrame(this, "UpdateRemoteAttachment", $"syncedLocalPosition: {syncedLocalPosition}");
#endif
        }

        private void AttachRemote(uint platformId)
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(AttachRemote)} - platformId: {platformId}");
#endif
            attachedPlatformId = platformId;
            AttachablePlatform platform = manager.GetPlatformFromId(platformId);
            stationPlayerPosition.SetParent(platform.transform, worldPositionStays: false);
            stationPlayerPosition.localPosition = syncedLocalPosition;
            stationPlayerPosition.localRotation = syncedLocalRotation;
        }

        private void DetachRemote()
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(DetachRemote)} - attachedPlatformId: {attachedPlatformId}");
#endif
            attachedPlatformId = 0u;
            interpolation.CancelPositionInterpolation(stationPlayerPosition);
            interpolation.CancelRotationInterpolation(stationPlayerPosition);
            stationPlayerPosition.SetParent(null, worldPositionStays: false);
        }

        public override void OnDeserialization()
        {
            // #if PLATFORM_ATTACHMENT_DEBUG
            //             Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(OnDeserialization)} - syncedAttachedPlatformId: {syncedAttachedPlatformId}, syncedLocalPosition: {syncedLocalPosition}");
            // #endif
            if (!Utilities.IsValid(player))
                return;
            if (syncedAttachedPlatformId == attachedPlatformId)
            {
                UpdateRemoteAttachment();
                return;
            }
            if (attachedPlatformId != 0u)
                DetachRemote();
            if (syncedAttachedPlatformId != 0u)
                AttachRemote(syncedAttachedPlatformId);
        }

        #endregion
    }
}
