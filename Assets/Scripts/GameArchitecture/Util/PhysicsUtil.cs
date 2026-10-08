using UnityEngine;

namespace GameArchitecture.Util
{
    public static class PhysicsUtil
    {
        // Method for moving rigidbody to position
        //  with its velocity
        public static Vector3 MoveToPosition(Rigidbody rb, Vector3 targetPosition, float speed)
        {
            // Calculate the direction to the target position
            var position = rb.position;
            Vector3 direction = (targetPosition - position).normalized;
            
            // Fuck you ChatGPT I'm smarter 8====>
            var distance = Vector3.Distance(position, targetPosition);
            var maxDistance = 1f;

            var val = 1 - distance / maxDistance;
            var lerpValue = Mathf.Clamp01(1 - val * val * val * val);

            // Calculate the desired velocity
            Vector3 desiredVelocity = direction * (speed * lerpValue);

            // Apply the desired velocity to the Rigidbody's velocity
            rb.linearVelocity = desiredVelocity;
            return rb.position;
        }
    }
}