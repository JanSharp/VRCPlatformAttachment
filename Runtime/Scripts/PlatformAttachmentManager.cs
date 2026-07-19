using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("bbe525fe8f53b070a9a6a76da1cf85ad")] // Runtime/Prefabs/PlatformAttachmentManager.prefab
    public partial class PlatformAttachmentManager : UdonSharpBehaviour
    {
        [HideInInspector][SerializeField][SingletonReference] QuickDebugUI qd;
        public LayerMask layersToAttachTo;
        [Header("Internal")]
        public Transform originDebug;
        public Transform avatarRootDebug;

        private VRCPlayerApi localPlayer;
        private AttachedRemotePlayer localAttachedPlayerSync;
        private VRC.SDK3.Components.VRCStation localStation;
        private Transform localStationPlayerPosition;
        // private Vector3 stationPositionLocalToOrigin;
        // private Quaternion stationRotationLocalToOrigin;
        // private Vector3 originPositionLocalToStation;
        // private Quaternion originRotationLocalToStation;
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
        [System.NonSerialized] public Vector3 attachedLocalPosition;
        [System.NonSerialized] public Quaternion attachedLocalRotation;
        private Transform attachedPlatform;
        private AttachablePlatform attachedAttachablePlatform;
        private Vector3 additionalVelocity;
        // the current frame's velocity is 70%, the prev velocity is 30%. And it repeats like that
        private const float AdditionalVelocityNewWeight = 0.35f;

        private const int MaxTPIterations = 10;
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
        private int funkyIterations;
        private double funkyTimingMs;
        private System.Diagnostics.Stopwatch funkyTpSw = new System.Diagnostics.Stopwatch();
        private System.Diagnostics.Stopwatch totalSw = new System.Diagnostics.Stopwatch();
        private object[] totalSwData;
        private System.Diagnostics.Stopwatch getTrackingDataSw = new System.Diagnostics.Stopwatch();
        private object[] getTrackingDataSwData;
        private System.Diagnostics.Stopwatch exitStationSw = new System.Diagnostics.Stopwatch();
        private object[] exitStationSwData;
        private System.Diagnostics.Stopwatch tpSw = new System.Diagnostics.Stopwatch();
        private object[] tpSwData;
        private System.Diagnostics.Stopwatch useStationSw = new System.Diagnostics.Stopwatch();
        private object[] useStationSwData;
#endif
#if PLATFORM_ATTACHMENT_DEBUG
        private Vector3 positionErrorLastFunkyFrame;
        private Quaternion rotationErrorLastFunkyFrame;
#endif

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
            totalSwData = StopwatchUtil.CreateDataContainer();
            getTrackingDataSwData = StopwatchUtil.CreateDataContainer();
            exitStationSwData = StopwatchUtil.CreateDataContainer();
            tpSwData = StopwatchUtil.CreateDataContainer();
            useStationSwData = StopwatchUtil.CreateDataContainer();
#endif
        }

        public void SetLocalAttachedPlayerSync(AttachedRemotePlayer localAttachedPlayerSync)
        {
            if (localPlayer == null)
                Start();
            this.localAttachedPlayerSync = localAttachedPlayerSync;
            localStation = localAttachedPlayerSync.station;
            localStationPlayerPosition = localAttachedPlayerSync.stationPlayerPosition;
            if (isAttached)
                localAttachedPlayerSync.BeginSyncLoop(attachedAttachablePlatform);
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

        // [OnTrulyPostLateUpdate]
        private void LateUpdate()
        {
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
            totalSw.Start();
#endif
#if PLATFORM_ATTACHMENT_DEBUG
            var origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            if (originDebug != null)
                originDebug.SetPositionAndRotation(origin.position, origin.rotation);
            qd.ShowForOneFrame(this, "Origin Position", origin.position.ToString("f3"));
            qd.ShowForOneFrame(this, "Origin Rotation", origin.rotation.eulerAngles.ToString("f3"));
            //
            var avatarRoot = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.AvatarRoot);
            if (avatarRootDebug != null)
            {
                // Appears to be effectively following the head, but of course down on the ground level.
                avatarRootDebug.SetPositionAndRotation(avatarRoot.position, avatarRoot.rotation);
                // Similarly following the head.
                // avatarRootDebug.SetPositionAndRotation(localPlayer.GetPosition(), localPlayer.GetRotation());
                // But in both of these cases, once in a station they have an unchanging offset to the station.
            }
#endif

            localPlayerPosition = localPlayer.GetPosition();
            float radius = LocalPlayerCapsule.GetRadius();
            Transform platform = null;
            if (Physics.SphereCast(
                localPlayerPosition + Vector3.up * (radius + 0.15f),
                isAttached ? radius + 0.1f : radius + 0.05f,
                Vector3.down,
                out RaycastHit hit,
                maxDistance: isAttached ? radius + 1f : radius + 0.40f,
                layersToAttachTo)) // QueryTriggerInteraction.UseGlobal
            {
                platform = hit.transform;
            }

            if (isAttached && platform == attachedPlatform)
            {
                ApplyPlatformMovement();
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                totalSw.Stop();
                ShowPerformance();
#endif
                return;
            }
            if (attachedPlatform != null)
                Detach();
            if (platform != null)
                Attach(platform);
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
            totalSw.Stop();
            ShowPerformance();
#endif
        }

