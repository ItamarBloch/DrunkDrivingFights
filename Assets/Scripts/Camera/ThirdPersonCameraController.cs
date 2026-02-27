using UnityEngine;
using Mirror;

/// <summary>
/// Multiplayer-ready 3rd person camera for vehicular combat.
/// Attach to your Car prefab — programmatically creates and manages
/// the camera at runtime. Only the local player's camera activates.
///
/// Features:
///   - Programmatic camera creation (no scene camera needed)
///   - Multiplayer-safe: one camera per local player, zero for remotes
///   - Speed-adaptive smoothing (tighter at low speed, floatier at high speed)
///   - Velocity-based pull-back + look-ahead for responsiveness
///   - Wall/terrain collision that ignores the car's own colliders
///   - Speed-based FOV widening
///   - Editor gizmos for tuning
/// </summary>
public class ThirdPersonCameraController : NetworkBehaviour
{
    // ──────────────────────────────────────────────
    //  CAMERA SETTINGS
    // ──────────────────────────────────────────────

    [Header("Follow Settings")]
    [Tooltip("Base offset from the car in local space (X=right, Y=up, Z=back)")]
    [SerializeField] private Vector3 baseOffset = new Vector3(0f, 3.5f, -8f);

    [Tooltip("Base follow smooth speed (lower = smoother, higher = snappier)")]
    [SerializeField] private float baseFollowSpeed = 10f;

    [Tooltip("Base rotation smooth speed")]
    [SerializeField] private float baseRotationSpeed = 8f;

    [Header("Look Target")]
    [Tooltip("Offset from car pivot the camera looks at (slightly above center)")]
    [SerializeField] private Vector3 lookAtOffset = new Vector3(0f, 1.5f, 0f);

    [Header("Speed-Adaptive Behaviour")]
    [Tooltip("Extra distance the camera pulls back at max speed")]
    [SerializeField] private float speedPullBack = 3f;

    [Tooltip("How far ahead of the car the camera look-target shifts based on velocity")]
    [SerializeField] private float velocityLookAhead = 2f;

    [Tooltip("At this speed (m/s) the speed effects are fully applied")]
    [SerializeField] private float referenceSpeed = 40f;

    [Tooltip("Follow speed multiplier at max speed (>1 = snappier at speed, <1 = floatier)")]
    [SerializeField] private float speedFollowMultiplier = 1.4f;

    [Header("Speed-Based FOV")]
    [SerializeField] private bool enableSpeedFOV = true;
    [SerializeField] private float baseFOV = 60f;
    [SerializeField] private float maxFOVBoost = 15f;
    [SerializeField] private float fovSmoothSpeed = 5f;

    [Header("Collision")]
    [Tooltip("Prevent camera from clipping through walls/terrain")]
    [SerializeField] private bool enableCollision = true;

    [Tooltip("Radius of the collision sphere cast")]
    [SerializeField] private float collisionRadius = 0.3f;

    [Tooltip("Layers the camera collides with (EXCLUDE your vehicle layer!)")]
    [SerializeField] private LayerMask collisionLayers = ~0;

    [Tooltip("Minimum distance the camera can be from the look target")]
    [SerializeField] private float minimumDistance = 1.0f;

    [Tooltip("How fast the camera pulls in on collision")]
    [SerializeField] private float collisionPullInSpeed = 20f;

    [Tooltip("How fast the camera restores to full distance after collision clears")]
    [SerializeField] private float collisionRestoreSpeed = 8f;

    // ──────────────────────────────────────────────
    //  RUNTIME STATE
    // ──────────────────────────────────────────────

    private Camera playerCamera;
    private AudioListener playerAudioListener;
    private Transform cameraTransform;
    private Rigidbody carRigidbody;
    private Collider[] carColliders; // cached to ignore in raycasts

    // Smoothing state
    private Vector3 positionVelocity;     // for SmoothDamp
    private float currentFOV;
    private float currentCollisionFactor; // 0 = fully pulled in, 1 = full distance
    private bool cameraCreated;

