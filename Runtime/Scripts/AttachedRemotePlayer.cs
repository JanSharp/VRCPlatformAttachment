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
        [HideInInspector][SerializeField][SingletonReference] QuickDebugUI qd;

        [SerializeField] private VRC.SDK3.Components.VRCStation station;
        [SerializeField] private Transform stationPlayerPosition;

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
            station.PlayerMobility = isLocal ? VRCStation.Mobility.Mobile : VRCStation.Mobility.ImmobilizeForVehicle;
            if (isLocal)
                manager.SetLocalAttachedPlayerSync(this);
        }

        #region Local

        public void BeginSyncLoop(AttachablePlatform attachedPlatform)
        {
            syncedAttachedPlatformId = attachedPlatform.id;
            this.attachedPlatform = attachedPlatform.transform;
            positionErrorLastFrame = Vector3.zero;
            rotationErrorLastFrame = Quaternion.identity;
            // stationPlayerPosition.SetParent(this.attachedPlatform, worldPositionStays: false);
            // prevDesiredRotation = GetProjectedHeadRotation();
            TeleportPlayer(player.GetPosition(), player.GetRotation(), Vector3.zero, Quaternion.identity);
            // prevOrigin = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            // prevHead = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            // prevPosition = player.GetPosition();
            // prevRotation = player.GetRotation();

            RequestSerialization();
            if (isSyncLoopRunning)
                return;
            shouldSyncLoopRunning = true;
            isSyncLoopRunning = true;
            SendCustomEventDelayedSeconds(nameof(SyncLoop), SyncLoopInterval);
        }

        /// <summary>Handles quaternions where their forward vector is pointing straight up or down.</summary>
        /// <returns>A quaternion purely rotating around the Y axis. If the given <paramref name="rotation"/>
        /// was upside down, the result does not reflect as such. The "up" of the resulting rotation is always
        /// equal to <see cref="Vector3.up"/>.</returns>
        public static Quaternion ProjectOntoYPlane(Quaternion rotation)
        {
            Vector3 projectedForward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            return projectedForward == Vector3.zero // Facing straight up or down?
                ? Quaternion.LookRotation(rotation * Vector3.down) // Imagine a head facing staring up. The chin is down.
                : Quaternion.LookRotation(projectedForward.normalized);
        }

        private Quaternion GetProjectedHeadRotation()
        {
            return ProjectOntoYPlane(player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation);
        }

        // private VRCPlayerApi.TrackingData prevOrigin;
        // private VRCPlayerApi.TrackingData prevHead;
        // private Vector3 prevPosition;
        // private Quaternion prevRotation;
        // private Quaternion prevDesiredRotation;

        private Vector3 positionErrorLastFrame;
        private Quaternion rotationErrorLastFrame;

        private VRCPlayerApi.TrackingData PrintOriginDiffs(
            VRCPlayerApi.TrackingData originalOrigin,
            VRCPlayerApi.TrackingData prevOrigin,
            string actionName)
        {
            var origin = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            Vector3 inducedMovement = origin.position - prevOrigin.position;
            Quaternion inducedRotation = Quaternion.Inverse(prevOrigin.rotation) * origin.rotation;
            Vector3 totalInducedMovement = origin.position - originalOrigin.position;
            Quaternion totalInducedRotation = Quaternion.Inverse(originalOrigin.rotation) * origin.rotation;
            qd.ShowForOneFrame(this, $"{actionName} induced movement", $"{inducedMovement:f3}, total: {totalInducedMovement:f3}");
            qd.ShowForOneFrame(this, $"{actionName} induced rotation", $"{inducedRotation.eulerAngles:f3}, total: {totalInducedRotation.eulerAngles:f3}");
            return origin;
        }

        private int funkyIterations;
        private double funkyTimingMs;
        private Vector3 funkyPositionErrorLastFrame;
        private Quaternion funkyRotationErrorLastFrame;

        public void TeleportPlayer(Vector3 position, Quaternion rotation, Vector3 positionDiff, Quaternion rotationDiff)
        {
            #region Attempts

            // // This doesn't prevent looking left and right, but it causes unintentional additional head rotation,
            // // resulting in continuous spinning when tilting the head and looking down or up.
            // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, rotation * Quaternion.Inverse(rotation) * player.GetRotation());
            // station.UseStation(player);

            // // Nope.
            // manager.RoomAlignedTeleport(position, player.GetRotation(), lerpOnRemote: true);
            // Quaternion toUndo = Quaternion.Inverse(player.GetRotation());
            // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, toUndo * rotation);
            // station.UseStation(player);

            // // Exact same spinning behavior as the first one
            // Quaternion originalRotation = player.GetRotation();
            // manager.RoomAlignedTeleport(position, originalRotation, lerpOnRemote: true);
            // Quaternion toUndo = Quaternion.Inverse(player.GetRotation());
            // // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true); // Nope, makes you no longer move with the platform.
            // stationPlayerPosition.SetPositionAndRotation(position, Quaternion.Inverse(toUndo * originalRotation) * rotation);
            // station.UseStation(player);

            // // Still same spinning issue.
            // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, ProjectOntoYPlane(rotation * Quaternion.Inverse(rotation)) * player.GetRotation());
            // station.UseStation(player);

            // // Still same spinning issue, but this time it does move along with the platform even with that second teleport
            // Quaternion originalRotation = player.GetRotation();
            // manager.RoomAlignedTeleport(player.GetPosition(), originalRotation, lerpOnRemote: true);
            // Quaternion toUndo = Quaternion.Inverse(player.GetRotation());
            // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, Quaternion.Inverse(toUndo * originalRotation) * rotation);
            // station.UseStation(player);

            // // Still spinning, but also when moving around, not just looking around. It's weird.
            // Vector3 originalPosition = player.GetPosition();
            // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, player.GetRotation());
            // station.UseStation(player);
            // Quaternion rotationDiff = rotation * Quaternion.Inverse(player.GetRotation());
            // manager.RoomAlignedTeleport(originalPosition, rotation * rotationDiff, lerpOnRemote: true);
            // station.UseStation(player);

            // // Same as above.
            // Vector3 originalPosition = player.GetPosition();
            // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, GetProjectedHeadRotation());
            // station.UseStation(player);
            // Quaternion rotationDiff = rotation * Quaternion.Inverse(GetProjectedHeadRotation());
            // manager.RoomAlignedTeleport(originalPosition, rotation * rotationDiff, lerpOnRemote: true);
            // station.UseStation(player);

            // // Same as before, the teleport truly does make the player exist the station instantly. No funny business there.
            // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true);
            // station.ExitStation(player);
            // stationPlayerPosition.SetPositionAndRotation(position, player.GetRotation());
            // station.UseStation(player);

            // // Random slow rotation while standing on a platform
            // // Head lock
            // // And spin when tilted
            // // Worst of all worlds!
            // Vector3 originalPosition = player.GetPosition();
            // Quaternion originalRotation = player.GetRotation();
            // station.ExitStation(player);
            // Quaternion rotationDiff = Quaternion.Inverse(originalRotation) * player.GetRotation();
            // stationPlayerPosition.SetPositionAndRotation(position, rotation);
            // station.UseStation(player);
            // manager.RoomAlignedTeleport(originalPosition, rotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, player.GetRotation() * Quaternion.Inverse(rotationDiff));
            // station.UseStation(player);

            // // Random slow rotation while standing on a platform
            // // No Head lock!
            // // And spin when tilted
            // // Slightly better!
            // Vector3 originalPosition = player.GetPosition();
            // Quaternion originalRotation = GetProjectedHeadRotation();
            // station.ExitStation(player);
            // Quaternion rotationDiff = Quaternion.Inverse(originalRotation) * GetProjectedHeadRotation();
            // stationPlayerPosition.SetPositionAndRotation(position, rotation);
            // station.UseStation(player);
            // manager.RoomAlignedTeleport(originalPosition, rotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, GetProjectedHeadRotation() * Quaternion.Inverse(rotationDiff));
            // station.UseStation(player);

            // // Still no difference
            // Vector3 originalPosition = player.GetPosition();
            // player.TeleportTo(position, player.GetRotation(), VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, player.GetRotation());
            // station.UseStation(player);
            // Quaternion rotationDiff = rotation * Quaternion.Inverse(player.GetRotation());
            // player.TeleportTo(originalPosition, rotation * rotationDiff, VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, lerpOnRemote: true);
            // station.UseStation(player);

            // // Still same as the first one.
            // player.TeleportTo(position, rotation, VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, GetProjectedHeadRotation());
            // station.UseStation(player);

            // // No head lock, but still spinning.
            // Quaternion preRotation = GetProjectedHeadRotation();
            // station.ExitStation(player);
            // Quaternion postRotation = GetProjectedHeadRotation();
            // Quaternion rotationDiff = Quaternion.Inverse(postRotation) * preRotation;
            // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, GetProjectedHeadRotation() * rotationDiff);
            // station.UseStation(player);

            // // You can still turn you head sometimes and it feels as though it initiates a spin too, but it
            // // pushes the player off the platform too soon to be able to tell.
            // station.ExitStation(player);
            // manager.RoomAlignedTeleport(position, Quaternion.identity, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, Quaternion.identity);
            // station.UseStation(player);

            // // Makes you no longer move with the platform
            // station.ExitStation(player);
            // manager.RoomAlignedTeleport(position, Quaternion.identity, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, Quaternion.identity);
            // station.UseStation(player);
            // station.ExitStation(player);
            // manager.RoomAlignedTeleport(position, Quaternion.identity, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, Quaternion.identity);
            // station.UseStation(player);
            // station.ExitStation(player);
            // manager.RoomAlignedTeleport(position, Quaternion.identity, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, Quaternion.identity);
            // station.UseStation(player);
            // station.ExitStation(player);
            // manager.RoomAlignedTeleport(position, Quaternion.identity, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, Quaternion.identity);
            // station.UseStation(player);

            // // Don't remember, but also broken, probably in the same way as all the rest.
            // station.ExitStation(player);
            // Quaternion headMovementSinceLastFrame = Quaternion.Inverse(rotationLastFrame) * GetProjectedHeadRotation();
            // desiredRotationLastFrame = desiredRotationLastFrame * rotationDiff * headMovementSinceLastFrame;
            // manager.RoomAlignedTeleport(position, desiredRotationLastFrame, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(position, desiredRotationLastFrame);
            // station.UseStation(player);
            // rotationLastFrame = GetProjectedHeadRotation();

            // Misc.

            // station.ExitStation(player);

            // // station.PlayerMobility = VRCStation.Mobility.ImmobilizeForVehicle;
            // // stationPlayerPosition.SetPositionAndRotation(this.attachedPlatform.position, this.attachedPlatform.rotation);
            // // station.PlayerMobility = VRCStation.Mobility.Mobile;
            // station.ExitStation(player);
            // manager.RoomAlignedTeleport(position, rotation, lerpOnRemote: true);
            // Quaternion playerRotation = player.GetRotation();
            // Quaternion preHeadRotation = ProjectOntoYPlane(player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation);
            // stationPlayerPosition.SetPositionAndRotation(position, playerRotation);
            // station.UseStation(player);
            // station.ExitStation(player);
            // Quaternion postHeadRotation = ProjectOntoYPlane(player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation);
            // Quaternion headRotationOffset = Quaternion.Inverse(postHeadRotation) * preHeadRotation;
            // manager.RoomAlignedTeleport(position, Quaternion.Inverse(headRotationOffset) * rotation, lerpOnRemote: true);
            // // stationPlayerPosition.SetPositionAndRotation(position, headRotationOffset * playerRotation);
            // // // station.stationEnterPlayerLocation = null;
            // station.UseStation(player);
            // // // station.stationEnterPlayerLocation = stationPlayerPosition;

            #endregion

            #region Reimplementing Movement
            // var origin = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            // var head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

            // Quaternion prevOffset = Quaternion.Inverse(prevOrigin.rotation) * prevHead.rotation;
            // Quaternion offset = Quaternion.Inverse(origin.rotation) * head.rotation;
            // Quaternion headMovement = Quaternion.Inverse(prevOffset) * offset;

            // prevPosition = prevPosition +
            //     prevOrigin.rotation * (Quaternion.Inverse(origin.rotation) * (head.position - origin.position)
            //         - Quaternion.Inverse(prevOrigin.rotation) * (prevHead.position - prevOrigin.position))
            //     + positionDiff;
            // prevRotation = prevRotation * ProjectOntoYPlane(headMovement) * rotationDiff;

            // station.ExitStation(player);
            // manager.RoomAlignedTeleport(prevPosition, prevRotation, lerpOnRemote: true);
            // stationPlayerPosition.SetPositionAndRotation(prevPosition, prevRotation);
            // station.UseStation(player);

            // prevOrigin = origin;
            // prevHead = head;
            #endregion

            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            // var originalOrigin = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            manager.GetTargetPosAndRot(position, rotation, out var tpPos, out var tpRot);
            Quaternion playerRotation = Quaternion.identity;
            bool updateTiming = false;
            // Only requires a single iteration 99.9% of the time. However when the head is tilted to the left
            // or right, when looking up and down there is a single frame at some threshold where it requires
            // multiple iterations to fully undo unintentional movement and rotation induced by entering the
            // station.
            // Requires 2 at <= 40 fps, 3 at 50 fps, 5 to 6 iterations at 60 fps, cannot test higher fps.
            // Each iteration takes a bit more than 1 ms on my machine with this current implementation.
            for (int i = 0; i < 10; i++)
            {
                player.TeleportTo(tpPos, tpRot, VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint, lerpOnRemote: true);
                var desiredOrigin = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
                // var desiredOrigin = PrintOriginDiffs(originalOrigin, originalOrigin, "tp 1");
                playerRotation = i == 0 ? player.GetRotation() : playerRotation;
                stationPlayerPosition.SetPositionAndRotation(position, playerRotation);
                station.UseStation(player);
                var origin = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
                // var origin = PrintOriginDiffs(originalOrigin, desiredOrigin, "station 1");
                Vector3 posDiff = origin.position - desiredOrigin.position;
                Quaternion rotDiff = Quaternion.Inverse(desiredOrigin.rotation) * origin.rotation;
                player.TeleportTo(tpPos, tpRot, VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint, lerpOnRemote: true);
                var originPreStation2 = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
                // origin = PrintOriginDiffs(originalOrigin, origin, "tp 2");
                stationPlayerPosition.SetPositionAndRotation(position - posDiff, playerRotation * Quaternion.Inverse(rotDiff));
                station.UseStation(player);
                origin = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
                // origin = PrintOriginDiffs(originalOrigin, origin, "station 2");
                Vector3 posDiff2 = origin.position - originPreStation2.position;
                player.TeleportTo(tpPos, tpRot, VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint, lerpOnRemote: true);
                // origin = PrintOriginDiffs(originalOrigin, origin, "tp 3");
                stationPlayerPosition.SetPositionAndRotation(position - posDiff - posDiff2, playerRotation * Quaternion.Inverse(rotDiff));
                station.UseStation(player);
                origin = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
                // origin = PrintOriginDiffs(originalOrigin, origin, "station 3");
                positionErrorLastFrame = origin.position - desiredOrigin.position;
                rotationErrorLastFrame = Quaternion.Inverse(desiredOrigin.rotation) * origin.rotation;
                // qd.ShowForOneFrame(this, $"position error", $"{positionErrorLastFrame:f3}");
                // qd.ShowForOneFrame(this, $"rotation error", $"{rotationErrorLastFrame.eulerAngles:f3}");
                if (positionErrorLastFrame == Vector3.zero && rotationErrorLastFrame == Quaternion.identity)
                    break;
                funkyIterations = i + 2;
                updateTiming = true;
                // position -= positionErrorLastFrame;
                // playerRotation *= Quaternion.Inverse(rotationErrorLastFrame);
            }
            sw.Stop();
            if (updateTiming)
            {
                funkyTimingMs = sw.Elapsed.TotalMilliseconds;
                funkyPositionErrorLastFrame = positionErrorLastFrame;
                funkyRotationErrorLastFrame = rotationErrorLastFrame;
            }
            qd.ShowForOneFrame(this, "funkyIterations", $"{funkyIterations:d}");
            qd.ShowForOneFrame(this, "funkyTimingMs", $"{funkyTimingMs:f3}");
            qd.ShowForOneFrame(this, "funkyPositionErrorLastFrame", $"{funkyPositionErrorLastFrame:f3}");
            qd.ShowForOneFrame(this, "funkyRotationErrorLastFrame", $"{funkyRotationErrorLastFrame.eulerAngles:f3}");
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
