using UnityEngine;

namespace Gameplay.Vehicles
{
    /// <summary>
    /// Visual-only bicycle. It knows nothing about the player: a driver (e.g. <c>BicycleDriver</c>)
    /// pushes the pose and state in every frame. Wheel spin, lean and IK targets belong here.
    /// The prefab should have no Rigidbody and no colliders that can touch the player.
    /// </summary>
    public class BicycleRig : MonoBehaviour
    {
        /// <summary>Whether the bike was on the ground at the last update.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>Speed along the ground at the last update (m/s).</summary>
        public float Speed { get; private set; }

        /// <summary>
        /// Places the bicycle. <paramref name="position"/> is the ground contact point.
        /// </summary>
        public void SetPose(Vector3 position, Quaternion rotation, bool isGrounded, float speed)
        {
            transform.SetPositionAndRotation(position, rotation);
            IsGrounded = isGrounded;
            Speed = speed;
        }
    }
}
