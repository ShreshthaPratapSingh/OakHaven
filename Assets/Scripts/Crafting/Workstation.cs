using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked workstation placed in the scene. Starts broken and must be repaired
/// using resources before players can craft with it.
///
/// NETWORKING FLOW:
/// ─ isRepaired is a NetworkVariable<bool> that gates all crafting.
/// ─ repairContributions is a NetworkList<InventoryItem> tracking deposited resources.
/// ─ Any player can contribute to repair or craft via RequireOwnership=false ServerRpcs.
/// ─ The server validates everything: sender identity, proximity, inventory contents.
///
/// EDITOR SETUP:
/// 1. Place a GameObject in the scene (cube placeholder is fine).
/// 2. Add NetworkObject + Workstation components.
/// 3. Assign a WorkingStationDefinition asset in the Inspector.
/// 4. Set interactRange (default 4m).
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class Workstation : NetworkBehaviour
{
    // ───────────────────────── Configuration ─────────────────────────

    [Header("Workstation Settings")]
    [Tooltip("Reference to the WorkingStationDefinition ScriptableObject. " +
             "Defines repair costs and available recipes.")]
    [SerializeField] private WorkingStationDefinition workstationDefinition;

    [Tooltip("Maximum distance (meters) a player can be from this workstation to interact.")]
    [SerializeField] private float interactRange = 4f;

    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// Whether this workstation has been fully repaired.
    /// Server-write, everyone-read. Gates all crafting operations.
    /// </summary>
    public NetworkVariable<bool> IsRepaired = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Tracks resources deposited toward repair. Reuses InventoryItem struct
    /// (FixedString32Bytes ItemId + int Quantity) for network serialization.
    /// Server-write, everyone-read. Clients use this for repair progress UI.
    /// </summary>
    public NetworkList<InventoryItem> RepairContributions { get; private set; }

    // ───────────────────────── Public Accessors ─────────────────────────

    /// <summary>
    /// The workstation definition asset. Used by UI to display repair costs and recipes.
    /// </summary>
    public WorkingStationDefinition Definition => workstationDefinition;

    /// <summary>
    /// Interaction range. Used by PlayerCrafting for proximity detection.
    /// </summary>
    public float InteractRange => interactRange;

    // ───────────────────────── Lifecycle ─────────────────────────

    private void Awake()
    {
        // NetworkList MUST be created before OnNetworkSpawn (same pattern as BaseInventory)
        RepairContributions = new NetworkList<InventoryItem>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            Debug.Log($"[Workstation] '{workstationDefinition?.displayName}' spawned " +
                      $"(NetworkObjectId={NetworkObjectId}, repaired={IsRepaired.Value})");
        }

        // ALL peers can subscribe for visual state changes
        IsRepaired.OnValueChanged += OnRepairedChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        IsRepaired.OnValueChanged -= OnRepairedChanged;
    }

    // ───────────────────────── ServerRpc — Repair Contribution ─────────────────────────

    /// <summary>
    /// Called by any client to contribute resources toward repairing this workstation.
    /// Server validates sender, proximity, resource need, and inventory before accepting.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void ContributeRepairServerRpc(ulong contributorClientId, string itemId, int amount,
        ServerRpcParams rpcParams = default)
    {
        // ── Security: verify sender matches claimed contributor ──
        if (rpcParams.Receive.SenderClientId != contributorClientId)
        {
            Debug.LogWarning($"[Workstation] Client {rpcParams.Receive.SenderClientId} claimed to be " +
                             $"client {contributorClientId} — RPC rejected (spoofed ID).");
            return;
        }

        // ── Reject if already repaired ──
        if (IsRepaired.Value)
        {
            Debug.Log($"[Workstation] Already repaired — ignoring contribution from client {contributorClientId}.");
            return;
        }

        if (workstationDefinition == null)
        {
            Debug.LogError("[Workstation] No WorkingStationDefinition assigned!");
            return;
        }

        // ── Validate this resource is actually needed for repair ──
        int totalNeeded = 0;
        foreach (var cost in workstationDefinition.repairCosts)
        {
            if (cost.itemId == itemId)
            {
                totalNeeded = cost.quantity;
                break;
            }
        }

        if (totalNeeded <= 0)
        {
            Debug.Log($"[Workstation] '{itemId}' is not needed for repair — rejected.");
            return;
        }

        // ── Calculate how much has already been contributed ──
        int alreadyContributed = 0;
        int existingIndex = -1;
        for (int i = 0; i < RepairContributions.Count; i++)
        {
            if (RepairContributions[i].ItemId.ToString() == itemId)
            {
                alreadyContributed = RepairContributions[i].Quantity;
                existingIndex = i;
                break;
            }
        }

        int stillNeeded = totalNeeded - alreadyContributed;
        if (stillNeeded <= 0)
        {
            Debug.Log($"[Workstation] '{itemId}' already fully contributed — rejected.");
            return;
        }

        // ── Clamp to what's still needed ──
        int actualAmount = Mathf.Min(amount, stillNeeded);

        // ── Proximity check ──
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(contributorClientId, out var client))
        {
            Debug.LogError($"[Workstation] Client {contributorClientId} not found in ConnectedClients!");
            return;
        }

        float dist = Vector3.Distance(client.PlayerObject.transform.position, transform.position);
        if (dist > interactRange + 1f) // +1f buffer for network latency
        {
            Debug.Log($"[Workstation] Client {contributorClientId} too far ({dist:F1}m > {interactRange}m) — rejected.");
            return;
        }

        // ── Remove resources from contributor's ResourceInventory ──
        var resourceInv = client.PlayerObject.GetComponent<ResourceInventory>();
        if (resourceInv == null)
        {
            Debug.LogError($"[Workstation] Client {contributorClientId}'s player has no ResourceInventory!");
            return;
        }

        if (!resourceInv.RemoveItem(itemId, actualAmount))
        {
            Debug.Log($"[Workstation] Client {contributorClientId} doesn't have {actualAmount}x {itemId} — rejected.");
            return;
        }

        // ── Update repair contributions ──
        if (existingIndex >= 0)
        {
            // Stack onto existing entry
            InventoryItem updated = RepairContributions[existingIndex];
            updated.Quantity += actualAmount;
            RepairContributions[existingIndex] = updated;
        }
        else
        {
            // New entry
            RepairContributions.Add(new InventoryItem(itemId, actualAmount));
        }

        Debug.Log($"[Workstation] Client {contributorClientId} contributed {actualAmount}x {itemId}. " +
                  $"Progress: {alreadyContributed + actualAmount}/{totalNeeded}");

        // ── Check if fully repaired ──
        if (CheckRepairComplete())
        {
            IsRepaired.Value = true;
            Debug.Log($"[Workstation] '{workstationDefinition.displayName}' is now fully repaired!");
            OnRepairedClientRpc();
        }
    }

    // ───────────────────────── ServerRpc — Craft Request ─────────────────────────

    /// <summary>
    /// Called by any client to request crafting a recipe at this workstation.
    /// Server validates everything before deducting ingredients and granting output.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void RequestCraftServerRpc(ulong crafterClientId, string recipeId,
        ServerRpcParams rpcParams = default)
    {
        // ── Security: verify sender ──
        if (rpcParams.Receive.SenderClientId != crafterClientId)
        {
            Debug.LogWarning($"[Workstation] Client {rpcParams.Receive.SenderClientId} claimed to be " +
                             $"client {crafterClientId} — craft RPC rejected.");
            return;
        }

        // ── Must be repaired ──
        if (!IsRepaired.Value)
        {
            Debug.Log($"[Workstation] Not repaired — craft request from client {crafterClientId} rejected.");
            return;
        }

        if (workstationDefinition == null)
        {
            Debug.LogError("[Workstation] No WorkingStationDefinition assigned!");
            return;
        }

        // ── Find the recipe ──
        CraftingRecipe recipe = null;
        foreach (var r in workstationDefinition.availableRecipes)
        {
            if (r != null && r.recipeId == recipeId)
            {
                recipe = r;
                break;
            }
        }

        if (recipe == null)
        {
            Debug.LogWarning($"[Workstation] Recipe '{recipeId}' not found on this workstation — rejected.");
            return;
        }

        // ── Proximity check ──
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(crafterClientId, out var client))
        {
            Debug.LogError($"[Workstation] Client {crafterClientId} not found!");
            return;
        }

        float dist = Vector3.Distance(client.PlayerObject.transform.position, transform.position);
        if (dist > interactRange + 1f)
        {
            Debug.Log($"[Workstation] Client {crafterClientId} too far to craft — rejected.");
            return;
        }

        // ── Get player inventories ──
        var resourceInv = client.PlayerObject.GetComponent<ResourceInventory>();
        var equipmentInv = client.PlayerObject.GetComponent<Inventory>();

        if (resourceInv == null || equipmentInv == null)
        {
            Debug.LogError($"[Workstation] Client {crafterClientId} missing inventory components!");
            return;
        }

        // ── Check all ingredients ──
        foreach (var ingredient in recipe.ingredients)
        {
            if (!resourceInv.HasItem(ingredient.itemId, ingredient.quantity))
            {
                Debug.Log($"[Workstation] Client {crafterClientId} missing {ingredient.quantity}x " +
                          $"{ingredient.itemId} for recipe '{recipeId}' — rejected.");
                CraftFailedClientRpc(crafterClientId, ingredient.itemId, ingredient.quantity);
                return;
            }
        }

        // ── Deduct all ingredients ──
        foreach (var ingredient in recipe.ingredients)
        {
            resourceInv.RemoveItem(ingredient.itemId, ingredient.quantity);
        }

        // ── Grant crafted output to equipment inventory ──
        int added = equipmentInv.AddItem(recipe.outputItemId, recipe.outputQuantity);

        Debug.Log($"[Workstation] Client {crafterClientId} crafted {added}x {recipe.outputItemId} " +
                  $"via recipe '{recipeId}'.");

        CraftSuccessClientRpc(crafterClientId, recipe.outputItemId, added);
    }

    // ───────────────────────── Server Helpers ─────────────────────────

    /// <summary>
    /// Checks if all repair costs have been met by comparing contributions to definition.
    /// SERVER-ONLY.
    /// </summary>
    private bool CheckRepairComplete()
    {
        if (workstationDefinition == null) return false;

        foreach (var cost in workstationDefinition.repairCosts)
        {
            int contributed = 0;
            for (int i = 0; i < RepairContributions.Count; i++)
            {
                if (RepairContributions[i].ItemId.ToString() == cost.itemId)
                {
                    contributed = RepairContributions[i].Quantity;
                    break;
                }
            }

            if (contributed < cost.quantity)
            {
                return false; // This resource isn't fully contributed yet
            }
        }

        return true; // All costs met
    }

    // ───────────────────────── ClientRpcs ─────────────────────────

    /// <summary>
    /// Notifies all clients when the workstation is repaired.
    /// Clients can use this for celebration VFX/SFX.
    /// </summary>
    [ClientRpc]
    private void OnRepairedClientRpc()
    {
        Debug.Log($"[Workstation] '{workstationDefinition?.displayName}' has been repaired!");
        // TODO: Play repair complete VFX/SFX
    }

    /// <summary>
    /// Notifies the crafting player that their craft succeeded.
    /// </summary>
    [ClientRpc]
    private void CraftSuccessClientRpc(ulong crafterClientId, string outputItemId, int amount)
    {
        if (NetworkManager.Singleton.LocalClientId != crafterClientId) return;

        Debug.Log($"[Workstation] Successfully crafted {amount}x {outputItemId}!");
        // TODO: Play craft success SFX, show UI feedback
    }

    /// <summary>
    /// Notifies the crafting player that their craft failed due to missing ingredients.
    /// </summary>
    [ClientRpc]
    private void CraftFailedClientRpc(ulong crafterClientId, string missingItemId, int missingAmount)
    {
        if (NetworkManager.Singleton.LocalClientId != crafterClientId) return;

        Debug.Log($"[Workstation] Craft failed — need {missingAmount}x {missingItemId}!");
        // TODO: Show UI feedback for missing ingredients
    }

    // ───────────────────────── Visual State Callback ─────────────────────────

    /// <summary>
    /// Called on ALL peers when IsRepaired changes.
    /// Use to swap visual appearance between broken and functional.
    /// </summary>
    private void OnRepairedChanged(bool previousValue, bool newValue)
    {
        Debug.Log($"[Workstation] Repair state changed: {previousValue} → {newValue}");
        // TODO: Swap visual mesh/material (broken → repaired)
        // TODO: Enable/disable particle effects
    }
}
