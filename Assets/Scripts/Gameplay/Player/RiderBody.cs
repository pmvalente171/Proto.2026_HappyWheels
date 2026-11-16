using System.Collections.Generic;
using Gameplay.Level;
using UnityEngine;

namespace Gameplay.Player
{
    /// <summary>
    /// A rider made of boxes, seated on the bicycle rig. While riding it's just visuals (no physics, no colliders).
    /// <see cref="Ragdoll"/> detaches it and turns it into a jointed ragdoll that keeps the bike's momentum.
    /// Positions are in the rig's local space (metres, Z forward), matching the default bicycle model.
    /// </summary>
    public class RiderBody
    {
        private static readonly Color Skin = new(0.95f, 0.75f, 0.6f);
        private static readonly Color Shirt = new(1f, 0.5f, 0.1f);   // delivery-uniform orange
        private static readonly Color Pants = new(0.2f, 0.25f, 0.4f);
        private static readonly Color Helmet = new(0.15f, 0.15f, 0.15f);

        private class Part
        {
            public GameObject Go;
            public float Mass;
            public Part Parent;
            public Vector3 JointLocal; // joint position, in the rider root's local space
        }

        private readonly List<Part> _parts = new();
        private readonly GameObject _root;

        /// <summary>Builds a seated rider as a child of <paramref name="rig"/>.</summary>
        public RiderBody(Transform rig)
        {
            _root = new GameObject("Rider");
            _root.transform.SetParent(rig, false);

            Vector3 hipL = new(-0.1f, 0.98f, -0.3f), hipR = new(0.1f, 0.98f, -0.3f);
            Vector3 neck = new(0f, 1.52f, -0.08f);
            Vector3 shoulderL = new(-0.2f, 1.47f, -0.1f), shoulderR = new(0.2f, 1.47f, -0.1f);

            Part hips = Box("Hips", new Vector3(0f, 1f, -0.3f), Quaternion.identity, new Vector3(0.34f, 0.18f, 0.24f), Pants, 10f, null, Vector3.zero);
            Part torso = Limb("Torso", new Vector3(0f, 1.05f, -0.3f), neck, new Vector3(0.38f, 0.22f), Shirt, 15f, hips);
            Head(new Vector3(0f, 1.68f, -0.02f), torso, neck);
            Limb("Arm L", shoulderL, new Vector3(-0.33f, 1.1f, 0.5f), new Vector3(0.1f, 0.1f), Shirt, 3f, torso);
            Limb("Arm R", shoulderR, new Vector3(0.33f, 1.1f, 0.5f), new Vector3(0.1f, 0.1f), Shirt, 3f, torso);
            _legL = Limb("Leg L", hipL, new Vector3(-0.13f, 0.35f, 0.05f), new Vector3(0.13f, 0.13f), Pants, 6f, hips);
            _legR = Limb("Leg R", hipR, new Vector3(0.13f, 0.35f, 0.05f), new Vector3(0.13f, 0.13f), Pants, 6f, hips);
            _torso = torso;
            _torsoRestRotation = torso.Go.transform.localRotation;

            SetPhysics(false);
        }

        private readonly Part _legL, _legR, _torso;
        private readonly Quaternion _torsoRestRotation;
        private bool _ragdolled;

        /// <summary>The torso while ragdolled (e.g. for a camera to follow), otherwise null.</summary>
        public Transform RagdollTorso { get; private set; }

        /// <summary>
        /// While seated: points each leg from its hip to a foot position (rig space), so the legs follow the pedals,
        /// and sways the torso a little with each stroke (<paramref name="sway"/> -1…1).
        /// </summary>
        public void SetPose(Vector3 leftFoot, Vector3 rightFoot, float sway)
        {
            if (_ragdolled || !_root)
                return;

            PointLimb(_legL, leftFoot);
            PointLimb(_legR, rightFoot);
            _torso.Go.transform.localRotation = Quaternion.AngleAxis(sway * 4f, Vector3.forward) * _torsoRestRotation;
        }

