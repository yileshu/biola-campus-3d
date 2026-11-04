using BiolaCampus.Core;
using BiolaCampus.Osm;
using UnityEngine;

namespace BiolaCampus.Player
{
    /// <summary>
    /// First-person walker (WASD + mouse look, Shift sprint, Space jump, gravity) with an optional
    /// drone/fly mode (F). Collides with the Cesium-generated tile MeshColliders and the boundary wall.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("Walk")]
        public float WalkSpeed = 4.2f;
        public float SprintSpeed = 8.5f;
        public float JumpHeight = 1.25f;
        public float Gravity = -19.6f;
        public float Acceleration = 14f;

        [Header("Fly / drone")]
        public float FlySpeed = 18f;
        public float FlySprintSpeed = 60f;
        public float MaxFlyAltitude = 450f;    // metres above the spawn ground

        [Header("Look")]
        public float LookSpeed = 2.0f;         // multiplied by the user's sensitivity setting
        public float EyeHeight = 1.62f;

        public Camera Camera { get; private set; }
        public bool Flying { get; private set; }
        public bool InputEnabled { get; set; } = true;
        public bool NearBoundary { get; private set; }
        public float GroundReferenceY { get; set; }
        public CampusMap Map { get; set; }

        public event System.Action<bool> FlyModeChanged;

        CharacterController _cc;
        Transform _head;
        float _pitch, _yaw;
        Vector3 _velocity;
        Vector3 _lastInside;
        float _noGroundTimer;

        public void Setup(Camera cam)
        {
            _cc = GetComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.35f;
            _cc.center = new Vector3(0, 0.9f, 0);
            _cc.stepOffset = 0.45f;
            _cc.slopeLimit = 55f;
            _cc.skinWidth = 0.05f;
            _cc.minMoveDistance = 0f;

            _head = new GameObject("Head").transform;
            _head.SetParent(transform, false);
            _head.localPosition = new Vector3(0, EyeHeight, 0);

            Camera = cam;
            cam.transform.SetParent(_head, false);
            cam.transform.localPosition = Vector3.zero;
            cam.transform.localRotation = Quaternion.identity;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 20000f;
            _yaw = transform.eulerAngles.y;
        }

        public void Teleport(Vector3 feetPosition, float yawDeg)
        {
            _cc.enabled = false;
            transform.position = feetPosition;
            _cc.enabled = true;
            _yaw = yawDeg; _pitch = 0f;
            _velocity = Vector3.zero;
            _lastInside = feetPosition;
            ApplyLook();
        }

        public void SetFlying(bool fly)
        {
            if (Flying == fly) return;
            Flying = fly;
            _velocity = Vector3.zero;
            FlyModeChanged?.Invoke(fly);
        }

        void Update()
        {
            if (_cc == null) return;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            bool locked = Cursor.lockState == CursorLockMode.Locked;

            if (InputEnabled && InputShim.Pressed(KeyCode.F)) SetFlying(!Flying);

            if (InputEnabled && locked)
            {
                Vector2 md = InputShim.MouseDelta * (LookSpeed * GameSettings.MouseSensitivity);
                _yaw += md.x;
                _pitch += GameSettings.InvertY ? md.y : -md.y;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
            }
            ApplyLook();
            if (Camera != null) Camera.fieldOfView = GameSettings.FieldOfView;

            Vector2 move = Vector2.zero;
            if (InputEnabled)
            {
                if (InputShim.Held(KeyCode.W) || InputShim.Held(KeyCode.UpArrow)) move.y += 1;
                if (InputShim.Held(KeyCode.S) || InputShim.Held(KeyCode.DownArrow)) move.y -= 1;
                if (InputShim.Held(KeyCode.D) || InputShim.Held(KeyCode.RightArrow)) move.x += 1;
                if (InputShim.Held(KeyCode.A) || InputShim.Held(KeyCode.LeftArrow)) move.x -= 1;
                move = Vector2.ClampMagnitude(move, 1f);
            }
            bool sprint = InputEnabled && (InputShim.Held(KeyCode.LeftShift) || InputShim.Held(KeyCode.RightShift));

            if (Flying) UpdateFly(move, sprint, dt);
            else UpdateWalk(move, sprint, dt);

            EnforceBoundary();
        }