    // ──────────────────────────────────────────────
    //  PUBLIC API
    // ──────────────────────────────────────────────

    /// <summary>The camera instance. Null on remote players.</summary>
    public Camera Camera => playerCamera;

    /// <summary>Change the base offset at runtime.</summary>
    public void SetOffset(Vector3 newOffset) => baseOffset = newOffset;

    /// <summary>Change the look-at offset at runtime.</summary>
    public void SetLookAtOffset(Vector3 newLookAtOffset) => lookAtOffset = newLookAtOffset;

    // ──────────────────────────────────────────────
    //  MIRROR LIFECYCLE
    // ──────────────────────────────────────────────

    public override void OnStartLocalPlayer()
    {
        base.OnStartLocalPlayer();
        CreateCamera();
    }

    public override void OnStopLocalPlayer()
    {
        base.OnStopLocalPlayer();
        DestroyCamera();
    }

    // ──────────────────────────────────────────────
    //  CAMERA CREATION
    // ──────────────────────────────────────────────

    private void CreateCamera()
    {
        // Guard: never create twice (covers host edge cases)
        if (cameraCreated) return;

        // ── Step 1: Kill ALL existing cameras + audio listeners ──
        // This guarantees the host doesn't end up with 2 cameras,
        // regardless of how the scene camera is named or tagged.
        DisableAllExistingCameras();

        // ── Step 2: Create our camera ──
        GameObject camObj = new GameObject($"PlayerCamera [netId={netId}]");

        playerCamera = camObj.AddComponent<Camera>();
        playerCamera.fieldOfView = baseFOV;
        playerCamera.nearClipPlane = 0.1f;
        playerCamera.farClipPlane = 1000f;
        playerCamera.tag = "MainCamera";

        playerAudioListener = camObj.AddComponent<AudioListener>();
        cameraTransform = camObj.transform;

        // ── Step 3: Cache car references ──
        carRigidbody = GetComponent<Rigidbody>();

        // Cache ALL colliders on the car so we can ignore them in spherecasts
        carColliders = GetComponentsInChildren<Collider>();

        // ── Step 4: Initialize state ──
        currentFOV = baseFOV;
        currentCollisionFactor = 1f; // full distance, no collision
        cameraCreated = true;

        // Snap immediately so the camera doesn't fly in from the origin
        SnapToTarget();

        Debug.Log($"[ThirdPersonCamera] Created for local player (netId={netId}), " +
                  $"ignoring {carColliders.Length} car colliders in collision checks");
    }

    private void DestroyCamera()
    {
        if (playerCamera != null)
        {
            Destroy(playerCamera.gameObject);
            playerCamera = null;
            playerAudioListener = null;
            cameraTransform = null;
        }
        cameraCreated = false;
    }

