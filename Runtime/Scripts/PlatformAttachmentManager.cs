using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("bbe525fe8f53b070a9a6a76da1cf85ad")] // Runtime/Prefabs/PlatformAttachmentManager.prefab
    public partial class PlatformAttachmentManager : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] QuickDebugUI qd;
        public LayerMask layersToAttachTo;
        [Header("Internal")]
        public Transform naturalGripPreventionCollider;
        public Transform originDebug;

        private VRCPlayerApi localPlayer;
        private AttachedRemotePlayer localAttachedPlayerSync;
        private VRC.SDK3.Components.VRCStation localStation;
        private Transform localStationPlayerPosition;
        /// <summary>
        /// <para>Set at the beginning of <see cref="OnTrulyPostLateUpdate"/>.</para>
        /// </summary>
        private Vector3 localPlayerPosition;
        private Quaternion localPlayerRotation;
        private VRCPlayerApi.TrackingData localPlayerOrigin;

        [BuildTimeIdAssignment(nameof(allPlatformIds), nameof(highestPlatformId))]
        [HideInInspector][SerializeField] private AttachablePlatform[] allPlatforms;
        [HideInInspector][SerializeField] private uint[] allPlatformIds;
        [HideInInspector][SerializeField] private uint highestPlatformId;

        private bool isAttached;
        private Vector3 prevPlayerPos;
        private Vector3 prevLocalPos;
        private Transform prevPlatform;
        private AttachablePlatform prevAttachablePlatform;
        private Quaternion prevPlatformRotation;
        private Vector3 additionalVelocity;
        // the current frame's velocity is 70%, the prev velocity is 30%. And it repeats like that
        private const float AdditionalVelocityNewWeight = 0.35f;

        // TP related things.
        private int funkyIterations;
        private double funkyTimingMs;
        private Vector3 positionErrorLastFunkyFrame;
        private Quaternion rotationErrorLastFunkyFrame;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
        }

        public void SetLocalAttachedPlayerSync(AttachedRemotePlayer localAttachedPlayerSync)
        {
            if (localPlayer == null)
                Start();
            this.localAttachedPlayerSync = localAttachedPlayerSync;
            localStation = localAttachedPlayerSync.station;
            localStationPlayerPosition = localAttachedPlayerSync.stationPlayerPosition;
            if (isAttached)
                localAttachedPlayerSync.BeginSyncLoop(prevAttachablePlatform);
        }

        public AttachablePlatform GetPlatformFromId(uint id)
        {
            int index = System.Array.BinarySearch(allPlatformIds, id);
            return index < 0 ? null : allPlatforms[index];
        }

        public uint GetIdFromPlatform(AttachablePlatform platform)
        {
            int index = System.Array.IndexOf(allPlatforms, platform);
            return index < 0 ? 0u : allPlatformIds[index];
        }

        [OnTrulyPostLateUpdate]
        public void OnTrulyPostLateUpdate()
        {
            var origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            originDebug.SetPositionAndRotation(origin.position, origin.rotation);
            qd.ShowForOneFrame(this, "Origin Position", origin.position.ToString("f3"));
            qd.ShowForOneFrame(this, "Origin Rotation", origin.rotation.eulerAngles.ToString("f3"));

            localPlayerPosition = localPlayer.GetPosition();
            float radius = LocalPlayerCapsule.GetRadius();
            Transform platform = null;
            if (Physics.SphereCast(
                localPlayerPosition + Vector3.up * (radius + 0.1f),
                radius,
                Vector3.down,
                out RaycastHit hit,
                radius + 0.35f,
                layersToAttachTo)) // QueryTriggerInteraction.UseGlobal
            {
                platform = hit.transform;
                naturalGripPreventionCollider.position = hit.point; // TODO: Make this tilt.
            }

            if (isAttached && platform == prevPlatform)
            {
                ApplyPlatformMovement();
                return;
            }
            if (prevPlatform != null)
                Detach();
            if (platform != null)
                Attach(platform);
        }

        private void Attach(Transform platform)
        {
            AttachablePlatform attachablePlatform = platform.GetComponent<AttachablePlatform>();
            if (attachablePlatform == null)
                return;
            if (attachablePlatform.id == 0u)
                attachablePlatform.id = GetIdFromPlatform(attachablePlatform);
            isAttached = true;
            prevPlayerPos = localPlayerPosition;
            prevPlatform = platform;
            prevAttachablePlatform = attachablePlatform;
            prevLocalPos = platform.InverseTransformDirection(prevPlayerPos - platform.position);
            prevPlatformRotation = platform.rotation;
            additionalVelocity = Vector3.zero;
            naturalGripPreventionCollider.gameObject.SetActive(true);
            if (localAttachedPlayerSync != null)
                localAttachedPlayerSync.BeginSyncLoop(attachablePlatform);
        }

        private void Detach()
        {
            isAttached = false;
            prevPlatform = null;
            prevAttachablePlatform = null;
            naturalGripPreventionCollider.gameObject.SetActive(false);
            localPlayer.SetVelocity(localPlayer.GetVelocity() + additionalVelocity);
            if (localAttachedPlayerSync != null)
                localAttachedPlayerSync.StopSyncLoop();
        }

        private void ApplyPlatformMovement()
        {
            Vector3 positionDiff = prevPlatform.position + prevPlatform.TransformDirection(prevLocalPos) - prevPlayerPos;
            Quaternion platformRotation = prevPlatform.rotation;
            Quaternion rotationDiff = ProjectOntoYPlane(Quaternion.Inverse(prevPlatformRotation) * platformRotation);
            localPlayerRotation = localPlayer.GetRotation();
            localPlayerOrigin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            TeleportPlayer(localPlayerPosition + positionDiff, localPlayerRotation * rotationDiff);

            prevPlayerPos = localPlayer.GetPosition();
            additionalVelocity = (positionDiff / Time.deltaTime) * AdditionalVelocityNewWeight + (additionalVelocity * (1f - AdditionalVelocityNewWeight));
            prevLocalPos = prevPlatform.InverseTransformDirection(prevPlayerPos - prevPlatform.position);
            prevPlatformRotation = platformRotation;
        }

        /// <summary>Handles quaternions where their forward vector is pointing straight up or down.</summary>
        /// <returns>A quaternion purely rotating around the Y axis. If the given <paramref name="rotation"/>
        /// was upside down, the result does not reflect as such. The "up" of the resulting rotation is always
        /// equal to <see cref="Vector3.up"/>.</returns>
        private Quaternion ProjectOntoYPlane(Quaternion rotation)
        {
            Vector3 projectedForward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            return projectedForward == Vector3.zero // Facing straight up or down?
                ? Quaternion.LookRotation(rotation * Vector3.down) // Imagine a head facing staring up. The chin is down.
                : Quaternion.LookRotation(projectedForward.normalized);
        }

        public void UseLocalStation()
        {
            localPlayerPosition = localPlayer.GetPosition();
            localPlayerRotation = localPlayer.GetRotation();
            localPlayerOrigin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            // Uses the teleport logic to prevent rotational jumps.
            TeleportPlayer(localPlayerPosition, localPlayerRotation);
        }

        private VRCPlayerApi.TrackingData PrintOriginDiffs(
            VRCPlayerApi.TrackingData originalOrigin,
            VRCPlayerApi.TrackingData prevOrigin,
            string actionName)
        {
            var origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            Vector3 inducedMovement = origin.position - prevOrigin.position;
            Quaternion inducedRotation = Quaternion.Inverse(prevOrigin.rotation) * origin.rotation;
            Vector3 totalInducedMovement = origin.position - originalOrigin.position;
            Quaternion totalInducedRotation = Quaternion.Inverse(originalOrigin.rotation) * origin.rotation;
            qd.ShowForOneFrame(this, $"{actionName} induced movement", $"{inducedMovement:f3}, total: {totalInducedMovement:f3}");
            qd.ShowForOneFrame(this, $"{actionName} induced rotation", $"{inducedRotation.eulerAngles:f3}, total: {totalInducedRotation.eulerAngles:f3}");
            return origin;
        }

        public void TeleportPlayer(Vector3 position, Quaternion rotation)
        {
            System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
            sw.Start();
            // var originalOrigin = localPlayerOrigin;
            GetRoomAlignedTeleportTargetPosAndRot(position, rotation, out var tpPos, out var tpRot);
            Vector3 positionErrorLastFrame = Vector3.zero;
            Quaternion rotationErrorLastFrame = Quaternion.identity;
            bool updateTiming = false;
            // Only requires a single iteration 99.9% of the time. However when the head is tilted to the left
            // or right, when looking up and down there is a single frame at some threshold where it requires
            // multiple iterations to fully undo unintentional movement and rotation induced by entering the
            // station.
            // Requires 2 at <= 40 fps, 3 at 50 fps, 5 to 6 iterations at 60 fps, cannot test higher fps.
            // Each iteration takes a bit more than 1 ms on my machine with this current implementation.
            for (int i = 0; i < 10; i++)
            {
                localPlayer.TeleportTo(tpPos, tpRot, VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint, lerpOnRemote: true);
                var desiredOrigin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
                // var desiredOrigin = PrintOriginDiffs(originalOrigin, originalOrigin, "tp 1");
                localStationPlayerPosition.SetPositionAndRotation(position, localPlayerRotation);
                localStation.UseStation(localPlayer);
                var origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
                // var origin = PrintOriginDiffs(originalOrigin, desiredOrigin, "station 1");
                Vector3 posDiff = origin.position - desiredOrigin.position;
                Quaternion rotDiff = Quaternion.Inverse(desiredOrigin.rotation) * origin.rotation;
                localPlayer.TeleportTo(tpPos, tpRot, VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint, lerpOnRemote: true);
                var originPreStation2 = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
                // origin = PrintOriginDiffs(originalOrigin, origin, "tp 2");
                localStationPlayerPosition.SetPositionAndRotation(position - posDiff, localPlayerRotation * Quaternion.Inverse(rotDiff));
                localStation.UseStation(localPlayer);
                origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
                // origin = PrintOriginDiffs(originalOrigin, origin, "station 2");
                Vector3 posDiff2 = origin.position - originPreStation2.position;
                localPlayer.TeleportTo(tpPos, tpRot, VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint, lerpOnRemote: true);
                // origin = PrintOriginDiffs(originalOrigin, origin, "tp 3");
                localStationPlayerPosition.SetPositionAndRotation(position - posDiff - posDiff2, localPlayerRotation * Quaternion.Inverse(rotDiff));
                localStation.UseStation(localPlayer);
                origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
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
                positionErrorLastFunkyFrame = positionErrorLastFrame;
                rotationErrorLastFunkyFrame = rotationErrorLastFrame;
            }
            qd.ShowForOneFrame(this, "funkyIterations", $"{funkyIterations:d}");
            qd.ShowForOneFrame(this, "funkyTimingMs", $"{funkyTimingMs:f3}");
            qd.ShowForOneFrame(this, "funkyPositionErrorLastFrame", $"{positionErrorLastFunkyFrame:f3}");
            qd.ShowForOneFrame(this, "funkyRotationErrorLastFrame", $"{rotationErrorLastFunkyFrame.eulerAngles:f3}");
        }
    }
}
