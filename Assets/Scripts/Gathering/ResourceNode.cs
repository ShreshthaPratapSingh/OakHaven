using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A gatherable resource node (tree, rock, bush) placed in-scene as a NetworkObject.
///
/// NETWORKING FLOW:
/// ─ currentHealth is a NetworkVariable<int> that auto-replicates to all clients.
///   Only the server can write to it (default write permission = Server).
/// ─ HitServerRpc() is called by clients when they interact with the node.
///   The RPC runs ON THE SERVER, which validates the hit, reduces health,
///   grants resources to the hitter's Inventory, and despawns when depleted.
/// ─ Clients see the health decrease via the NetworkVariable and can play
///   visual/audio feedback locally.
///
/// TOOL VALIDATION:
/// ─ requiredToolTag is checked both client-side (in PlayerGathering, for
///   immediate feedback) and server-side (in HitServerRpc, to prevent
///   modified clients from bypassing the requirement).
///
/// EDITOR SETUP:
/// 1. Place a tree/rock/bush GameObject in the scene.
/// 2. Add a NetworkObject component.
/// 3. Add this ResourceNode component.
/// 4. Set the resourceType (e.g. "wood"), amountPerHit, maxHealth, requiredToolTag.
/// 5. Add a Collider (e.g. CapsuleCollider for trees) so the player's raycast can hit it.
/// 6. Since these are in-scene placed NetworkObjects, they'll be auto-synced to
///    joining clients — no manual spawning needed.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class ResourceNode : NetworkBehaviour
{
    // ───────────────────────── Configuration (set in Inspector) ─────────────────────────

    [Header("Resource Settings")]
    [Tooltip("The item ID granted on each hit (must match an ItemDefinition.itemId, e.g. 'wood').")]
    [SerializeField] private string resourceType = "wood";

    [Tooltip("How many items the player receives per successful hit.")]
    [SerializeField] private int amountPerHit = 1;

    [Tooltip("Starting health of this node. When it reaches 0, the node is depleted and despawned.")]
    [SerializeField] private int maxHealth = 5;

    [Header("Tool Requirement")]
    [Tooltip("Tag the player's equipped tool must have to gather this resource. " +
             "Leave empty if no tool is required (e.g. fiber bushes can be gathered by hand). " +
             "Validated BOTH client-side (for UX) and server-side (for security).")]
    [SerializeField] private string requiredToolTag = "";

    [Header("Hit Cooldown")]
    [Tooltip("Minimum seconds between hits from the same player (prevents spam-clicking).")]
    [SerializeField] private float hitCooldown = 0.5f;

    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// Current health of the node. Only the server modifies this.
    /// Clients read it for visual feedback (health bars, damage effects, etc.).
    /// NetworkVariable default: ServerWrite, EveryoneRead.
    /// </summary>
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        0, // Initial value — overwritten in OnNetworkSpawn by server
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server-side cooldown tracking per player
    private readonly System.Collections.Generic.Dictionary<ulong, float> _lastHitTime
        = new System.Collections.Generic.Dictionary<ulong, float>();

    // ───────────────────────── Public Accessors ─────────────────────────

    /// <summary>
    /// The tool tag required to gather this node.
    /// Read by PlayerGathering for client-side pre-validation.
    /// </summary>
    public string RequiredToolTag => requiredToolTag;

    /// <summary>
    /// The resource item ID this node grants. Useful for UI tooltips.
    /// </summary>
    public string ResourceType => resourceType;

    /// <summary>
    /// Max health of this node. Useful for UI health bar calculation.
    /// </summary>
    public int MaxHealth => maxHealth;

    // ───────────────────────── Lifecycle ─────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Only the server sets the initial health value.
        // Clients will receive this via NetworkVariable replication.
        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;
            Debug.Log($"[ResourceNode] '{resourceType}' spawned with {maxHealth} HP " +
                      $"(NetworkObjectId={NetworkObjectId})");
        }

        // ALL peers can subscribe to health changes for visual feedback
        CurrentHealth.OnValueChanged += OnHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        CurrentHealth.OnValueChanged -= OnHealthChanged;
    }

    // ───────────────────────── ServerRpc — Hit Handler ─────────────────────────

    /// <summary>
    /// Called BY CLIENTS via PlayerGathering when they interact with this node.
    /// Runs ON THE SERVER. Validates the hit, reduces health, grants resources,
    /// and despawns the NetworkObject if depleted.
    ///
    /// RequireOwnership = false because ANY client can hit any resource node
    /// (they don't own the node — the server does for in-scene objects).
    /// </summary>
    /// <param name="hitterClientId">The OwnerClientId of the player who hit this node.</param>
    /// <param name="equippedToolTag">The tool tag the client claims to have equipped.
    /// The server re-validates this against PlayerGathering on the server's copy.</param>
    [ServerRpc(RequireOwnership = false)]
    public void HitServerRpc(ulong hitterClientId, string equippedToolTag, ServerRpcParams rpcParams = default)
    {
        // ── Security: verify the caller matches the claimed hitter ──
        if (rpcParams.Receive.SenderClientId != hitterClientId)
        {
            Debug.LogWarning($"[ResourceNode] Client {rpcParams.Receive.SenderClientId} claimed to be " +
                             $"client {hitterClientId} — RPC rejected (spoofed ID).");
            return;
        }

        // ── Check if node is already depleted ──
        if (CurrentHealth.Value <= 0)
        {
            Debug.Log($"[ResourceNode] '{resourceType}' already depleted — ignoring hit from client {hitterClientId}.");
            return;
        }

        // ── Cooldown check (prevents rapid-fire hits) ──
        float now = Time.time;
        if (_lastHitTime.TryGetValue(hitterClientId, out float lastTime))
        {
            if (now - lastTime < hitCooldown)
            {
                return; // Silently ignore — too soon
            }
        }
        _lastHitTime[hitterClientId] = now;

        // ── Server-side tool validation ──
        // Even though the client already checked, we re-validate here
        // to prevent modified clients from bypassing the requirement.
        if (!string.IsNullOrEmpty(requiredToolTag))
        {
            // Validate the claimed tool tag
            if (equippedToolTag != requiredToolTag)
            {
                Debug.LogWarning($"[ResourceNode] Client {hitterClientId} tried to hit '{resourceType}' " +
                                 $"with tool '{equippedToolTag}' but requires '{requiredToolTag}' — rejected.");
                return;
            }

            // Additional server-side verification: check the server's copy of
            // the player's PlayerGathering to confirm they actually have the right tool
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(hitterClientId, out var client))
            {
                var gathering = client.PlayerObject?.GetComponent<PlayerGathering>();
                if (gathering != null && gathering.GetEquippedToolTag() != requiredToolTag)
                {
                    Debug.LogWarning($"[ResourceNode] Server-side tool check failed for client {hitterClientId}. " +
                                     $"Claimed '{equippedToolTag}' but server sees '{gathering.GetEquippedToolTag()}'.");
                    return;
                }
            }
        }

        // ── Reduce health ──
        CurrentHealth.Value -= 1;
        Debug.Log($"[ResourceNode] '{resourceType}' hit by client {hitterClientId}. " +
                  $"Health: {CurrentHealth.Value}/{maxHealth}");

        // ── Grant resource to hitter's inventory ──
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(hitterClientId, out var hitterClient))
        {
            var inventory = hitterClient.PlayerObject?.GetComponent<Inventory>();
            if (inventory != null)
            {
                // This runs on the server, so we call AddItem directly (not via RPC)
                inventory.AddItem(resourceType, amountPerHit);
            }
            else
            {
                Debug.LogError($"[ResourceNode] Client {hitterClientId}'s player has no Inventory component!");
            }
        }
        else
        {
            Debug.LogError($"[ResourceNode] Client {hitterClientId} not found in ConnectedClients!");
        }

        // ── Despawn if depleted ──
        if (CurrentHealth.Value <= 0)
        {
            Debug.Log($"[ResourceNode] '{resourceType}' depleted — despawning NetworkObject.");

            // Notify all clients before despawning (for VFX/SFX)
            OnDepletedClientRpc();

            // Despawn the NetworkObject. For in-scene objects, this hides it
            // from all clients. It can be re-spawned later if needed.
            // destroy: false keeps the GameObject alive on the server for potential respawning.
            NetworkObject.Despawn(destroy: false);
        }
    }

    // ───────────────────────── ClientRpc — Visual Feedback ─────────────────────────

    /// <summary>
    /// Sent by the server to ALL clients when the node is depleted.
    /// Clients can use this to play destruction VFX, particles, sound, etc.
    /// </summary>
    [ClientRpc]
    private void OnDepletedClientRpc()
    {
        Debug.Log($"[ResourceNode] '{resourceType}' depleted — playing destruction feedback.");

        // TODO: Play destruction particle effect
        // TODO: Play destruction sound effect
        // For now, we just log it. The NetworkObject despawn will handle visibility.
    }

    // ───────────────────────── Event Handlers ─────────────────────────

    /// <summary>
    /// Called on ALL peers when CurrentHealth changes (via NetworkVariable replication).
    /// Use for visual feedback like shaking the tree, showing damage numbers, etc.
    /// </summary>
    private void OnHealthChanged(int previousValue, int newValue)
    {
        // Only play feedback if health decreased (not on initial spawn)
        if (newValue < previousValue && previousValue > 0)
        {
            Debug.Log($"[ResourceNode] '{resourceType}' took damage: {previousValue} → {newValue}");

            // TODO: Play hit animation / shake effect
            // TODO: Show floating damage number
            // TODO: Play hit sound
        }
    }
}
