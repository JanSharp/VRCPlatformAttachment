using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("bbe525fe8f53b070a9a6a76da1cf85ad")] // Runtime/Prefabs/PlatformAttachmentManager.prefab
    public class PlatformAttachmentManager : UdonSharpBehaviour
    {
        public LayerMask layersToAttachTo;
        [Header("Internal")]
        public Transform naturalGripPreventionCollider;

        private VRCPlayerApi localPlayer;
        private AttachedRemotePlayer localAttachedPlayerSync;
        /// <summary>
        /// <para>Set at the beginning of <see cref="OnTrulyPostLateUpdate"/>.</para>
        /// </summary>
        private Vector3 localPlayerPosition;

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

        private bool immobilized;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
        }

        public void SetLocalAttachedPlayerSync(AttachedRemotePlayer localAttachedPlayerSync)
        {
            this.localAttachedPlayerSync = localAttachedPlayerSync;
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
            prevPlayerPos = localPlayer.GetPosition();
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
            immobilized = false;
            localPlayer.Immobilize(immobilized);
            if (localAttachedPlayerSync != null)
                localAttachedPlayerSync.StopSyncLoop();
        }

        private void ApplyPlatformMovement()
        {
            Vector3 positionDiff = prevPlatform.position + prevPlatform.TransformDirection(prevLocalPos) - prevPlayerPos;
            Quaternion platformRotation = prevPlatform.rotation;
            Quaternion rotationDiff = ProjectOntoYPlane(Quaternion.Inverse(prevPlatformRotation) * platformRotation);
            // TeleportPlayerAlignedCorrectiveHead(localPlayerPosition + positionDiff, rotationDiff, lerpOnRemote: true);
            localAttachedPlayerSync.TeleportPlayer(localPlayerPosition + positionDiff, localPlayer.GetRotation() * rotationDiff);
            // RoomAlignedTeleport(localPlayerPosition + positionDiff, localPlayer.GetRotation() * rotationDiff, lerpOnRemote: true);

            prevPlayerPos = localPlayer.GetPosition();
            additionalVelocity = (positionDiff / Time.deltaTime) * AdditionalVelocityNewWeight + (additionalVelocity * (1f - AdditionalVelocityNewWeight));
            prevLocalPos = prevPlatform.InverseTransformDirection(prevPlayerPos - prevPlatform.position);
            prevPlatformRotation = platformRotation;

            immobilized = !immobilized;
            // localPlayer.Immobilize(immobilized);
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

        private void TeleportPlayerAlignedCorrectiveHead(Vector3 teleportPosition, Quaternion rotationDiff, bool lerpOnRemote)
        {
            Quaternion playerRotation = localPlayer.GetRotation();
            Quaternion preHeadRotation = ProjectOntoYPlane(localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation);
            localPlayer.TeleportTo(teleportPosition, playerRotation, VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, lerpOnRemote);
            Quaternion postHeadRotation = ProjectOntoYPlane(localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).rotation);
            Quaternion headRotationOffset = Quaternion.Inverse(postHeadRotation) * preHeadRotation;
            localPlayer.TeleportTo(teleportPosition, headRotationOffset * playerRotation * rotationDiff, VRC_SceneDescriptor.SpawnOrientation.AlignPlayerWithSpawnPoint, lerpOnRemote);
        }

        /// <summary>
        /// <para>See: https://gist.github.com/Phasedragon/5b76edfb8723b6bc4a49cd43adde5d3d</para>
        /// </summary>
        /// <param name="teleportRot">Gets projected onto the Y plane.</param>
        public void RoomAlignedTeleport(Vector3 teleportPos, Quaternion teleportRot, bool lerpOnRemote)
        {
#if UNITY_EDITOR
            // Skip process and Exit early for ClientSim since there is no play space to orient.
            localPlayer.TeleportTo(teleportPos, teleportRot);
#else
            // // This is absolutely not how you are supposed to use euler angles. Converting a quaternion to
            // // euler angles, taking some component of that and then converting that back to a quaternion is
            // // asking for trouble, and that is exactly what is happening here. However through some miracle
            // // this case actually behaves correctly, and I (JanSharp) believe that it's related to the order
            // // that the euler axis get processed by Unity. Supposedly it is YXZ around local axis and ZXY
            // // around world axis. So maybe these functions here use YXZ and that's why it works.
            // teleportRot = Quaternion.Euler(0, teleportRot.eulerAngles.y, 0);

            // Get player pos/rot
            Vector3 playerPos = localPlayerPosition;
            Quaternion invPlayerRot = Quaternion.Inverse(localPlayer.GetRotation());

            // Get origin pos/rot
            VRCPlayerApi.TrackingData origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);

            // Subtract player from origin in order to get the offset from the player to the origin
            // offset = origin - player
            Vector3 offsetPos = origin.position - playerPos;
            Quaternion offsetRot = invPlayerRot * origin.rotation;

            // Add the offset onto the destination in order to construct a pos/rot of where your origin would be in order to put the player at the destination
            // target = destination + offset
            localPlayer.TeleportTo(
                teleportPos + teleportRot * invPlayerRot * offsetPos,
                teleportRot * offsetRot,
                VRC_SceneDescriptor.SpawnOrientation.AlignRoomWithSpawnPoint,
                lerpOnRemote);
#endif
        }
    }
}
