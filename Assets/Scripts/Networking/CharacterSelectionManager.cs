using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager for character selection.
/// Lives on the same scene object as NetworkManager / RelayManager.
///
/// FLOW:
/// 1. Host starts the Relay session. No player object is auto-created.
/// 2. This manager shows the selection UI for the local client.
/// 3. Player picks a character. Server reserves it and spawns the player object.
/// 4. Joining clients connect, see only available characters, pick one, then spawn.
/// 5. When a player disconnects, their character becomes available again.
/// </summary>
public class CharacterSelectionManager : NetworkBehaviour
{
    public static CharacterSelectionManager Instance { get; private set; }

    [Header("Data")]
    [Tooltip("Catalog of selectable characters.")]
    [SerializeField] private CharacterCatalog catalog;

    [Header("Relay / Spawn")]
    [Tooltip("RelayManager that provides spawn position/rotation.")]
    [SerializeField] private RelayManager relayManager;

    [Header("UI")]
    [Tooltip("UI controller to show after networking starts.")]
    [SerializeField] private CharacterSelectionUI selectionUI;

    /// <summary>Raised whenever a character is reserved or released.</summary>
    public event Action OnTakenCharactersChanged;

    /// <summary>
    /// Replicated list of character IDs already reserved by active players.
    /// </summary>
    private NetworkList<FixedString32Bytes> _takenCharacterIds;

    /// <summary>
    /// Server-only mapping from client ID to selected character ID.
    /// </summary>
    private readonly Dictionary<ulong, string> _clientChoices = new Dictionary<ulong, string>();

    private void Awake()
    {
        Instance = this;
        _takenCharacterIds = new NetworkList<FixedString32Bytes>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _takenCharacterIds.OnListChanged += OnTakenListChanged;

        if (IsClient && selectionUI != null)
            selectionUI.ShowCharacterSelection();

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _takenCharacterIds.OnListChanged -= OnTakenListChanged;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    /// <summary>
    /// True if no active player has reserved this character.
    /// </summary>
    public bool IsCharacterAvailable(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return false;
        for (int i = 0; i < _takenCharacterIds.Count; i++)
        {
            if (_takenCharacterIds[i].ToString() == characterId)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Called by the local selection UI when the player clicks a character.
    /// </summary>
    public void SelectCharacter(string characterId)
    {
        if (!IsSpawned)
        {
            Debug.LogWarning("[CharacterSelectionManager] Cannot select character before networking is started.");
            return;
        }

        if (IsServer)
        {
            ServerSelectCharacter(NetworkManager.Singleton.LocalClientId, characterId);
        }
        else
        {
            RequestCharacterServerRpc(characterId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestCharacterServerRpc(string characterId, ServerRpcParams rpcParams = default)
    {
        ServerSelectCharacter(rpcParams.Receive.SenderClientId, characterId);
    }

    private void ServerSelectCharacter(ulong clientId, string characterId)
    {
        if (_clientChoices.ContainsKey(clientId))
        {
            Debug.LogWarning($"[CharacterSelectionManager] Client {clientId} already selected a character.");
            return;
        }

        if (catalog == null || !catalog.HasCharacter(characterId))
        {
            RejectCharacterClientRpc(clientId, characterId, "Invalid character.");
            return;
        }

        if (!IsCharacterAvailable(characterId))
        {
            RejectCharacterClientRpc(clientId, characterId, "Character already taken.");
            return;
        }

        _clientChoices[clientId] = characterId;
        _takenCharacterIds.Add(new FixedString32Bytes(characterId));

        Debug.Log($"[CharacterSelectionManager] Client {clientId} reserved character '{characterId}'.");
        SpawnPlayer(clientId, characterId);
    }

    private void SpawnPlayer(ulong clientId, string characterId)
    {
        Vector3 spawnPos = relayManager != null ? relayManager.SpawnPosition : Vector3.zero;
        Quaternion spawnRot = relayManager != null ? relayManager.SpawnRotation : Quaternion.identity;

        GameObject prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        if (prefab == null)
        {
            Debug.LogError("[CharacterSelectionManager] NetworkManager has no PlayerPrefab assigned.");
            return;
        }

        GameObject playerInstance = Instantiate(prefab, spawnPos, spawnRot);
        NetworkObject netObj = playerInstance.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("[CharacterSelectionManager] Player prefab is missing a NetworkObject.");
            Destroy(playerInstance);
            return;
        }

        netObj.SpawnAsPlayerObject(clientId);

        PlayerCharacterAppearance appearance = playerInstance.GetComponent<PlayerCharacterAppearance>();
        if (appearance != null)
            appearance.SetCharacterId(characterId);
        Debug.Log($"[CharacterSelectionManager] Spawned player for client {clientId} with character '{characterId}'.");
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"[CharacterSelectionManager] Client {clientId} connected, waiting for character selection.");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (_clientChoices.TryGetValue(clientId, out string characterId))
        {
            _clientChoices.Remove(clientId);
            RemoveTakenCharacter(characterId);
            Debug.Log($"[CharacterSelectionManager] Client {clientId} disconnected; character '{characterId}' is now available.");
        }
    }

    private void RemoveTakenCharacter(string characterId)
    {
        for (int i = 0; i < _takenCharacterIds.Count; i++)
        {
            if (_takenCharacterIds[i].ToString() == characterId)
            {
                _takenCharacterIds.RemoveAt(i);
                return;
            }
        }
    }

    private void OnTakenListChanged(NetworkListEvent<FixedString32Bytes> changeEvent)
    {
        OnTakenCharactersChanged?.Invoke();
    }

    [ClientRpc]
    private void RejectCharacterClientRpc(ulong clientId, string characterId, string reason)
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId != clientId)
            return;

        Debug.LogWarning($"[CharacterSelectionManager] Character '{characterId}' rejected: {reason}");
    }
}
