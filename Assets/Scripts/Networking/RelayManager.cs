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

    private string _joinCode;

    // ───────────────────────── Lifecycle ─────────────────────────

    private bool _isSignedIn = false;

    private void Awake()
    {
        // Wire buttons immediately — don't wait for auth
        if (hostButton != null)  hostButton.onClick.AddListener(() => CreateRelay());
        if (joinButton != null)  joinButton.onClick.AddListener(() => JoinRelay(joinCodeInputField.text));

        Debug.Log("[RelayManager] Awake — buttons wired. hostButton=" + (hostButton != null) +
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
    }

    // ───────────────────────── Host (Create Relay) ─────────────────────────

    /// <summary>
    /// Allocates a Relay server, obtains a join code, configures UnityTransport,
    /// and starts NGO as a Host.
    /// </summary>
    public async void CreateRelay()
    {
        try
        {
            SetStatus("Creating relay...");
            Debug.Log("[RelayManager] Creating relay allocation...");

            // 1. Create a Relay allocation
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);

            // 2. Get the join code so other players can connect
            _joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log("[RelayManager] Join Code: " + _joinCode);

            // 3. Configure UnityTransport with the relay server data (DTLS encryption)
            //    Uses the ToRelayServerData() extension method from the Multiplayer SDK
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(allocation.ToRelayServerData("dtls"));

            // 4. Start as Host
            NetworkManager.Singleton.StartHost();

            SetStatus("Hosting! Join Code: " + _joinCode);
            Debug.Log("[RelayManager] Host started. Share this join code: " + _joinCode);

            // Disable buttons after connecting
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
            Debug.LogError("[RelayManager] CreateRelay failed: " + e);
        }
    }

    // ───────────────────────── Client (Join Relay) ─────────────────────────

    /// <summary>
    /// Joins an existing Relay allocation using the provided join code,
    /// configures UnityTransport, and starts NGO as a Client.
    /// </summary>
    public async void JoinRelay(string joinCode)
    {
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

            // 1. Join the allocation
            JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            // 2. Configure UnityTransport with the relay server data (DTLS encryption)
            //    Uses the ToRelayServerData() extension method from the Multiplayer SDK
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(joinAllocation.ToRelayServerData("dtls"));

            // 3. Start as Client
            NetworkManager.Singleton.StartClient();

            SetStatus("Connected as client!");
            Debug.Log("[RelayManager] Client started. Connected via Relay.");

            // Disable buttons after connecting
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
            Debug.LogError("[RelayManager] JoinRelay failed: " + e);
        }
    }

    // ───────────────────────── Helpers ─────────────────────────

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
