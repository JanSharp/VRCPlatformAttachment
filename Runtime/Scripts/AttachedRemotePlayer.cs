using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class AttachedRemotePlayer : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] private PlatformAttachmentManager manager;
        [HideInInspector][SerializeField][SingletonReference] private UpdateManager updateManager;
        /// <summary>
        /// <para>Used by the <see cref="UpdateManager"/>.</para>
        /// </summary>
        [System.NonSerialized] public int customUpdateInternalIndex;
#if PLATFORM_ATTACHMENT_DEBUG
        [HideInInspector][SerializeField][SingletonReference] private QuickDebugUI qd;
        [HideInInspector][SerializeField][SingletonReference] private RemoteSmoothingUI remoteSmoothing;
#endif

        public VRC.SDK3.Components.VRCStation station;
        public Transform stationPlayerPosition;

#if PLATFORM_ATTACHMENT_DEBUG
        private float InterpolationDuration => remoteSmoothing.InterpolationDuration;
        private float DesiredVelocityMultiplier => remoteSmoothing.DesiredVelocityMultiplier;
        private float SyncLoopInterval => remoteSmoothing.SyncLoopInterval;
        private float VelocityDiffCutoff => remoteSmoothing.VelocityDiffCutoff;
        private float AngularVelocityDiffCutoff => remoteSmoothing.AngularVelocityDiffCutoff;
#else
        private const float InterpolationDuration = 0.15f;
        private const float DesiredVelocityMultiplier = 0.4f;
        private const float SyncLoopInterval = 0.3f;
        private const float VelocityDiffCutoff = 0.0f;
        private const float AngularVelocityDiffCutoff = 0.0f;