        private static void PointLimb(Part limb, Vector3 end)
        {
            Vector3 start = limb.JointLocal;
            Vector3 direction = end - start;
            if (direction.sqrMagnitude < 0.0001f)
                return;

            Transform t = limb.Go.transform;
            t.SetLocalPositionAndRotation((start + end) / 2f, Quaternion.LookRotation(direction, Vector3.back));
            Vector3 scale = t.localScale;
            scale.z = direction.magnitude;
            t.localScale = scale;
        }

        /// <summary>Detaches the rider and turns it into a ragdoll moving at <paramref name="velocity"/>.</summary>
        public void Ragdoll(Vector3 velocity)
        {
            _ragdolled = true;
            _root.transform.SetParent(null, true);
            SetPhysics(true);

            foreach (Part part in _parts)
            {
                var body = part.Go.AddComponent<Rigidbody>();
                body.mass = part.Mass;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }

            foreach (Part part in _parts)
            {
                var body = part.Go.GetComponent<Rigidbody>();
                if (part.Parent != null)
                {
                    var joint = part.Go.AddComponent<CharacterJoint>();
                    joint.connectedBody = part.Parent.Go.GetComponent<Rigidbody>();
                    joint.anchor = part.Go.transform.InverseTransformPoint(_root.transform.TransformPoint(part.JointLocal));
                    joint.enableProjection = true;
                    joint.swing1Limit = new SoftJointLimit { limit = 60f };
                    joint.swing2Limit = new SoftJointLimit { limit = 60f };
                    joint.lowTwistLimit = new SoftJointLimit { limit = -30f };
                    joint.highTwistLimit = new SoftJointLimit { limit = 30f };
                }

                // Keep the bike's momentum, plus a little pop so the rider goes over the handlebars
                body.linearVelocity = velocity + Vector3.up * 2f;
                body.angularVelocity = Random.insideUnitSphere * 3f;
            }

            RagdollTorso = _parts[1].Go.transform;
        }

        /// <summary>Removes the rider (seated or ragdolled).</summary>
        public void Destroy()
        {
            if (_root)
                Object.Destroy(_root);
        }

        private void SetPhysics(bool on)
        {
            foreach (Part part in _parts)
                if (part.Go.TryGetComponent(out Collider collider))
                    collider.enabled = on;
        }

        #region Building

        private Part Limb(string name, Vector3 from, Vector3 to, Vector2 thickness, Color color, float mass, Part parent)
        {
            Vector3 direction = to - from;
            var rotation = Quaternion.LookRotation(direction, Vector3.back); // X stays sideways for up/down limbs
            return Box(name, (from + to) / 2f, rotation, new Vector3(thickness.x, thickness.y, direction.magnitude),
                color, mass, parent, from);
        }

        private Part Head(Vector3 centre, Part torso, Vector3 neck)
        {
            Part head = Add(PrimitiveType.Sphere, "Head", centre, Quaternion.identity, Vector3.one * 0.26f, Skin, 5f, torso, neck);
            // Helmet: visual only, a flattened sphere on top of the head
            GameObject helmet = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(helmet.GetComponent<Collider>());
            helmet.name = "Helmet";
            helmet.transform.SetParent(head.Go.transform, false);
            helmet.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            helmet.transform.localScale = new Vector3(1.1f, 0.7f, 1.15f);
            helmet.GetComponent<Renderer>().sharedMaterial = ColorMaterials.Lit(Helmet);
            return head;
        }

        private Part Box(string name, Vector3 centre, Quaternion rotation, Vector3 size, Color color, float mass, Part parent, Vector3 joint) =>
            Add(PrimitiveType.Cube, name, centre, rotation, size, color, mass, parent, joint);

        private Part Add(PrimitiveType type, string name, Vector3 centre, Quaternion rotation, Vector3 size,
            Color color, float mass, Part parent, Vector3 joint)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(_root.transform, false);
            go.transform.SetLocalPositionAndRotation(centre, rotation);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = ColorMaterials.Lit(color);

            var part = new Part { Go = go, Mass = mass, Parent = parent, JointLocal = joint };
            _parts.Add(part);
            return part;
        }

        #endregion
    }
}
