/*
Teleports the player using GetPosition and GetRotation as the basis for
the interface, but does all the math to teleport by TrackingData origin
under the hood correctly. This gives it a solid, reliable teleport suitable
for seamless teleportation use cases, while giving you an interface that
still has an easy to use pivot point and forward axis.

Copyright (c) 2023 @Phasedragon on GitHub
Additional help by @Nestorboy

Permission is hereby granted, free of charge, to any person obtaining
a copy of this software and associated documentation files (the
"Software"), to deal in the Software without restriction, including
without limitation the rights to use, copy, modify, merge, publish,
distribute, sublicense, and/or sell copies of the Software, and to
permit persons to whom the Software is furnished to do so, subject to
the following conditions:

The above copyright notice and this permission notice shall be
included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

// Notes by JanSharp:
// Modified it slightly, most notably just commenting out the projecting of the rotation onto the y plane,
// as that is not needed for my use case here.
//
// Additional note, I'm of the opinion that you cannot put a license onto this function.
// It is literally just getting some Vector3s and Quaternions, doing a tiny amount of math and calling a
// teleport function. I've done that dozens of times throughout my time with Unity.
// To me this is more like the MIT license being used as a patent. I'm no lawyer but this just doesn't make
// sense to me. If I were to publish a function like this and I wanted to put a license on it so people who
// treat code without a license as all rights reserved can be calm, I'd put it under The Unlicense. Because
// let's be honest, if I knew that AlignRoomWithSpawnPoint was using the tracking data origin as a reference
// point, I'd have been able to write this function no problem.
// But now that I've learned this knowledge by reading MIT licensed code, it feels as though me knowing that
// room aligned teleports use the origin as a reference is third party licensed knowledge. And that's just
// not how licenses work.
// However but, and here's the thing, if I were to just apply this knowledge - which is abstract, it is not
// literal code - to write my own teleport function, guess what the code I would write would look like...
// That's right! It would be the exact same code, maybe just rearranged ever so slightly.
// And thus even if I did that one could look at it and make the argument that I just took MIT licensed code
// and removed the license from it, which would be not adhering to the license.
// And that's so stupid. You can't put an MIT license on an idea or knowledge. That's a patent, not a license.
// But here I am, including the license notice at the top anyway. Why? Because just in case somebody would
// complain, I do not want to deal with arguing with them. So this makes it impossible for them to argue that
// I did not adhere to the license. Because I did, even though I am of the opinion that I would not have to.
// Lastly if the goal was just to have people credit where they've gotten the function/knowledge from, a
// simple comment in the function stating who wrote it would have been sufficient.
// People who want to give credit will give credit anyway - see how I've linked the github gist in the xml
// annotation for example - and people who don't care about giving credit aren't going to adhere to the MIT
// license either.
// The positive thing here at least is that it is the MIT license, so there's no downside to me just including
// it here even if I think I wouldn't have to. If it was like gpl v3 or something it'd be a lot more annoying,
// because at that point I couldn't just include it, I'd instead be forced to make the argument that the
// license application is invalid in its entirety if I wanted to publish my code under MIT.
// This is what I get for taking licensing seriously. Which is again why The Unlicense would be a better
// choice here as it would make the life of those taking licensing seriously easier. Those who don't care
// don't care either way.

using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
#if !UNITY_EDITOR
using VRC.SDKBase;
#endif

namespace JanSharp
{
    public partial class PlatformAttachmentManager : UdonSharpBehaviour
    {
        /// <summary>
        /// <para>See: https://gist.github.com/Phasedragon/5b76edfb8723b6bc4a49cd43adde5d3d</para>
        /// </summary>
        /// <param name="teleportRot">Gets projected onto the Y plane.</param>
        public void RoomAlignedTeleport(Vector3 teleportPos, Quaternion teleportRot, bool lerpOnRemote)
        {
            RoomAlignedTeleport(teleportPos, teleportRot, lerpOnRemote, localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin));
        }

        public void GetTargetPosAndRot(Vector3 teleportPos, Quaternion teleportRot, out Vector3 targetPos, out Quaternion targetRot)
        {
            // This is absolutely not how you are supposed to use euler angles. Converting a quaternion to
            // euler angles, taking some component of that and then converting that back to a quaternion is
            // asking for trouble, and that is exactly what is happening here. However through some miracle
            // this case actually behaves correctly, and I (JanSharp) believe that it's related to the order
            // that the euler axis get processed by Unity. Supposedly it is YXZ around local axis and ZXY
            // around world axis. So maybe these functions here use YXZ and that's why it works.
            teleportRot = Quaternion.Euler(0, teleportRot.eulerAngles.y, 0);

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
            targetPos = teleportPos + teleportRot * invPlayerRot * offsetPos;
            targetRot = teleportRot * offsetRot;
        }

        /// <summary>
        /// <para>See: https://gist.github.com/Phasedragon/5b76edfb8723b6bc4a49cd43adde5d3d</para>
        /// </summary>
        /// <param name="teleportRot">Gets projected onto the Y plane.</param>
        public void RoomAlignedTeleport(Vector3 teleportPos, Quaternion teleportRot, bool lerpOnRemote, VRCPlayerApi.TrackingData origin)
        {
#if UNITY_EDITOR
            // Skip process and Exit early for ClientSim since there is no play space to orient.
            localPlayer.TeleportTo(teleportPos, teleportRot);
#else
            // This is absolutely not how you are supposed to use euler angles. Converting a quaternion to
            // euler angles, taking some component of that and then converting that back to a quaternion is
            // asking for trouble, and that is exactly what is happening here. However through some miracle
            // this case actually behaves correctly, and I (JanSharp) believe that it's related to the order
            // that the euler axis get processed by Unity. Supposedly it is YXZ around local axis and ZXY
            // around world axis. So maybe these functions here use YXZ and that's why it works.
            teleportRot = Quaternion.Euler(0, teleportRot.eulerAngles.y, 0);

            // Get player pos/rot
            Vector3 playerPos = localPlayerPosition;
            Quaternion invPlayerRot = Quaternion.Inverse(localPlayer.GetRotation());

            // Get origin pos/rot
            // VRCPlayerApi.TrackingData origin = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Origin);

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
