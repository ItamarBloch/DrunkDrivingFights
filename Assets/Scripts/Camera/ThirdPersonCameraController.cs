using UnityEngine;
using Mirror;
using Unity.Cinemachine;

/// <summary>
/// Multiplayer-ready 3rd person orbit camera using Cinemachine 3.
/// Attach to your Car prefab — creates Camera + Cinemachine rig at runtime.
/// Only the local player's camera activates.
///
/// Components created programmatically:
///   - Camera + CinemachineBrain (the real camera)
///   - CinemachineCamera (virtual camera)
///   - CinemachineOrbitalFollow (orbit positioning — mouse driven)
///   - CinemachineInputAxisController (auto-connects mouse to orbit axes)
///   - CinemachineRotationComposer (smooth look-at)
///   - CinemachineDeoccluder (wall/obstacle collision)
///
/// Controls (handled by CinemachineInputAxisController):
///   - Mouse X/Y: Orbit around the car
///   - Recentering: Auto-returns behind car when idle
///
/// Requirements:
///   - com.unity.cinemachine (v3.x, ships with Unity 6)
///   - com.unity.inputsystem
///   - Mirror
/// </summary>
public class ThirdPersonCameraController : NetworkBehaviour
{
    // ──────────────────────────────────────────────
    //  ORBIT SETTINGS
    // ──────────────────────────────────────────────

    [Header("Orbit")]
    [Tooltip("Distance from the car")]
    [SerializeField] private float orbitRadius = 8f;

    [Tooltip("Offset above the car pivot that the orbit centers on")]
    [SerializeField] private Vector3 targetOffset = new Vector3(0f, 2f, 0f);

    [Header("Recentering")]
    [Tooltip("Camera auto-returns behind the car when mouse is idle")]
    [SerializeField] private bool enableRecentering = true;

    [Tooltip("Seconds of idle before recentering starts")]
    [SerializeField] private float recenterWait = 1.5f;

    [Tooltip("Time in seconds to recenter")]
    [SerializeField] private float recenterTime = 1.0f;

    [Header("Vertical Limits")]
    [Tooltip("Minimum vertical angle (looking down)")]
    [SerializeField] private float minVerticalAngle = -20f;

    [Tooltip("Maximum vertical angle (looking up)")]
    [SerializeField] private float maxVerticalAngle = 50f;

    [Header("Damping")]
    [Tooltip("Position damping — smaller = more responsive, larger = smoother")]
    [SerializeField] private Vector3 positionDamping = new Vector3(1f, 1f, 1f);

    [Header("Look At")]
    [Tooltip("Offset above the car that the camera looks at")]
    [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Collision")]
    [Tooltip("Enable obstacle avoidance")]
    [SerializeField] private bool enableCollision = true;

    [Tooltip("Layers the camera collides with")]
    [SerializeField] private LayerMask collisionLayers = ~0;

    [Tooltip("Tag to ignore for collision (set to your car's tag)")]
    [SerializeField] private string ignoreCollisionTag = "";

    [Header("Speed Effects")]
    [SerializeField] private bool enableSpeedFOV = true;
    [SerializeField] private float baseFOV = 60f;
    [SerializeField] private float maxFOVBoost = 15f;
    [SerializeField] private float referenceSpeed = 40f;
    [SerializeField] private float fovSmoothSpeed = 5f;

    [Header("Cursor")]
    [SerializeField] private bool lockCursor = true;

    // ──────────────────────────────────────────────
    //  RUNTIME REFERENCES
    // ──────────────────────────────────────────────

    private GameObject cameraGO;
    private Camera playerCamera;
    private GameObject vcamGO;
    private CinemachineCamera cinemachineCamera;
    private CinemachineOrbitalFollow orbitalFollow;

    private Rigidbody carRigidbody;
    private float currentFOV;
    private bool cameraCreated;

    // ──────────────────────────────────────────────
    //  PUBLIC API
    // ──────────────────────────────────────────────

    public Camera Camera => playerCamera;

    // ──────────────────────────────────────────────
    //  MIRROR LIFECYCLE
    // ──────────────────────────────────────────────

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        CreateCameraRig();
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();
        DestroyCameraRig();
    }

    // ──────────────────────────────────────────────
    //  CAMERA RIG CREATION
    // ──────────────────────────────────────────────