#endif

        private VRCPlayerApi player;
        // Local
        private bool shouldSyncLoopBeRunning = false;
        private bool isSyncLoopRunning = false;
        private Transform attachedPlatform; // Also used on remote.
        // Local to Remote
        [UdonSynced] private uint syncedAttachedPlatformId = 0u;
        [UdonSynced] private Vector3 syncedLocalPosition;
        [UdonSynced] private Quaternion syncedLocalRotation;
        // Remote
        private uint attachedPlatformId = 0u;
        private Vector3 velocityRelativeToPlatform;
        private float angularVelocityRelativeToPlatform;
        // private float interpolationProgress;
        private Vector3 currentLocalPosition;
        private Quaternion currentLocalRotation;

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

        private void AttachRemote(uint platformId)
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(AttachRemote)} - platformId: {platformId}");
#endif
            attachedPlatformId = platformId;
            attachedPlatform = manager.GetPlatformFromId(platformId).transform;
            currentLocalPosition = attachedPlatform.InverseTransformPoint(player.GetPosition());
            currentLocalRotation = Quaternion.Inverse(PlatformAttachmentManager.ProjectOntoYPlane(attachedPlatform.rotation)) * player.GetRotation();
            // This might be nonsensical, if the player is already in the station velocity isn't going to
            // give us anything useful. Furthermore I don't even know if remote players give us useful
            // velocity values to begin with.
            velocityRelativeToPlatform = attachedPlatform.InverseTransformVector(player.GetVelocity());
            angularVelocityRelativeToPlatform = 0f;
            updateManager.Register(this);
        }

        private void SwitchRemotePlatform(uint platformId)
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(SwitchRemotePlatform)} - attachedPlatformId: {attachedPlatformId}, platformId: {platformId}");
#endif
            Vector3 worldVelocity = attachedPlatform.TransformVector(velocityRelativeToPlatform);
            attachedPlatformId = platformId;
            attachedPlatform = manager.GetPlatformFromId(platformId).transform;
            currentLocalPosition = attachedPlatform.InverseTransformPoint(stationPlayerPosition.position);
            currentLocalRotation = Quaternion.Inverse(PlatformAttachmentManager.ProjectOntoYPlane(attachedPlatform.rotation)) * stationPlayerPosition.rotation;
            velocityRelativeToPlatform = attachedPlatform.InverseTransformVector(worldVelocity);
            // Leaving angularVelocityRelativeToPlatform untouched? Probably fine?
        }

        private void DetachRemote()
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(DetachRemote)} - attachedPlatformId: {attachedPlatformId}");
#endif
            attachedPlatformId = 0u;
            attachedPlatform = null;
            updateManager.Deregister(this);
        }

        public override void OnDeserialization()
        {
            // #if PLATFORM_ATTACHMENT_DEBUG
            //             Debug.Log($"[PlatformAttachmentDebug] {name}  {nameof(OnDeserialization)} - syncedAttachedPlatformId: {syncedAttachedPlatformId}, syncedLocalPosition: {syncedLocalPosition}");
            // #endif
            if (!Utilities.IsValid(player))
                return;
            // interpolationProgress = 0f;
            if (syncedAttachedPlatformId == attachedPlatformId)
                return;
            if (attachedPlatformId == 0u)
                AttachRemote(syncedAttachedPlatformId);
            else if (syncedAttachedPlatformId == 0u)
                DetachRemote();
            else // Both non 0.
                SwitchRemotePlatform(syncedAttachedPlatformId);
        }

        public void CustomUpdate()
        {
            InterpolatePosition();
            InterpolateRotation();
            stationPlayerPosition.SetPositionAndRotation(
                attachedPlatform.TransformPoint(currentLocalPosition),
                PlatformAttachmentManager.ProjectOntoYPlane(attachedPlatform.rotation) * currentLocalRotation);
        }

        private void InterpolatePosition()
        {
            Vector3 remainder = syncedLocalPosition - currentLocalPosition;
            Vector3 desiredLocalVelocity = (remainder / InterpolationDuration) * DesiredVelocityMultiplier;
            float velocityDiff = VelocityDiffCutoff / Vector3.Distance(velocityRelativeToPlatform, desiredLocalVelocity);
            float deltaTime = Time.deltaTime;
            velocityRelativeToPlatform = Vector3.Lerp(velocityRelativeToPlatform, desiredLocalVelocity, Mathf.Clamp01(Mathf.Max(velocityDiff, deltaTime / InterpolationDuration)));
            currentLocalPosition += velocityRelativeToPlatform * deltaTime;
        }

        private void InterpolateRotation()
        {
            Quaternion diff = Quaternion.Inverse(currentLocalRotation) * syncedLocalRotation;
            diff.ToAngleAxis(out float angle, out Vector3 axis);
            if (axis.y < 0)
                angle = -angle;
            float desiredLocalVelocity = (angle / InterpolationDuration) * DesiredVelocityMultiplier;
            float velocityDiff = AngularVelocityDiffCutoff / Mathf.Abs(Mathf.Abs(angularVelocityRelativeToPlatform) - Mathf.Abs(desiredLocalVelocity));
            float deltaTime = Time.deltaTime;
            angularVelocityRelativeToPlatform = Mathf.Lerp(angularVelocityRelativeToPlatform, desiredLocalVelocity, Mathf.Clamp01(Mathf.Max(velocityDiff, deltaTime / InterpolationDuration)));
            currentLocalRotation *= Quaternion.AngleAxis(angularVelocityRelativeToPlatform * deltaTime, Vector3.up);

            // if (interpolationProgress == 1f)
            //     return;
            // float toAdd = Time.deltaTime / InterpolationDuration;
            // // If progress is 0.6f and toAdd is 0.1f then currentStep is 0.25f.
            // // Effectively one quarter of the way from current point (0.6f) to target point (1.0f).
            // float currentStep = toAdd / (1f - interpolationProgress);
            // interpolationProgress += toAdd;
            // if (interpolationProgress < 1f)
            //     currentLocalRotation = Quaternion.Lerp(currentLocalRotation, syncedLocalRotation, currentStep);
            // else
            // {
            //     interpolationProgress = 1f;
            //     currentLocalRotation = syncedLocalRotation;
            // }
        }

        #endregion
    }
}
