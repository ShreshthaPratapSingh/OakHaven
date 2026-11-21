using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Manages Unity Relay allocation and NGO host/client startup.
/// Attach this to the same GameObject as NetworkManager (or any scene object).
/// Wire the UI buttons and input field in the Inspector.
///
/// CHARACTER SELECTION NOTE:
/// This script no longer immediately spawns the player. It only creates/joins the
/// Relay session and starts NGO. CharacterSelectionManager then waits for the
/// player to choose a character and spawns the player object manually.
/// </summary>
public class RelayManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_InputField joinCodeInputField;
    [SerializeField] private TMP_Text statusText;

    [Header("Relay Settings")]
    [SerializeField] private int maxConnections = 4;

    [Header("Spawn Settings")]
    [Tooltip("Where players spawn when they join. Set Y slightly above terrain to avoid clipping.")]
    [SerializeField] private Vector3 spawnPosition = new Vector3(625.08f, 4.5f, 436.94f);
    [SerializeField] private Vector3 spawnRotation = new Vector3(0f, 180f, 0f);

    public Vector3 SpawnPosition => spawnPosition;
    public Quaternion SpawnRotation => Quaternion.Euler(spawnRotation);

    private string _joinCode;

    // ───────────────────────── Lifecycle ─────────────────────────

    private bool _isSignedIn = false;

    private void Awake()
    {
        Debug.Log("[RelayManager] Awake — hostButton=" + (hostButton != null) +
                  " joinButton=" + (joinButton != null) + " statusText=" + (statusText != null));
    }

    private async void Start()
    {
        try
        {
            Debug.Log("[RelayManager] Starting UGS initialization...");
            SetStatus("Initializing UGS...");

            await UnityServices.InitializeAsync();
            Debug.Log("[RelayManager] UGS initialized. Signing in...");
            SetStatus("Signing in...");

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            _isSignedIn = true;
            SetStatus("Signed in! Player: " + AuthenticationService.Instance.PlayerId);
            Debug.Log("[RelayManager] Signed in. Player ID: " + AuthenticationService.Instance.PlayerId);
        }
        catch (Exception e)
        {
            SetStatus("Auth failed: " + e.Message);
            Debug.LogError("[RelayManager] Authentication failed:\n" + e);
        }

        // Runtime fallback: make sure the menu buttons drive the character selection UI
        // even if the scene's persistent UnityEvent listeners were lost.
        WireButtonsToCharacterSelectionUI();
    }

    private void WireButtonsToCharacterSelectionUI()
    {
        CharacterSelectionUI ui = FindFirstObjectByType<CharacterSelectionUI>();
        if (ui == null)
        {
            Debug.LogWarning("[RelayManager] No CharacterSelectionUI found in scene. Host/Join buttons may not work.");
            return;
        }

        if (hostButton != null)
        {
            hostButton.onClick.RemoveAllListeners();
            hostButton.onClick.AddListener(ui.OnHostClicked);
            Debug.Log("[RelayManager] HostButton wired to CharacterSelectionUI.OnHostClicked at runtime.");
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(ui.OnJoinClicked);
            Debug.Log("[RelayManager] JoinButton wired to CharacterSelectionUI.OnJoinClicked at runtime.");
        }
    }

    // ───────────────────────── Host (Create Relay) ─────────────────────────

    /// <summary>
    /// Allocates a Relay server, obtains a join code, configures UnityTransport,
    /// and starts NGO as a Host. Player spawning is deferred to CharacterSelectionManager.
    /// </summary>
    public async void StartHostFlow()
    {
        if (!_isSignedIn)
        {
            SetStatus("Not signed in yet.");
            Debug.LogWarning("[RelayManager] Cannot host — not signed in.");
            return;
        }

        try
        {
            SetStatus("Creating relay...");
            Debug.Log("[RelayManager] Creating relay allocation...");

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log("[RelayManager] Join Code: " + _joinCode);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));

            ConfigureConnectionApproval();

            NetworkManager.Singleton.StartHost();

            SetStatus("Hosting! Join Code: " + _joinCode);
            Debug.Log("[RelayManager] Host started. Share this join code: " + _joinCode);

            SetButtonsInteractable(false);
        }
        catch (RelayServiceException e)
        {
            SetStatus("Relay error: " + e.Message);
            Debug.LogError("[RelayManager] Relay service error: " + e);
        }
        catch (Exception e)
        {
            SetStatus("Error: " + e.Message);
            Debug.LogError("[RelayManager] StartHostFlow failed: " + e);
        }
    }

    // ───────────────────────── Client (Join Relay) ─────────────────────────

    /// <summary>
    /// Joins an existing Relay allocation using the provided join code,
    /// configures UnityTransport, and starts NGO as a Client.
    /// Player spawning is deferred to CharacterSelectionManager.
    /// </summary>
    public async void StartClientFlow(string joinCode)
    {
        if (!_isSignedIn)
        {
            SetStatus("Not signed in yet.");
            Debug.LogWarning("[RelayManager] Cannot join — not signed in.");
            return;
        }

        if (string.IsNullOrWhiteSpace(joinCode))
        {
            SetStatus("Please enter a join code.");
            Debug.LogWarning("[RelayManager] Join code is empty.");
            return;
        }

        try
        {
            SetStatus("Joining relay...");
            Debug.Log("[RelayManager] Joining relay with code: " + joinCode);

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(joinAllocation.ToRelayServerData("dtls"));

            ConfigureConnectionApproval();

            NetworkManager.Singleton.StartClient();

            SetStatus("Connected as client!");
            Debug.Log("[RelayManager] Client started. Connected via Relay.");

            SetButtonsInteractable(false);
        }
        catch (RelayServiceException e)
        {
            SetStatus("Relay error: " + e.Message);
            Debug.LogError("[RelayManager] Relay service error: " + e);
        }
        catch (Exception e)
        {
            SetStatus("Error: " + e.Message);
            Debug.LogError("[RelayManager] StartClientFlow failed: " + e);
        }
    }

    // ───────────────────────── Connection Approval ─────────────────────────

    /// <summary>
    /// Configures the NetworkManager to use connection approval so we can
    /// set the player's spawn position/rotation, but we defer player object creation
    /// until CharacterSelectionManager confirms a character choice.
    /// </summary>
    private void ConfigureConnectionApproval()
    {
        var nm = NetworkManager.Singleton;
        nm.NetworkConfig.ConnectionApproval = true;
        nm.ConnectionApprovalCallback = OnConnectionApproval;
    }

    /// <summary>
    /// Called on the server/host for each incoming connection.
    /// Approves the connection and stores the spawn position, but does NOT
    /// auto-create the player object. CharacterSelectionManager spawns it later.
    /// </summary>
    private void OnConnectionApproval(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = true;
        response.CreatePlayerObject = false;
        response.Position = spawnPosition;
        response.Rotation = Quaternion.Euler(spawnRotation);
        response.Pending = false;

        Debug.Log("[RelayManager] Approved connection for client " + request.ClientNetworkId +
                  " — player spawn deferred to character selection.");
    }

    // ───────────────────────── Helpers ─────────────────────────

    private void OnApplicationQuit()
    {
        if (NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsClient))
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (hostButton != null) hostButton.interactable = interactable;
        if (joinButton != null) joinButton.interactable = interactable;
        if (joinCodeInputField != null) joinCodeInputField.interactable = interactable;
    }
}
