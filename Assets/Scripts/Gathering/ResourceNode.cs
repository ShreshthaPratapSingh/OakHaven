using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A gatherable resource node (tree, rock, bush) placed in-scene as a NetworkObject.
///
/// NETWORKING FLOW:
/// ─ currentHealth is a NetworkVariable<int> that auto-replicates to all clients.
///   Only the server can write to it (default write permission = Server).
/// ─ The client is responsible only for aiming, swinging, and reporting which node
///   it is trying to hit at the impact frame.
/// ─ ServerApplyHit() is called BY THE SERVER after the player's gathering code
///   has validated range, angle, line-of-sight, and equipped tool.
/// ─ The server validates everything: tool requirement, cooldown, inventory space,
///   then reduces health and grants resources.
///
/// TOOL VALIDATION:
/// ─ requiredToolTag is checked server-side only. Set it in the Inspector for each
///   node (e.g. "Axe" for trees). Leave it empty for hand-gatherable resources.
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
             "Validated server-side.")]
    [SerializeField] private string requiredToolTag = "";

    [Header("Hit Cooldown")]
    [Tooltip("Minimum seconds between hits from the same player (prevents rapid-fire hits).")]
    [SerializeField] private float hitCooldown = 0.5f;

    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// Current health of the node. Only the server modifies this.
    /// Clients read it for visual feedback (health bars, damage effects, etc.).
    /// NetworkVariable default: ServerWrite, EveryoneRead.
    /// </summary>
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server-side cooldown tracking per player
    private readonly System.Collections.Generic.Dictionary<ulong, float> _lastHitTime
        = new System.Collections.Generic.Dictionary<ulong, float>();

    // ───────────────────────── Public Accessors ─────────────────────────

    /// <summary>
    /// The tool tag required to gather this resource.
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

        if (IsServer)
        {
            CurrentHealth.Value = maxHealth;
            Debug.Log($"[ResourceNode] '{resourceType}' spawned with {maxHealth} HP " +
                      $"(NetworkObjectId={NetworkObjectId})");
        }

        CurrentHealth.OnValueChanged += OnHealthChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        CurrentHealth.OnValueChanged -= OnHealthChanged;
    }

    // ───────────────────────── Server-Side Hit Application ─────────────────────────

    /// <summary>
    /// Called by PlayerGathering on the server after aim, range, angle, and
    /// line-of-sight have been validated. This is the ONLY place that should
    /// reduce node health or grant resources.
    /// </summary>
    public void ServerApplyHit(ulong hitterClientId, string toolTag)
    {
        if (!IsServer) return;

        // Cooldown check
        float now = Time.time;
        if (_lastHitTime.TryGetValue(hitterClientId, out float lastTime))
        {
            if (now - lastTime < hitCooldown)
            {
                Debug.Log($"[ResourceNode] '{resourceType}' hit ignored from client {hitterClientId} — cooldown.");
                return;
            }
        }
        _lastHitTime[hitterClientId] = now;

        if (CurrentHealth.Value <= 0)
        {
            Debug.Log($"[ResourceNode] '{resourceType}' already depleted — ignoring hit.");
            return;
        }

        // Tool requirement validation
        if (!string.IsNullOrEmpty(requiredToolTag))
        {
            if (toolTag != requiredToolTag)
            {
                Debug.LogWarning($"[ResourceNode] Client {hitterClientId} tried to hit '{resourceType}' " +
                                 $"with tool '{toolTag}' but requires '{requiredToolTag}' — rejected.");
                return;
            }
        }

        // Grant resource to the hitting player's ResourceInventory
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(hitterClientId, out var hitterClient))
        {
            var resourceInv = hitterClient.PlayerObject?.GetComponent<ResourceInventory>();
            if (resourceInv == null)
            {
                Debug.LogError($"[ResourceNode] Client {hitterClientId}'s player has no ResourceInventory component!");
                return;
            }

            int added = resourceInv.AddItem(resourceType, amountPerHit);
            if (added == 0)
            {
                Debug.Log($"[ResourceNode] '{resourceType}' hit by client {hitterClientId} " +
                          $"but player's resource inventory is full — node not damaged.");
                return;
            }

            CurrentHealth.Value -= 1;
            Debug.Log($"[ResourceNode] '{resourceType}' hit by client {hitterClientId}. " +
                      $"Granted {added}/{amountPerHit}. Health: {CurrentHealth.Value}/{maxHealth}");
        }
        else
        {
            Debug.LogError($"[ResourceNode] Client {hitterClientId} not found in ConnectedClients!");
            return;
        }

        // Despawn if depleted
        if (CurrentHealth.Value <= 0)
        {
            Debug.Log($"[ResourceNode] '{resourceType}' depleted — despawning NetworkObject.");
            OnDepletedClientRpc();
            NetworkObject.Despawn(destroy: true);
        }
    }

    // ───────────────────────── ClientRpc — Visual Feedback ─────────────────────────

    /// <summary>
    /// Sent by the server to ALL clients when the node is depleted.
    /// </summary>
    [ClientRpc]
    private void OnDepletedClientRpc()
    {
        Debug.Log($"[ResourceNode] '{resourceType}' depleted — playing destruction feedback.");
        // TODO: Play destruction particle effect / sound
    }

    // ───────────────────────── Event Handlers ─────────────────────────

    /// <summary>
    /// Called on ALL peers when CurrentHealth changes.
    /// </summary>
    private void OnHealthChanged(int previousValue, int newValue)
    {
        if (newValue < previousValue && previousValue > 0)
        {
            Debug.Log($"[ResourceNode] '{resourceType}' took damage: {previousValue} → {newValue}");
            // TODO: Play hit animation / shake effect / sound / floating damage number
        }
    }
}
