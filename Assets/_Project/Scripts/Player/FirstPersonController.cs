using UnityEngine;

namespace YourFinalOrder.Player
{
    /// <summary>
    /// Локальное управление от первого лица: ходьба, бег с выносливостью, приседание, прыжок, обзор мышью.
    /// Включается только у владельца (см. NetworkPlayer).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FirstPersonController : MonoBehaviour
    {
        [Header("Движение")]
        public float walkSpeed = 3.2f;
        public float sprintSpeed = 5.8f;
        public float crouchSpeed = 1.5f;
        public float acceleration = 12f;
        public float gravity = -20f;
        public float jumpHeight = 0.8f;

        [Header("Приседание")]
        public float standHeight = 1.8f;
        public float crouchHeight = 1.1f;
        public float standCameraY = 1.6f;
        public float crouchCameraY = 0.95f;

        [Header("Выносливость")]
        public float maxStamina = 5f;
        public float staminaRegen = 0.8f;
        public float staminaRegenDelay = 1.2f;
        [Range(0f, 1f)] public float exhaustedRecover = 0.35f;

        [Header("Обзор")]
        public Transform cameraRoot;
        public float mouseSensitivity = 2f;
        public float maxPitch = 85f;

        public bool InputEnabled { get; set; } = true;
        public bool IsSprinting { get; private set; }
        public bool IsCrouching { get; private set; }
        public float Stamina01 => stamina / maxStamina;
        public float Pitch => pitch;

        CharacterController cc;
        Vector3 horizontalVelocity;
        float verticalVelocity;
        float pitch;
        float stamina;
        float regenTimer;
        bool exhausted;

        void Awake()
        {
            cc = GetComponent<CharacterController>();
            stamina = maxStamina;
        }

        void Update()
        {
            bool canControl = InputEnabled && Cursor.lockState == CursorLockMode.Locked;

            if (canControl) Look();
            Move(canControl);
        }

        void Look()
        {
            float mx = Input.GetAxis("Mouse X") * mouseSensitivity;
            float my = Input.GetAxis("Mouse Y") * mouseSensitivity;
            transform.Rotate(0f, mx, 0f);
            pitch = Mathf.Clamp(pitch - my, -maxPitch, maxPitch);
            if (cameraRoot != null) cameraRoot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void Move(bool canControl)
        {
            if (!cc.enabled) return;

            Vector2 input = Vector2.zero;
            if (canControl)
            {
                if (Input.GetKey(KeyCode.W)) input.y += 1f;
                if (Input.GetKey(KeyCode.S)) input.y -= 1f;
                if (Input.GetKey(KeyCode.D)) input.x += 1f;
                if (Input.GetKey(KeyCode.A)) input.x -= 1f;
                input = Vector2.ClampMagnitude(input, 1f);
            }

            bool wantCrouch = canControl && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.C));
            if (!wantCrouch && IsCrouching && HasCeilingAbove()) wantCrouch = true;
            IsCrouching = wantCrouch;

            bool wantSprint = canControl && Input.GetKey(KeyCode.LeftShift) && input.y > 0.1f && !IsCrouching;
            IsSprinting = wantSprint && !exhausted && stamina > 0f;

            UpdateStamina();
            UpdateCrouchShape();

            float speed = IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
            Vector3 target = (transform.right * input.x + transform.forward * input.y) * speed;
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, target, acceleration * Time.deltaTime);

            if (cc.isGrounded)
            {
                verticalVelocity = -2f;
                if (canControl && !IsCrouching && Input.GetKeyDown(KeyCode.Space))
                    verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
            }
            verticalVelocity += gravity * Time.deltaTime;

            cc.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
        }

        void UpdateStamina()
        {
            if (IsSprinting)
            {
                stamina = Mathf.Max(0f, stamina - Time.deltaTime);
                regenTimer = staminaRegenDelay;
                if (stamina <= 0f) exhausted = true;
            }
            else if (regenTimer > 0f)
            {
                regenTimer -= Time.deltaTime;
            }
            else
            {
                stamina = Mathf.Min(maxStamina, stamina + staminaRegen * Time.deltaTime);
                if (exhausted && Stamina01 >= exhaustedRecover) exhausted = false;
            }
        }

        void UpdateCrouchShape()
        {
            float targetHeight = IsCrouching ? crouchHeight : standHeight;
            cc.height = Mathf.MoveTowards(cc.height, targetHeight, 6f * Time.deltaTime);
            cc.center = Vector3.up * (cc.height / 2f);

            if (cameraRoot != null)
            {
                var p = cameraRoot.localPosition;
                p.y = Mathf.MoveTowards(p.y, IsCrouching ? crouchCameraY : standCameraY, 5f * Time.deltaTime);
                cameraRoot.localPosition = p;
            }
        }

        bool HasCeilingAbove()
        {
            var origin = transform.position + Vector3.up * (cc.height - cc.radius);
            return Physics.SphereCast(origin, cc.radius * 0.9f, Vector3.up, out _, standHeight - cc.height + 0.05f,
                ~0, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Мгновенно переместить игрока (респавн).</summary>
        public void ResetState(float yaw)
        {
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            pitch = 0f;
            if (cameraRoot != null) cameraRoot.localRotation = Quaternion.identity;
            horizontalVelocity = Vector3.zero;
            verticalVelocity = 0f;
            stamina = maxStamina;
            exhausted = false;
        }
    }
}
