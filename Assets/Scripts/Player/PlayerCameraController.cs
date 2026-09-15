using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages per-player camera activation in a networked game.
/// On spawn, enables the Camera and AudioListener only for the owning client.
/// Non-owned instances keep both disabled to prevent camera conflicts
/// and the "more than one AudioListener" warning.
/// </summary>
public class PlayerCameraController : NetworkBehaviour
{
    [Header("References (wired in prefab)")]
    [Tooltip("The Camera component on the PlayerCamera child object.")]
    [SerializeField] private Camera playerCamera;

    [Tooltip("The AudioListener on the PlayerCamera child object.")]
    [SerializeField] private AudioListener audioListener;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            ActivateCamera();
        }
        else
        {
            DeactivateCamera();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        DeactivateCamera();
    }

    /// <summary>
    /// Enables the camera and audio listener for the local owning player.
    /// Also disables the scene's default main camera if one exists,
    /// since this player camera should now be the primary view.
    /// </summary>
    private void ActivateCamera()
    {
        if (playerCamera != null)
        {
            playerCamera.enabled = true;
            playerCamera.tag = "MainCamera"; // Mark as main so UI raycasts work
        }

        if (audioListener != null)
        {
            audioListener.enabled = true;
        }

        // Disable the scene's default camera (if any) to avoid two cameras rendering
        DisableSceneDefaultCamera();

        Debug.Log("[PlayerCameraController] Camera activated for local player (OwnerClientId=" + OwnerClientId + ")");
    }

    /// <summary>
    /// Disables the camera and audio listener. Used for non-owned players
    /// and on despawn cleanup.
    /// </summary>
    private void DeactivateCamera()
    {
        if (playerCamera != null)
        {
            playerCamera.enabled = false;
            playerCamera.tag = "Untagged";
        }

        if (audioListener != null)
        {
            audioListener.enabled = false;
        }
    }

    /// <summary>
    /// Finds and disables any pre-existing "Main Camera" in the scene so it
    /// doesn't conflict with the player's camera. Only runs on the owning client.
    /// </summary>
    private void DisableSceneDefaultCamera()
    {
        // Camera.main will find the first active camera tagged "MainCamera".
        // At this point, the player camera hasn't been tagged yet, so Camera.main
        // should return the scene's default camera (if any).
        Camera sceneCamera = Camera.main;

        // Make sure we don't disable our own camera
        if (sceneCamera != null && sceneCamera != playerCamera)
        {
            // Disable the AudioListener on the scene camera first
            AudioListener sceneListener = sceneCamera.GetComponent<AudioListener>();
            if (sceneListener != null)
            {
                sceneListener.enabled = false;
            }

            sceneCamera.enabled = false;
            Debug.Log("[PlayerCameraController] Disabled scene default camera: " + sceneCamera.gameObject.name);
        }
    }
}