    /// <summary>
    /// Disables ALL cameras and audio listeners in the scene.
    /// Aggressive on purpose — guarantees no leftover cameras from the scene,
    /// from other scripts, or from Unity defaults.
    /// Our camera is created AFTER this runs.
    /// </summary>
    private void DisableAllExistingCameras()
    {
        // Destroy all audio listeners first (only 1 is allowed in Unity)
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
        foreach (AudioListener listener in listeners)
        {
            Debug.Log($"[ThirdPersonCamera] Removing AudioListener from: {listener.gameObject.name}");
            Destroy(listener);
        }

        // Disable all existing cameras
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera cam in cameras)
        {
            Debug.Log($"[ThirdPersonCamera] Disabling existing camera: {cam.gameObject.name}");
            cam.enabled = false;

            // Disable the whole GO if it looks like a standalone camera holder
            // (has no children, just Transform + Camera + maybe AudioListener)
            if (cam.transform.childCount == 0 && cam.GetComponents<Component>().Length <= 3)
            {
                cam.gameObject.SetActive(false);
            }
        }
    }

    // ──────────────────────────────────────────────
    //  UPDATE
    // ──────────────────────────────────────────────

    private void LateUpdate()
    {
        if (!isLocalPlayer || cameraTransform == null) return;

        float speed = GetCarSpeed();
        float speedNormalized = Mathf.Clamp01(speed / referenceSpeed);

        Vector3 desiredPos = CalculateDesiredPosition(speedNormalized);
        Vector3 lookTarget = CalculateLookTarget(speedNormalized);

        // Apply collision
        if (enableCollision)
        {
            desiredPos = ApplyCollision(lookTarget, desiredPos);
        }

        // Smooth follow — speed-adaptive: snappier at high speed so camera keeps up
        float adaptiveSpeed = baseFollowSpeed * Mathf.Lerp(1f, speedFollowMultiplier, speedNormalized);
        cameraTransform.position = Vector3.SmoothDamp(
            cameraTransform.position,
            desiredPos,
            ref positionVelocity,
            1f / adaptiveSpeed
        );

        // Smooth look rotation — also speed-adaptive
        Vector3 lookDir = lookTarget - cameraTransform.position;
        if (lookDir.sqrMagnitude > 0.001f)
        {
            Quaternion desiredRot = Quaternion.LookRotation(lookDir);
            float adaptiveRotSpeed = baseRotationSpeed * Mathf.Lerp(1f, speedFollowMultiplier, speedNormalized);
            cameraTransform.rotation = Quaternion.Slerp(
                cameraTransform.rotation,
                desiredRot,
                adaptiveRotSpeed * Time.deltaTime
            );
        }

        // FOV
        UpdateFOV(speedNormalized);
    }

    // ──────────────────────────────────────────────
    //  POSITION & LOOK TARGET CALCULATION
    // ──────────────────────────────────────────────

    /// <summary>
    /// Desired camera position: base offset + speed-based pull-back.
    /// At higher speeds the camera pulls further back to show more arena.
    /// </summary>
    private Vector3 CalculateDesiredPosition(float speedNormalized)
    {
        Vector3 offset = baseOffset;

        // Pull back further at speed (extend the Z which is already negative)
        offset.z -= speedPullBack * speedNormalized;

        // Convert local offset to world space using car's rotation
        return transform.position + transform.rotation * offset;
    }

    /// <summary>
    /// Where the camera looks. At speed, the look target shifts slightly
    /// in the direction of movement so you see more of what's ahead.
    /// </summary>
    private Vector3 CalculateLookTarget(float speedNormalized)
    {
        Vector3 baseLookTarget = transform.position + transform.TransformDirection(lookAtOffset);

        // Add velocity-based look-ahead
        if (carRigidbody != null && speedNormalized > 0.05f)
        {
            Vector3 velocityDir = carRigidbody.linearVelocity;
            velocityDir.y *= 0.3f; // dampen vertical look-ahead (don't stare at sky when jumping)
            baseLookTarget += velocityDir.normalized * (velocityLookAhead * speedNormalized);
        }

        return baseLookTarget;
    }

    // ──────────────────────────────────────────────
    //  COLLISION
    // ──────────────────────────────────────────────

    /// <summary>
    /// Sphere-cast from the look target toward the desired camera position.
    /// Uses RaycastAll so we can IGNORE the car's own colliders.
    /// Pulls in fast on hit, restores slowly when clear.
    /// </summary>
    private Vector3 ApplyCollision(Vector3 lookTarget, Vector3 desiredPosition)
    {
        Vector3 direction = desiredPosition - lookTarget;
        float fullDistance = direction.magnitude;

        if (fullDistance < 0.01f) return desiredPosition;

        Vector3 dirNormalized = direction / fullDistance;

        // RaycastAll so we can filter out our own colliders
        RaycastHit[] hits = Physics.SphereCastAll(
            lookTarget,
            collisionRadius,
            dirNormalized,
            fullDistance,
            collisionLayers,
            QueryTriggerInteraction.Ignore
        );

        // Find closest hit that ISN'T part of our car
        float closestHitDistance = fullDistance;
        bool hitSomething = false;

        foreach (RaycastHit hit in hits)
        {
            if (IsCarCollider(hit.collider)) continue;

            if (hit.distance < closestHitDistance)
            {
                closestHitDistance = hit.distance;
                hitSomething = true;
            }
        }

        if (hitSomething)
        {
            // Pull in front of obstacle
            float safeDistance = closestHitDistance - collisionRadius;
            safeDistance = Mathf.Max(safeDistance, minimumDistance);
            float safeFactor = safeDistance / fullDistance;

            // Pull IN fast so we never clip through walls
            currentCollisionFactor = Mathf.MoveTowards(
                currentCollisionFactor,
                safeFactor,
                collisionPullInSpeed * Time.deltaTime
            );
        }
        else
        {
            // No collision — smoothly restore to full distance
            currentCollisionFactor = Mathf.MoveTowards(
                currentCollisionFactor,
                1f,
                collisionRestoreSpeed * Time.deltaTime
            );
        }

        return lookTarget + dirNormalized * (fullDistance * currentCollisionFactor);
    }

    /// <summary>
    /// Check if a collider belongs to our car (cached on creation).
    /// </summary>
    private bool IsCarCollider(Collider col)
    {
        if (carColliders == null) return false;

        for (int i = 0; i < carColliders.Length; i++)
        {
            if (carColliders[i] == col) return true;
        }
        return false;
    }

    // ──────────────────────────────────────────────
    //  FOV
    // ──────────────────────────────────────────────

    private void UpdateFOV(float speedNormalized)
    {
        if (!enableSpeedFOV || playerCamera == null) return;

        float targetFOV = baseFOV + (maxFOVBoost * speedNormalized);
        currentFOV = Mathf.Lerp(currentFOV, targetFOV, fovSmoothSpeed * Time.deltaTime);
        playerCamera.fieldOfView = currentFOV;
    }

    // ──────────────────────────────────────────────
    //  HELPERS
    // ──────────────────────────────────────────────

    private float GetCarSpeed()
    {
        return carRigidbody != null ? carRigidbody.linearVelocity.magnitude : 0f;
    }

    /// <summary>
    /// Instant snap — no smoothing. Used on first spawn so camera
    /// doesn't interpolate from the world origin.
    /// </summary>
    private void SnapToTarget()
    {
        if (cameraTransform == null) return;

        float speedNorm = Mathf.Clamp01(GetCarSpeed() / referenceSpeed);
        Vector3 desiredPos = CalculateDesiredPosition(speedNorm);
        Vector3 lookTarget = CalculateLookTarget(speedNorm);

        if (enableCollision)
        {
            desiredPos = ApplyCollision(lookTarget, desiredPos);
        }

        cameraTransform.position = desiredPos;
        cameraTransform.LookAt(lookTarget);

        positionVelocity = Vector3.zero;
        currentCollisionFactor = 1f;
    }

    // ──────────────────────────────────────────────
    //  EDITOR GIZMOS
    // ──────────────────────────────────────────────

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Vector3 desiredPos = transform.position + transform.rotation * baseOffset;
        Vector3 lookTarget = transform.position + transform.TransformDirection(lookAtOffset);

        // Camera position (cyan)
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(desiredPos, 0.3f);

        // Look target (yellow)
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(lookTarget, 0.2f);

        // Line between them (green)
        Gizmos.color = Color.green;
        Gizmos.DrawLine(desiredPos, lookTarget);

        // Max speed pull-back position (orange)
        Vector3 maxSpeedOffset = baseOffset;
        maxSpeedOffset.z -= speedPullBack;
        Vector3 maxSpeedPos = transform.position + transform.rotation * maxSpeedOffset;
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.5f);
        Gizmos.DrawWireSphere(maxSpeedPos, 0.2f);
        Gizmos.DrawLine(desiredPos, maxSpeedPos);

        // Collision radius (red)
        if (enableCollision)
        {
            Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
            Gizmos.DrawWireSphere(desiredPos, collisionRadius);
        }
    }
#endif
}
