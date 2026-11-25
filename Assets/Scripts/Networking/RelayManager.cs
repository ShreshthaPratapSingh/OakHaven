using System;
using System.Collections;
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
    [Header("Menu UI")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private TMP_InputField joinCodeInputField;
    [SerializeField] private TMP_Text statusText;

    [Header("Session HUD")]
    [Tooltip("Persistent HUD root shown after host/client starts. Should be a direct child of the Canvas, outside any menu panel.")]
    [SerializeField] private GameObject sessionHudRoot;
    [Tooltip("TMP text inside the persistent HUD that displays the join code / connection status.")]
    [SerializeField] private TMP_Text sessionCodeText;

    [Header("Relay Settings")]
    [SerializeField] private int maxConnections = 4;

    [Header("Spawn Settings")]
    [Tooltip("Where players spawn when they join. Set Y slightly above terrain to avoid clipping.")]
    [SerializeField] private Vector3 spawnPosition = new Vector3(625.08f, 4.5f, 436.94f);
    [SerializeField] private Vector3 spawnRotation = new Vector3(0f, 180f, 0f);

    public Vector3 SpawnPosition => spawnPosition;
    public Quaternion SpawnRotation => Quaternion.Euler(spawnRotation);

    private string _joinCode;
    private string _pendingJoinCode;
    private Coroutine _connectionTimeout;

    // How long the client waits for OnClientConnectedCallback before declaring timeout.
    private const float ConnectionTimeoutSeconds = 15f;

    // True while a host/client start is in progress. Prevents double-clicks from
    // starting multiple concurrent Relay allocations / NGO starts.
    private bool _networkStartInProgress;

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

        // Subscribe to NGO lifecycle events so we only report success/failure when
        // the transport actually connects or disconnects.
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
            NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;
        }
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
        }
    }

    // ───────────────────────── Host (Create Relay) ─────────────────────────

    /// <summary>
    /// Allocates a Relay server, obtains a join code, configures UnityTransport,
    /// and starts NGO as a Host. Player spawning is deferred to CharacterSelectionManager.
    /// </summary>
    public async void StartHostFlow()
    {
        if (_networkStartInProgress)
        {
            Debug.Log("[RelayManager] Host start already in progress. Ignoring extra click.");
            return;
        }

        if (!_isSignedIn)
        {
            SetStatus("Not signed in yet.");
            Debug.LogWarning("[RelayManager] Cannot host — not signed in.");
            return;
        }

        _networkStartInProgress = true;
        SetButtonsInteractable(false);
        bool startedHost = false;

        try
        {
            if (!ValidateNetworkManagerReady("host"))
                return;

            SetStatus("Creating relay...");
            Debug.Log("[RelayManager] Creating relay allocation...");

            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
            _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log("[RelayManager] Join Code: " + _joinCode);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));

            // Connection approval must be enabled on BOTH peers so the NGO config hash
            // matches during the handshake. The callback itself is only assigned on the host.
            PrepareNetworkConfig(isHost: true);

            LogNetworkConfig("host");
            LogNetworkManagerState("before StartHost");
            startedHost = NetworkManager.Singleton.StartHost();
            if (!startedHost)
            {
                SetStatus("Failed to start host. Check logs.");
                LogNetworkManagerState("after StartHost failure");
                Debug.LogError("[RelayManager] StartHost returned false.");
                NetworkManager.Singleton.Shutdown();
                return;
            }

            Debug.Log("[RelayManager] Host start requested. Waiting for local client connection...");
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
        finally
        {
            if (!startedHost)
            {
                _networkStartInProgress = false;
                SetButtonsInteractable(true);
            }
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
        if (_networkStartInProgress)
        {
            Debug.Log("[RelayManager] Join already in progress. Ignoring extra click.");
            return;
        }

        if (!_isSignedIn)
        {
            SetStatus("Not signed in yet.");
            Debug.LogWarning("[RelayManager] Cannot join — not signed in.");
            return;
        }

        joinCode = joinCode?.Trim();
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            SetStatus("Please enter a join code.");
            Debug.LogWarning("[RelayManager] Join code is empty.");
            return;
        }

        _networkStartInProgress = true;
        SetButtonsInteractable(false);
        bool startedClient = false;

        try
        {
            if (!ValidateNetworkManagerReady("client"))
                return;

            _pendingJoinCode = joinCode;
            SetStatus("Joining relay...");
            Debug.Log("[RelayManager] Joining relay with code: " + joinCode);

            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(joinAllocation.ToRelayServerData("dtls"));

            // Connection approval must be enabled on BOTH peers so the NGO config hash
            // matches during the handshake. The callback itself is only assigned on the host.
            PrepareNetworkConfig(isHost: false);

            LogNetworkConfig("client");
            LogNetworkManagerState("before StartClient");
            startedClient = NetworkManager.Singleton.StartClient();
            if (!startedClient)
            {
                SetStatus("Failed to start client. Check logs.");
                LogNetworkManagerState("after StartClient failure");
                Debug.LogError("[RelayManager] StartClient returned false.");
                NetworkManager.Singleton.Shutdown();
                return;
            }

            if (_connectionTimeout != null) StopCoroutine(_connectionTimeout);
            _connectionTimeout = StartCoroutine(ConnectionTimeoutCoroutine());

            SetStatus("Connecting to host...");
            Debug.Log("[RelayManager] Client start requested. Waiting for connection...");
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
        finally
        {
            if (!startedClient)
            {
                _networkStartInProgress = false;
                SetButtonsInteractable(true);
            }
        }
    }

    // ───────────────────────── Connection Approval ─────────────────────────

    /// <summary>
    /// Enables connection approval in the NGO config on both host and client so
    /// their NetworkConfig hashes match. The approval callback is only registered
    /// on the host; the client does not need one.
    /// </summary>
    private void PrepareNetworkConfig(bool isHost)
    {
        var nm = NetworkManager.Singleton;
        nm.NetworkConfig.ConnectionApproval = true;
        nm.ConnectionApprovalCallback = isHost ? OnConnectionApproval : null;

        Debug.Log($"[RelayManager] Prepared network config for {(isHost ? "host" : "client")}. " +
                  $"ConnectionApproval=true, Callback={(isHost ? "assigned" : "cleared")}.");
    }

    /// <summary>
    /// Logs the NGO config hash and the fields that contribute to it. Useful for
    /// diagnosing mismatches between host and client builds.
    /// </summary>
    private void LogNetworkConfig(string role)
    {
        if (NetworkManager.Singleton == null) return;

        var config = NetworkManager.Singleton.NetworkConfig;
        try
        {
            ulong hash = config.GetConfig(false);
            Debug.Log($"[RelayManager] NetworkConfig [{role}] hash=0x{hash:X16}, " +
                      $"ProtocolVersion={config.ProtocolVersion}, " +
                      $"ConnectionApproval={config.ConnectionApproval}, " +
                      $"ForceSamePrefabs={config.ForceSamePrefabs}, " +
                      $"EnableSceneManagement={config.EnableSceneManagement}, " +
                      $"TickRate={config.TickRate}, " +
                      $"RpcHashSize={config.RpcHashSize}, " +
                      $"PlayerPrefab={(config.PlayerPrefab != null ? config.PlayerPrefab.name : "null")}, " +
                      $"Prefabs.Count={config.Prefabs.Prefabs.Count}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[RelayManager] Could not compute NetworkConfig hash: " + ex.Message);
        }
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

    // ───────────────────────── NGO Lifecycle Callbacks ─────────────────────────

    private void OnClientConnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        _networkStartInProgress = false;

        if (_connectionTimeout != null)
        {
            StopCoroutine(_connectionTimeout);
            _connectionTimeout = null;
        }

        if (NetworkManager.Singleton.IsHost)
        {
            SetStatus("Hosting! Join Code: " + _joinCode);
            ShowSessionHud("Join Code: " + _joinCode);
            Debug.Log("[RelayManager] Host local client connected. Join code: " + _joinCode);
        }
        else if (NetworkManager.Singleton.IsClient)
        {
            SetStatus("Connected as client!");
            ShowSessionHud("Joined: " + _pendingJoinCode);
            Debug.Log("[RelayManager] Client connected to host.");
        }
    }

    private void OnClientDisconnect(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        _networkStartInProgress = false;

        if (_connectionTimeout != null)
        {
            StopCoroutine(_connectionTimeout);
            _connectionTimeout = null;
        }

        string reason = NetworkManager.Singleton.DisconnectReason;
        if (!string.IsNullOrEmpty(reason))
        {
            Debug.LogError("[RelayManager] Disconnected. Reason: " + reason);
            SetStatus("Disconnected: " + reason);
        }
        else
        {
            Debug.LogWarning("[RelayManager] Disconnected from server (no reason provided).");
            SetStatus("Disconnected from server.");
        }

        ClearSessionHud();
        SetButtonsInteractable(true);
    }

    private void OnTransportFailure()
    {
        _networkStartInProgress = false;

        if (_connectionTimeout != null)
        {
            StopCoroutine(_connectionTimeout);
            _connectionTimeout = null;
        }

        string reason = NetworkManager.Singleton != null ? NetworkManager.Singleton.DisconnectReason : "";
        if (!string.IsNullOrEmpty(reason))
        {
            Debug.LogError("[RelayManager] Transport failure. Reason: " + reason);
            SetStatus("Connection failed: " + reason);
        }
        else
        {
            Debug.LogError("[RelayManager] Transport failure. Check the join code and try again.");
            SetStatus("Connection failed. Check the join code and try again.");
        }

        ClearSessionHud();
        SetButtonsInteractable(true);
    }

    private IEnumerator ConnectionTimeoutCoroutine()
    {
        yield return new WaitForSeconds(ConnectionTimeoutSeconds);
        _connectionTimeout = null;

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsConnectedClient)
        {
            _networkStartInProgress = false;
            SetStatus("Connection timed out. Check the join code and try again.");
            ClearSessionHud();
            SetButtonsInteractable(true);
            NetworkManager.Singleton.Shutdown();
        }
    }

    // ───────────────────────── Helpers ─────────────────────────

    /// <summary>
    /// Returns true if NetworkManager exists and is not already running.
    /// </summary>
    private bool ValidateNetworkManagerReady(string flowName)
    {
        if (NetworkManager.Singleton == null)
        {
            SetStatus("NetworkManager is missing.");
            Debug.LogError("[RelayManager] NetworkManager.Singleton is null.");
            return false;
        }

        if (NetworkManager.Singleton.IsListening)
        {
            SetStatus($"Already connected. Use Back to disconnect before starting {flowName}.");
            Debug.LogWarning($"[RelayManager] NetworkManager is already running (IsHost={NetworkManager.Singleton.IsHost}, " +
                             $"IsClient={NetworkManager.Singleton.IsClient}, IsServer={NetworkManager.Singleton.IsServer}). " +
                             $"Cannot start {flowName} without disconnecting first.");
            return false;
        }

        return true;
    }

    private void LogNetworkManagerState(string context)
    {
        if (NetworkManager.Singleton == null)
        {
            Debug.LogError("[RelayManager] NetworkManager state " + context + ": Singleton is null.");
            return;
        }

        Debug.Log($"[RelayManager] NetworkManager state {context}: " +
                  $"IsListening={NetworkManager.Singleton.IsListening}, " +
                  $"IsHost={NetworkManager.Singleton.IsHost}, " +
                  $"IsClient={NetworkManager.Singleton.IsClient}, " +
                  $"IsServer={NetworkManager.Singleton.IsServer}, " +
                  $"IsConnectedClient={NetworkManager.Singleton.IsConnectedClient}");
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

    /// <summary>
    /// Shows the persistent session HUD with the given message.
    /// Called after hosting or joining succeeds.
    /// </summary>
    private void ShowSessionHud(string message)
    {
        if (sessionCodeText != null)
            sessionCodeText.text = message;

        if (sessionHudRoot != null)
            sessionHudRoot.SetActive(true);
    }

    /// <summary>
    /// Hides and clears the persistent session HUD.
    /// Public so the back-to-menu flow can call it.
    /// </summary>
    public void ClearSessionHud()
    {
        if (sessionHudRoot != null)
            sessionHudRoot.SetActive(false);

        if (sessionCodeText != null)
            sessionCodeText.text = "";
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (hostButton != null) hostButton.interactable = interactable;
        if (joinButton != null) joinButton.interactable = interactable;
        if (joinCodeInputField != null) joinCodeInputField.interactable = interactable;
    }
}