    private void CreateCameraRig()
    {
        if (cameraCreated) return;

        carRigidbody = GetComponent<Rigidbody>();

        // ── Kill all existing cameras ──
        DisableAllExistingCameras();

        // ── 1. Real Camera + CinemachineBrain ──
        cameraGO = new GameObject($"PlayerCamera [netId={netId}]");

        playerCamera = cameraGO.AddComponent<Camera>();
        playerCamera.fieldOfView = baseFOV;
        playerCamera.nearClipPlane = 0.1f;
        playerCamera.farClipPlane = 1000f;
        playerCamera.tag = "MainCamera";

        cameraGO.AddComponent<AudioListener>();
        cameraGO.AddComponent<CinemachineBrain>();

        // ── 2. Virtual Camera ──
        vcamGO = new GameObject($"VCam_Player [netId={netId}]");

        cinemachineCamera = vcamGO.AddComponent<CinemachineCamera>();
        cinemachineCamera.Follow = transform;
        cinemachineCamera.LookAt = transform;

        // ── 3. Orbital Follow (Position Control) ──
        orbitalFollow = vcamGO.AddComponent<CinemachineOrbitalFollow>();
        orbitalFollow.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
        orbitalFollow.Radius = orbitRadius;
        orbitalFollow.TargetOffset = targetOffset;
        orbitalFollow.TrackerSettings.PositionDamping = positionDamping;

        // Horizontal axis: full 360° wrap
        orbitalFollow.HorizontalAxis.Value = 0f;
        orbitalFollow.HorizontalAxis.Center = 0f;
        orbitalFollow.HorizontalAxis.Range = new Vector2(-180f, 180f);
        orbitalFollow.HorizontalAxis.Wrap = true;
        orbitalFollow.HorizontalAxis.Recentering.Enabled = enableRecentering;
        orbitalFollow.HorizontalAxis.Recentering.Wait = recenterWait;
        orbitalFollow.HorizontalAxis.Recentering.Time = recenterTime;

        // Vertical axis: clamped pitch
        orbitalFollow.VerticalAxis.Value = 15f;
        orbitalFollow.VerticalAxis.Center = 15f;
        orbitalFollow.VerticalAxis.Range = new Vector2(minVerticalAngle, maxVerticalAngle);
        orbitalFollow.VerticalAxis.Wrap = false;
        orbitalFollow.VerticalAxis.Recentering.Enabled = enableRecentering;
        orbitalFollow.VerticalAxis.Recentering.Wait = recenterWait;
        orbitalFollow.VerticalAxis.Recentering.Time = recenterTime;

        // ── 4. Input Axis Controller ──
        // This is the Cinemachine 3 way: it auto-detects axes on the
        // OrbitalFollow and connects them to mouse/gamepad input.
        // Works with both New Input System and Legacy.
        vcamGO.AddComponent<CinemachineInputAxisController>();

        // ── 5. Rotation Composer (Aim) ──
        var rotationComposer = vcamGO.AddComponent<CinemachineRotationComposer>();
        rotationComposer.TargetOffset = lookAtOffset;

        // ── 6. Deoccluder (Collision) ──
        if (enableCollision)
        {
            var deoccluder = vcamGO.AddComponent<CinemachineDeoccluder>();
            deoccluder.CollideAgainst = collisionLayers;

            if (!string.IsNullOrEmpty(ignoreCollisionTag))
            {
                deoccluder.IgnoreTag = ignoreCollisionTag;
            }
        }

        // ── 7. Finalize ──
        currentFOV = baseFOV;
        cameraCreated = true;

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        Debug.Log($"[ThirdPersonCamera] Cinemachine 3 orbit camera created for local player (netId={netId})");
    }

    private void DestroyCameraRig()
    {
        if (vcamGO != null) Destroy(vcamGO);
        if (cameraGO != null) Destroy(cameraGO);

        playerCamera = null;
        cinemachineCamera = null;
        orbitalFollow = null;
        cameraCreated = false;

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void DisableAllExistingCameras()
    {
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
        {
            Destroy(listener);
        }

        foreach (var cam in FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            cam.enabled = false;
            if (cam.transform.childCount == 0 && cam.GetComponents<Component>().Length <= 3)
            {
                cam.gameObject.SetActive(false);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  UPDATE — only FOV + cursor toggle
    // ──────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!isLocalPlayer || playerCamera == null) return;

        // Cursor toggle
        if (UnityEngine.InputSystem.Keyboard.current != null
            && UnityEngine.InputSystem.Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            bool isLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = isLocked ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = isLocked;
        }

        // Speed FOV
        if (enableSpeedFOV && carRigidbody != null)
        {
            float speed = carRigidbody.linearVelocity.magnitude;
            float speedNormalized = Mathf.Clamp01(speed / referenceSpeed);
            float targetFOV = baseFOV + (maxFOVBoost * speedNormalized);
            currentFOV = Mathf.Lerp(currentFOV, targetFOV, fovSmoothSpeed * Time.deltaTime);
            playerCamera.fieldOfView = currentFOV;
        }
    }
}