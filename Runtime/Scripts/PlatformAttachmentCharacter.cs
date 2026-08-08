using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    [SingletonScript("bbe525fe8f53b070a9a6a76da1cf85ad")] // Runtime/Prefabs/PlatformAttachmentManager.prefab
    public class PlatformAttachmentCharacter : UdonSharpBehaviour
    {
#if PLATFORM_ATTACHMENT_DEBUG
        [HideInInspector][SerializeField][SingletonReference] private QuickDebugUI qd;
#endif
        [HideInInspector][SerializeField][SingletonReference] private PlatformAttachmentManager manager;
        [HideInInspector][SerializeField][SingletonReference] private UpdateManager updateManager;
        /// <summary>
        /// <para>Used by the <see cref="UpdateManager"/>.</para>
        /// </summary>
        [System.NonSerialized] public int customUpdateInternalIndex;
        public CharacterController characterController;
        public Transform characterTransform;
        public LayerMask playerCollisionMask;

        private Transform platform;
        private AttachablePlatform platformScript;

        private VRCPlayerApi localPlayer;
        private AttachedRemotePlayer localAttachedPlayerSync;
        private VRC.SDK3.Components.VRCStation localStation;
        private Transform localStationPlayerPosition;

        private const float GravityConstant = -9.81f;

        private Vector3 velocity;
        /// <summary>
        /// <para>Always around axis <see cref="Vector3.up"/>.</para>
        /// </summary>
        private float angularVelocityAngles;
        // // the current frame's velocity is 65%, the prev velocity is 35%. And it repeats like that
        // private const float AdditionalVelocityNewWeight = 0.35f;
        private bool isGrounded;
        private float airtime;
        private const float MinimumAirtimeBeforeDetaching = 1.25f;
        private const float MaxAngleForAirbornePlatformChecks = 30f * Mathf.Deg2Rad;
        private const float MaxDistanceForAirbornePlatformChecks = 32f;

        private Quaternion prevPlatformRotation;

        private Vector3 characterPositionLocalToPlatform;
        private Quaternion characterRotationLocalToPlatform;

        private Vector3 stationPositionLocalToCharacter;
        private Quaternion stationRotationLocalToCharacter;

        private Vector3 characterPositionBeforeMovementThisFrame;

        private void Start()
        {
            localPlayer = Networking.LocalPlayer;
        }

        public void SetLocalAttachedPlayerSync(AttachedRemotePlayer localAttachedPlayerSync)
        {
            this.localAttachedPlayerSync = localAttachedPlayerSync;
            localStation = localAttachedPlayerSync.station;
            localStationPlayerPosition = localAttachedPlayerSync.stationPlayerPosition;
        }

        public void Attach(AttachablePlatform platformScript)
        {
            Vector3 playerPosition = localPlayer.GetPosition();
            Quaternion playerRotation = localPlayer.GetRotation();
            platform = platformScript.transform;
            this.platformScript = platformScript;
            characterTransform.position = playerPosition;
            prevPlatformRotation = PlatformAttachmentManager.ProjectOntoYPlane(platform.rotation);
            velocity = localPlayer.GetVelocity();
            angularVelocityAngles = 0f;
            isGrounded = false;
            airtime = 0f;
            localStationPlayerPosition.SetPositionAndRotation(playerPosition, playerRotation);
            manager.UseLocalStation();

            characterPositionLocalToPlatform = platform.InverseTransformPoint(playerPosition);
            characterRotationLocalToPlatform = Quaternion.Inverse(PlatformAttachmentManager.ProjectOntoYPlane(platform.rotation)) * playerRotation;
            stationPositionLocalToCharacter = Quaternion.Inverse(playerRotation) * (localStationPlayerPosition.position - playerPosition);
            stationRotationLocalToCharacter = Quaternion.Inverse(playerRotation) * localStationPlayerPosition.rotation;

            inputJump = false;
            updateManager.Register(this);
        }

        private void SwitchAttachedPlatform(AttachablePlatform platformScript)
        {
            platform = platformScript.transform;
            this.platformScript = platformScript;
            prevPlatformRotation = PlatformAttachmentManager.ProjectOntoYPlane(platform.rotation);

            characterPositionLocalToPlatform = platform.InverseTransformPoint(characterTransform.position);
            characterRotationLocalToPlatform = Quaternion.Inverse(PlatformAttachmentManager.ProjectOntoYPlane(platform.rotation)) * characterTransform.rotation;

            manager.SwitchAttachedPlatform(platformScript);
        }

        private void Detach()
        {
            updateManager.Deregister(this);
            manager.TeleportPlayerOutOfStation();
            localPlayer.SetVelocity(velocity);
            manager.Detach();
        }

        public void UpdateController()
        {
            if (ShouldDetach())
            {
                Detach();
                return;
            }
            characterPositionBeforeMovementThisFrame = characterTransform.position;
            RespectPlatformMovement();
            RespectMovementInPlaySpace();
            RespectUserInput();
            ApplyMovementToStation();
        }

        private bool ShouldDetach()
        {
            float radius = LocalPlayerCapsule.GetRadius();
            Vector3 direction;
            float maxDistance;
            if (isGrounded)
            {
                airtime = 0f;
                direction = Vector3.down;
                maxDistance = radius + 1f;
            }
            else
            {
                airtime += Time.fixedDeltaTime;
                if (airtime <= MinimumAirtimeBeforeDetaching)
                    return false;
                direction = Vector3.RotateTowards(Vector3.down, velocity, MaxAngleForAirbornePlatformChecks, maxMagnitudeDelta: 0f);
                maxDistance = MaxDistanceForAirbornePlatformChecks;
            }

            if (!Physics.SphereCast(
                characterTransform.position + Vector3.up * (radius + 0.5f),
                radius * 0.9f,
                direction,
                out RaycastHit hit,
                maxDistance,
                manager.layersToAttachTo,
                QueryTriggerInteraction.Ignore))
            {
                return true;
            }
            Transform platform = hit.transform;
            if (platform == null) // null for VRChat internals.
                return true;
            AttachablePlatform platformScript = platform.GetComponent<AttachablePlatform>();
            if (platformScript == null)
                return true;
            if (platformScript != this.platformScript)
                SwitchAttachedPlatform(platformScript);
            return false;
        }

        private void RespectPlatformMovement()
        {
            if (!isGrounded)
                return;
            characterController.enabled = false;
            characterTransform.position = platform.TransformPoint(characterPositionLocalToPlatform);
            characterController.enabled = true;
            // Vector3 pre = characterTransform.position;
            // This might help with getting continuously moved when some collider intersects with the player
            // that previously wasn't - a collider moved into the player.
            // TODO: think about what variables should get modified after this call. Think about this more in general.
            characterController.Move(Vector3.down * 0.1f);
            // if (characterTransform.position != pre)
            //     Debug.Log($"<dlt> Moved {(characterTransform.position - pre).magnitude} | {characterTransform.position - pre}");
#if PLATFORM_ATTACHMENT_DEBUG
            qd.ShowForOneFrame(this, "platform velocity", ((characterTransform.position - characterPositionBeforeMovementThisFrame) / Time.fixedDeltaTime).ToString());
#endif

            Quaternion platformRotation = PlatformAttachmentManager.ProjectOntoYPlane(platform.rotation);
            Quaternion diff = Quaternion.Inverse(prevPlatformRotation) * platformRotation;
            prevPlatformRotation = platformRotation;

            diff.ToAngleAxis(out float angle, out Vector3 axis);
            if (axis.y < 0)
                angle = -angle;
            angularVelocityAngles = angle / Time.fixedDeltaTime;
        }

        private void RespectMovementInPlaySpace()
        {
            var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 characterPosition = characterTransform.position;
            Vector3 headPosition = head.position;
            headPosition.y = characterPosition.y;
            Vector3 fromCharacterToHead = headPosition - characterPosition;
            if (fromCharacterToHead.magnitude < 0.01f)
                return;

            characterController.Move(fromCharacterToHead);
            Vector3 movement = characterTransform.position - characterPosition;
            Vector3 localMovement = Quaternion.Inverse(PlatformAttachmentManager.ProjectOntoYPlane(platform.rotation) * characterRotationLocalToPlatform) * movement;

            characterPositionBeforeMovementThisFrame += movement;

            stationPositionLocalToCharacter.x -= localMovement.x;
            // stationPositionLocalToPlatform.y += movement.y;
            stationPositionLocalToCharacter.z -= localMovement.z;
        }

        private void RespectUserInput()
        {
            ProcessLookInput();
            ProcessMoveInput();
            inputJump = false;
        }

        private void ProcessLookInput()
        {
            // Using inputLookHorizontal directly here (and of course then not setting it to 0 in here as that
            // would be invalid) here caused unexplainably fast spinning with lower frame rates.
            characterRotationLocalToPlatform *= Quaternion.AngleAxis(accumulatedInputLookHorizontal * 90f * Time.fixedDeltaTime, Vector3.up);
            accumulatedInputLookHorizontal = 0f;
            // TODO: Impl properly.
        }

        private void ProcessMoveInput()
        {
            var head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Quaternion headRotation = PlatformAttachmentManager.ProjectOntoYPlane(head.rotation);
            Vector3 inputVelocity = headRotation * new Vector3(
                inputMoveHorizontal * localPlayer.GetStrafeSpeed(),
                0f,
                inputMoveVertical * localPlayer.GetRunSpeed());

            Vector3 newVelocity;
            if (!isGrounded)
            {
                newVelocity = new Vector3(
                    Mathf.Lerp(velocity.x, inputVelocity.x, Time.fixedDeltaTime), // TODO: Look at lerp smoothing from Freya.
                    velocity.y + GravityConstant * localPlayer.GetGravityStrength() * Time.fixedDeltaTime,
                    Mathf.Lerp(velocity.z, inputVelocity.z, Time.fixedDeltaTime));
            }
            else
            {
                if (inputJump)
                {
                    newVelocity = velocity;
                    newVelocity.y = localPlayer.GetJumpImpulse();
                }
                else if (Physics.Raycast(
                    characterTransform.position + Vector3.up * 0.1f,
                    Vector3.down,
                    // out RaycastHit hit,
                    0.1f + characterController.stepOffset + characterController.skinWidth + 0.05f,
                    playerCollisionMask,
                    QueryTriggerInteraction.Ignore))
                {
                    newVelocity = inputVelocity;
                    newVelocity.y = -10f;
                }
                else
                {
                    newVelocity = inputVelocity;
                    newVelocity.y = -0.1f;
                }
            }

            characterController.Move(newVelocity * Time.fixedDeltaTime);
            Vector3 actualVelocity = (characterTransform.position - characterPositionBeforeMovementThisFrame) / Time.fixedDeltaTime;
            velocity = actualVelocity.magnitude > newVelocity.magnitude
                ? actualVelocity.normalized * newVelocity.magnitude
                : actualVelocity;
            isGrounded = characterController.isGrounded;

            characterPositionLocalToPlatform = platform.InverseTransformPoint(characterTransform.position);

#if PLATFORM_ATTACHMENT_DEBUG
            qd.ShowForOneFrame(this, "isGrounded", isGrounded.ToString());
#endif

            // TODO: Apply angular velocity.
        }

        private void ApplyMovementToStation()
        {
            Quaternion characterRotation = PlatformAttachmentManager.ProjectOntoYPlane(platform.rotation) * characterRotationLocalToPlatform;
            localStationPlayerPosition.SetPositionAndRotation(
                characterTransform.position + characterRotation * stationPositionLocalToCharacter,
                characterRotation * stationRotationLocalToCharacter);
        }

        private bool inputJump;
        private float inputLookHorizontal;
        private float accumulatedInputLookHorizontal;
        private float inputMoveHorizontal;
        private float inputMoveVertical;

        public override void InputJump(bool value, UdonInputEventArgs args)
        {
            if (value)
                inputJump = true;
        }

        public void CustomUpdate()
        {
            accumulatedInputLookHorizontal += inputLookHorizontal;
        }

        public override void InputLookHorizontal(float value, UdonInputEventArgs args)
        {
            inputLookHorizontal = value;
        }

        public override void InputMoveHorizontal(float value, UdonInputEventArgs args)
        {
            inputMoveHorizontal = value;
        }

        public override void InputMoveVertical(float value, UdonInputEventArgs args)
        {
            inputMoveVertical = value;
        }
    }
}
