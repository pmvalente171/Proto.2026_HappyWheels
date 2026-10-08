using System;
using Unity.VisualScripting;
using UnityEngine;

namespace Gameplay.Player
{
    [RequireComponent(typeof(Rigidbody))]
    public class MovementComponent : MonoBehaviour
    {
        [SerializeField] private InputBootstrap input;
        [Tooltip("Defaults to the main camera if not set.")]
        [SerializeField] private Transform cameraTransform;
        
        [Space]
        [SerializeField] private float torqueStrength = 10f;
        
        // Physiscs
        private Rigidbody _rigidbody;
        
        // Input
        private InputBindingScope _bindings;
        private Vector3 _wishDirection;

        public Vector3 WishDirection
        {
            get => _wishDirection;
            set => _wishDirection = value.normalized;
        }
        
        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            
            // If the camera transform is not set, default to the main camera
            if (!cameraTransform && Camera.main) 
                cameraTransform = Camera.main.transform;
        }

        private void OnEnable()
        {
            var actions = input.Actions.Player;
            _bindings = new InputBindingScope();
        }
        
        private void OnDisable()
        {
            _bindings?.Dispose();
            _bindings = null;
        }

        private void Update()
        {
            WishDirection = input.Actions.Player.Move.ReadValue<Vector2>();
        }

        private void RollTowardDirection(Vector3 wishDirection)
        {
            Vector3 move = cameraTransform.right * wishDirection.x + cameraTransform.forward * wishDirection.y;
            _rigidbody.AddTorque(Vector3.Cross(Vector3.up, move) * torqueStrength, ForceMode.Acceleration);
        }

        private void FixedUpdate()
        {
            RollTowardDirection(WishDirection);
        }
    }
}