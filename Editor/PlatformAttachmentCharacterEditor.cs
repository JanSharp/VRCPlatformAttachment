using UnityEditor;
using UnityEngine;

namespace JanSharp
{
    public static class PlatformAttachmentCharacterOnBuild
    {
        [OrderedInitializeOnLoad]
        private static void OnAssemblyLoad()
        {
            OnBuildUtil.RegisterType<PlatformAttachmentCharacter>(OnBuild);
        }

        private static bool OnBuild(PlatformAttachmentCharacter character)
        {
            int layer = LayerMask.NameToLayer("PlayerLocal");
            if (layer == -1)
            {
                Debug.LogError("[PlatformAttachment] VRChat's layers must be configured, missing 'PlayerLocal' layer. "
                    + "Use the VRChat SDK control panel, before uploading a world it prompts for setting up project settings.");
                return false;
            }

            int playerLocalCollisionMask = 0;
            for (int i = 0; i < 32; i++)
                if (!Physics.GetIgnoreLayerCollision(layer, i))
                    playerLocalCollisionMask |= 1 << i;

            SerializedObject so = new(character);
            so.FindProperty(nameof(PlatformAttachmentCharacter.playerCollisionMask)).intValue = playerLocalCollisionMask;
            so.ApplyModifiedProperties();

            return true;
        }
    }
}