#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
        private void ShowPerformance()
        {
            qd.ShowForOneFrame(this, "total ms", StopwatchUtil.FormatAvgMinMax(totalSw, totalSwData));
            qd.ShowForOneFrame(this, "get tracking data ms", StopwatchUtil.FormatAvgMinMax(getTrackingDataSw, getTrackingDataSwData));
            qd.ShowForOneFrame(this, "exit station ms", StopwatchUtil.FormatAvgMinMax(exitStationSw, exitStationSwData));
            qd.ShowForOneFrame(this, "tp ms", StopwatchUtil.FormatAvgMinMax(tpSw, tpSwData));
            qd.ShowForOneFrame(this, "use station ms", StopwatchUtil.FormatAvgMinMax(useStationSw, useStationSwData));
            totalSw.Reset();
            getTrackingDataSw.Reset();
            exitStationSw.Reset();
            tpSw.Reset();
            useStationSw.Reset();
        }
#endif

        private void Attach(Transform platform)
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] Manager  {nameof(Attach)}");
#endif
            AttachablePlatform attachablePlatform = platform.GetComponent<AttachablePlatform>();
            if (attachablePlatform == null)
                return;
            if (attachablePlatform.id == 0u)
                attachablePlatform.id = GetIdFromPlatform(attachablePlatform);
            isAttached = true;
            attachedPlatform = platform;
            attachedAttachablePlatform = attachablePlatform;
            attachedLocalPosition = platform.InverseTransformPoint(localPlayerPosition);
            attachedLocalRotation = Quaternion.Inverse(ProjectOntoYPlane(platform.rotation)) * localPlayer.GetRotation();
            additionalVelocity = Vector3.zero;
            if (localAttachedPlayerSync != null)
                localAttachedPlayerSync.BeginSyncLoop(attachablePlatform);
        }

        private void Detach()
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] Manager  {nameof(Detach)}");
#endif
            isAttached = false;
            attachedPlatform = null;
            attachedAttachablePlatform = null;
            // localPlayer.SetVelocity(localPlayer.GetVelocity() + additionalVelocity);
            if (localAttachedPlayerSync != null)
                localAttachedPlayerSync.StopSyncLoop();
        }

        private void ApplyPlatformMovement()
        {
            ApplyUserInput();
            MoveLocalStationToAttachedLocation();
            //             Vector3 positionDiff = attachedPlatform.position + attachedPlatform.TransformPoint(attachedLocalPosition) - prevPlayerPos;
            //             Quaternion platformRotation = attachedPlatform.rotation;
            //             Quaternion rotationDiff = ProjectOntoYPlane(Quaternion.Inverse(prevPlatformRotation) * platformRotation);
            //             localPlayerRotation = localPlayer.GetRotation();
            // #if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
            //             getTrackingDataSw.Start();
            // #endif
            //             localPlayerOrigin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            // #if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
            //             getTrackingDataSw.Stop();
            // #endif
            //             TeleportPlayer(localPlayerPosition + positionDiff, localPlayerRotation * rotationDiff, positionDiff, rotationDiff);

            //             prevPlayerPos = localPlayer.GetPosition();
            //             additionalVelocity = (positionDiff / Time.deltaTime) * AdditionalVelocityNewWeight + (additionalVelocity * (1f - AdditionalVelocityNewWeight));
            //             attachedLocalPosition = attachedPlatform.InverseTransformDirection(prevPlayerPos - attachedPlatform.position);
            //             prevPlatformRotation = platformRotation;
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
            // localStationPlayerPosition.SetPositionAndRotation(localPlayer.GetPosition(), localPlayer.GetRotation());
            // localStation.UseStation(localPlayer);
            // localStationPlayerPosition.SetPositionAndRotation(
            //     attachedPlatform.TransformPoint(attachedLocalPosition),
            //     ProjectOntoYPlane(attachedPlatform.rotation) * attachedLocalRotation);

            TeleportPlayerIntoStation(localPlayer.GetPosition(), localPlayer.GetRotation());

            // Vector3 stationPosition = localStationPlayerPosition.position;
            // Quaternion stationRotation = localStationPlayerPosition.rotation;
            // var origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);

            // Quaternion inverseRotation = Quaternion.Inverse(origin.rotation);
            // stationPositionLocalToOrigin = inverseRotation * (stationPosition - origin.position);
            // stationRotationLocalToOrigin = inverseRotation * stationRotation;

            // inverseRotation = Quaternion.Inverse(stationRotation);
            // originPositionLocalToStation = inverseRotation * (origin.position - stationPosition);
            // originRotationLocalToStation = inverseRotation * origin.rotation;
        }

        private void MoveLocalStationToAttachedLocation()
        {
            Vector3 position = attachedPlatform.TransformPoint(attachedLocalPosition);
            Quaternion rotation = ProjectOntoYPlane(attachedPlatform.rotation) * attachedLocalRotation;

            // Vector3 desiredOriginPosition = position + rotation * originPositionLocalToStation;
            // Quaternion desiredOriginRotation = rotation * originRotationLocalToStation;

            // var preOrigin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            localStationPlayerPosition.SetPositionAndRotation(position, rotation);
            // var origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            // qd.ShowForOneFrame(this, "moving station origin diff", (preOrigin.position - origin.position).ToString());
            // // This ^ always shows 0 0 0. That's a problem. That means moving the station player position does
            // // not instantly move the player, which makes trying to cancel out VRChat's stupid jitter very
            // // difficult if not impossible.

            // Vector3 positionOffset = origin.position - desiredOriginPosition;
            // Quaternion rotationOffset = Quaternion.Inverse(origin.rotation) * desiredOriginRotation;

            // localStationPlayerPosition.SetPositionAndRotation(
            //     position + positionOffset,
            //     rotation);

            // Quaternion inverseRotation = Quaternion.Inverse(origin.rotation);
            // Vector3 actualStationPositionLocalToOrigin = inverseRotation * (position - origin.position);
            // Quaternion actualStationRotationLocalToOrigin = inverseRotation * rotation;
            // Vector3 positionOffset = origin.rotation * (stationPositionLocalToOrigin - actualStationPositionLocalToOrigin);
            // Quaternion rotationOffset = Quaternion.Inverse(actualStationRotationLocalToOrigin) * stationRotationLocalToOrigin;

            // localStationPlayerPosition.SetPositionAndRotation(
            //     position + positionOffset,
            //     rotation);
            // // localStationPlayerPosition.SetPositionAndRotation(
            // //     attachedPlatform.TransformPoint(attachedLocalPosition) + positionOffset,
            // //     ProjectOntoYPlane(attachedPlatform.rotation) * attachedLocalRotation * rotationOffset);
        }