        void ApplyLook()
        {
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_head != null) _head.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        void UpdateWalk(Vector2 move, bool sprint, float dt)
        {
            float speed = sprint ? SprintSpeed : WalkSpeed;
            Vector3 wish = (transform.right * move.x + transform.forward * move.y) * speed;
            Vector3 horiz = new Vector3(_velocity.x, 0, _velocity.z);
            horiz = Vector3.MoveTowards(horiz, wish, Acceleration * speed * dt);
            _velocity.x = horiz.x; _velocity.z = horiz.z;

            bool grounded = _cc.isGrounded;
            if (grounded && _velocity.y < 0) _velocity.y = -2f;
            if (grounded && InputEnabled && InputShim.Pressed(KeyCode.Space))
                _velocity.y = Mathf.Sqrt(JumpHeight * -2f * Gravity);

            // Tiles stream in: if there is no collider anywhere below us yet, hover instead of falling forever.
            bool groundBelow = grounded || Physics.Raycast(transform.position + Vector3.up * 0.5f, Vector3.down, 1500f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (groundBelow) { _velocity.y += Gravity * dt; _noGroundTimer = 0f; }
            else { _velocity.y = 0f; _noGroundTimer += dt; }

            _cc.Move(_velocity * dt);

            // Fell through a hole while a tile was refining: pop back up onto whatever is there now.
            if (transform.position.y < GroundReferenceY - 120f) RecoverToGround();
        }

        void UpdateFly(Vector2 move, bool sprint, float dt)
        {
            float speed = sprint ? FlySprintSpeed : FlySpeed;
            float scroll = InputEnabled ? InputShim.Scroll : 0f;
            if (Mathf.Abs(scroll) > 0.01f) FlySpeed = Mathf.Clamp(FlySpeed * (1f + 0.15f * scroll), 4f, 120f);

            float vertical = 0f;
            if (InputEnabled && (InputShim.Held(KeyCode.Space) || InputShim.Held(KeyCode.E))) vertical += 1f;
            if (InputEnabled && (InputShim.Held(KeyCode.LeftControl) || InputShim.Held(KeyCode.Q) || InputShim.Held(KeyCode.C))) vertical -= 1f;

            Vector3 fwd = Camera != null ? Camera.transform.forward : transform.forward;
            Vector3 wish = (Camera != null ? Camera.transform.right : transform.right) * move.x + fwd * move.y + Vector3.up * vertical;
            _velocity = Vector3.Lerp(_velocity, wish * speed, 1f - Mathf.Exp(-6f * dt));
            _cc.Move(_velocity * dt);

            float ceiling = GroundReferenceY + MaxFlyAltitude;
            if (transform.position.y > ceiling)
            {
                _cc.enabled = false;
                transform.position = new Vector3(transform.position.x, ceiling, transform.position.z);
                _cc.enabled = true;
            }
        }

        void EnforceBoundary()
        {
            if (Map == null || !Map.HasBoundary) return;
            Vector2 p = CampusMap.XZ(transform.position);
            if (Map.IsInsideCampus(p))
            {
                _lastInside = transform.position;
                NearBoundary = Map.DistanceToBoundary(p) < 4f;
            }
            else
            {
                // Safety net in case a wall collider was skipped (e.g. teleport, very high speed).
                _cc.enabled = false;
                transform.position = _lastInside;
                _cc.enabled = true;
                _velocity = Vector3.zero;
                NearBoundary = true;
            }
        }

        public void RecoverToGround()
        {
            Vector3 p = transform.position;
            if (Physics.Raycast(new Vector3(p.x, GroundReferenceY + 800f, p.z), Vector3.down, out var hit, 2000f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                Teleport(hit.point + Vector3.up * 0.2f, _yaw);
            else
                Teleport(new Vector3(p.x, GroundReferenceY + 2f, p.z), _yaw);
        }

        public float Yaw => _yaw;
        public bool WaitingForGround => !Flying && _noGroundTimer > 0.5f;
    }
}