#if PLATFORM_ATTACHMENT_DEBUG
        private VRCPlayerApi.TrackingData PrintOriginDiffs(
            VRCPlayerApi.TrackingData originalOrigin,
            VRCPlayerApi.TrackingData prevOrigin,
            string actionName)
        {
            getTrackingDataSw.Start();
            var origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            getTrackingDataSw.Stop();
            Vector3 inducedMovement = origin.position - prevOrigin.position;
            Quaternion inducedRotation = Quaternion.Inverse(prevOrigin.rotation) * origin.rotation;
            Vector3 totalInducedMovement = origin.position - originalOrigin.position;
            Quaternion totalInducedRotation = Quaternion.Inverse(originalOrigin.rotation) * origin.rotation;
            qd.ShowForOneFrame(this, $"{actionName} induced movement", $"{inducedMovement:f3}, total: {totalInducedMovement:f3}");
            qd.ShowForOneFrame(this, $"{actionName} induced rotation", $"{inducedRotation.eulerAngles:f3}, total: {totalInducedRotation.eulerAngles:f3}");
            return origin;
        }
#endif

        // private VRCPlayerApi.TrackingData prevOrigin;
        // private VRCPlayerApi.TrackingData prevHead;
        // private Vector3 prevPosition;
        // private Quaternion prevRotation;

        private float inputMoveHorizontal;
        private float inputMoveVertical;
        private float inputLookHorizontal;

        public override void InputMoveHorizontal(float value, UdonInputEventArgs args)
        {
            inputMoveHorizontal = value;
        }

        public override void InputMoveVertical(float value, UdonInputEventArgs args)
        {
            inputMoveVertical = value;
        }

        public override void InputLookHorizontal(float value, UdonInputEventArgs args)
        {
            inputLookHorizontal = value;
        }

        // public override void InputLookVertical(float value, UdonInputEventArgs args)
        // {
        // }

        private void ApplyUserInput()
        {
            float deltaTime = Time.deltaTime;
            var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);

            Vector3 velocity = ProjectOntoYPlane(head.rotation) * new Vector3(
                inputMoveHorizontal * localPlayer.GetStrafeSpeed(),
                0f,
                inputMoveVertical * localPlayer.GetRunSpeed());

            Vector3 position = attachedPlatform.TransformPoint(attachedLocalPosition);
            Quaternion rotation = ProjectOntoYPlane(attachedPlatform.rotation) * attachedLocalRotation;

            position += velocity * deltaTime;
            rotation *= Quaternion.AngleAxis(deltaTime * inputLookHorizontal * 180f, Vector3.up);

            attachedLocalPosition = attachedPlatform.InverseTransformPoint(position);
            attachedLocalRotation = Quaternion.Inverse(ProjectOntoYPlane(attachedPlatform.rotation)) * rotation;

            // Doing this is good when in half body, but in full body it locks the avatar oddly, it does not
            // follow trackers anymore.
            // localPlayer.SetVelocity(velocity);
        }

        public void TeleportPlayerOutOfStation()
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] Manager  {nameof(TeleportPlayerOutOfStation)}");
#endif
            var headPre = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            localStation.ExitStation(localPlayer);
            var headPost = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            // Diffs when added to post result in pre.
            Vector3 posDiff = headPre.position - headPost.position;
            // Since both rotations are just around the Y axis, order happens to not matter.
            Quaternion rotDiff = ProjectOntoYPlane(headPre.rotation) * Quaternion.Inverse(ProjectOntoYPlane(headPost.rotation));
            localPlayerPosition = localPlayer.GetPosition();
            localPlayerRotation = localPlayer.GetRotation();
            localPlayerOrigin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            RoomAlignedTeleport(localPlayerPosition + posDiff, localPlayerRotation * rotDiff, lerpOnRemote: true);
        }

        public void TeleportPlayerIntoStation(Vector3 position, Quaternion rotation)
        {
#if PLATFORM_ATTACHMENT_DEBUG
            Debug.Log($"[PlatformAttachmentDebug] Manager  {nameof(TeleportPlayerIntoStation)}");
#endif
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
            funkyTpSw.Reset();
            funkyTpSw.Start();
            bool updateTiming = false;
#endif
#if PLATFORM_ATTACHMENT_DEBUG
            var originalOrigin = localPlayerOrigin;
            Vector3 positionErrorLastFrame = Vector3.zero;
            Quaternion rotationErrorLastFrame = Quaternion.identity;
#endif
            localPlayerPosition = localPlayer.GetPosition();
            localPlayerRotation = localPlayer.GetRotation();
            localPlayerOrigin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
            GetRoomAlignedTeleportTargetPosAndRot(position, rotation, out var desiredOriginPos, out var desiredOriginRot);
            // Only requires a single iteration 99.9% of the time. However when the head is tilted to the left
            // or right, when looking up and down there is a single frame at some threshold where it requires
            // multiple iterations to fully undo unintentional movement and rotation induced by entering the
            // station.
            // Requires 2 at <= 40 fps, 3 at 50 fps, 5 to 6 iterations at 60 fps, cannot test higher fps.
            // Each iteration takes a bit more than 1 ms on my machine with this current implementation.
            for (int i = 0; i < MaxTPIterations; i++)
            {
                // Teleports also make the player exist the station.
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                exitStationSw.Start();
                localStation.ExitStation(localPlayer);
                exitStationSw.Stop();
#endif
#if PLATFORM_ATTACHMENT_DEBUG
                var desiredOrigin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
#endif
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                tpSw.Start();
#endif
                localPlayer.TeleportTo(position, localPlayerRotation, VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, lerpOnRemote: false);
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                tpSw.Stop();
#endif
#if PLATFORM_ATTACHMENT_DEBUG
                var origin = PrintOriginDiffs(originalOrigin, desiredOrigin, "tp 1");
#else
#if PLATFORM_ATTACHMENT_STOPWATCH
                getTrackingDataSw.Start();
#endif
                var origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
#if PLATFORM_ATTACHMENT_STOPWATCH
                getTrackingDataSw.Stop();
#endif
#endif
                Vector3 posDiff = origin.position - desiredOriginPos;
                Quaternion rotDiff = Quaternion.Inverse(desiredOriginRot) * origin.rotation;
                Vector3 almostFinalPosition = position - posDiff;
                Quaternion finalRotation = localPlayerRotation * Quaternion.Inverse(rotDiff);
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                tpSw.Start();
#endif
                localPlayer.TeleportTo(almostFinalPosition, finalRotation, VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, lerpOnRemote: false);
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                tpSw.Stop();
#endif
#if PLATFORM_ATTACHMENT_DEBUG
                origin = PrintOriginDiffs(originalOrigin, origin, "tp 2");
#else
#if PLATFORM_ATTACHMENT_STOPWATCH
                getTrackingDataSw.Start();
#endif
                origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
#if PLATFORM_ATTACHMENT_STOPWATCH
                getTrackingDataSw.Stop();
#endif
#endif
                Vector3 posDiff2 = origin.position - desiredOriginPos;
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                tpSw.Start();
#endif
                localPlayer.TeleportTo(desiredOriginPos, desiredOriginRot, VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint, lerpOnRemote: false);
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                tpSw.Stop();
#endif
#if PLATFORM_ATTACHMENT_DEBUG
                origin = PrintOriginDiffs(originalOrigin, origin, "tp 3");
#endif
                localStationPlayerPosition.SetPositionAndRotation(almostFinalPosition - posDiff2, finalRotation);
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                useStationSw.Start();
#endif
                localStation.UseStation(localPlayer);
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                useStationSw.Stop();
#endif
#if PLATFORM_ATTACHMENT_DEBUG
                origin = PrintOriginDiffs(originalOrigin, origin, "station 3");
                positionErrorLastFrame = origin.position - desiredOriginPos;
                rotationErrorLastFrame = Quaternion.Inverse(desiredOriginRot) * origin.rotation;
                qd.ShowForOneFrame(this, $"position error", $"{positionErrorLastFrame:f3}");
                qd.ShowForOneFrame(this, $"rotation error", $"{rotationErrorLastFrame.eulerAngles:f3}");
                if (positionErrorLastFrame == Vector3.zero && rotationErrorLastFrame == Quaternion.identity)
                    break;
#else
#if PLATFORM_ATTACHMENT_STOPWATCH
                getTrackingDataSw.Start();
#endif
                origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);
#if PLATFORM_ATTACHMENT_STOPWATCH
                getTrackingDataSw.Stop();
#endif
                if ((origin.position - desiredOriginPos) == Vector3.zero
                    && (Quaternion.Inverse(desiredOriginRot) * origin.rotation) == Quaternion.identity)
                    break;
#endif
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
                funkyIterations = System.Math.Min(MaxTPIterations, i + 2);
                updateTiming = true;
#endif
            }
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
            funkyTpSw.Stop();
            if (updateTiming)
            {
                funkyTimingMs = funkyTpSw.Elapsed.TotalMilliseconds;
#endif
#if PLATFORM_ATTACHMENT_DEBUG
                positionErrorLastFunkyFrame = positionErrorLastFrame;
                rotationErrorLastFunkyFrame = rotationErrorLastFrame;
#endif
#if PLATFORM_ATTACHMENT_DEBUG || PLATFORM_ATTACHMENT_STOPWATCH
            }
            qd.ShowForOneFrame(this, "funkyIterations", $"{funkyIterations:d}");
            qd.ShowForOneFrame(this, "funkyTimingMs", $"{funkyTimingMs:f3}");
#endif
#if PLATFORM_ATTACHMENT_DEBUG
            qd.ShowForOneFrame(this, "funkyPositionErrorLastFrame", $"{positionErrorLastFunkyFrame:f3}");
            qd.ShowForOneFrame(this, "funkyRotationErrorLastFrame", $"{rotationErrorLastFunkyFrame.eulerAngles:f3}");
#endif
        }
    }
}
