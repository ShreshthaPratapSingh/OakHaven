using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Equips a held item (e.g. stone axe) into the player's hand.
///
/// LIVES ON:
/// ─ The player prefab (same GameObject as PlayerController / Inventory).
/// ─ The handSocket is an empty child of the right-hand bone on the visible
///   character model (created by Tools > OakHaven > Setup Equipment).
///
/// NETWORKING FLOW:
/// ─ Input reading happens CLIENT-SIDE (IsOwner only) — same pattern as
///   PlayerCrafting / PlayerGathering.
/// ─ The client only SENDS a request; the server decides. EquipServerRpc
///   validates: sender identity → inventory possession → item existence.
/// ─ Equipped state replicates via NetworkVariable<FixedString32Bytes>
///   (Server-write, Everyone-read). Only the ITEM ID crosses the network —
///   the visual prefab is resolved locally via ItemDatabase on each client.
/// ─ The held visual is a plain local GameObject parented under handSocket.
///   It is NOT a NetworkObject: the player's Animator plays the same pose
///   deterministically on every client, so a socketed child follows the hand
///   everywhere with zero extra network traffic.
///
/// EDITOR SETUP:
/// ─ Run Tools > OakHaven > Setup Equipment (adds this component, creates the
///   hand socket on the character's right hand and wires the reference).
/// ─ Requires ItemDatabaseLoader in the scene (same as inventory/crafting).
/// </summary>
public class PlayerEquipment : NetworkBehaviour
{
    // ───────────────────────── Configuration ─────────────────────────

    [Header("Socket")]
    [Tooltip("Empty child of the right-hand bone. The held item's prefab is parented here.")]
    [SerializeField] private Transform handSocket;

    [Header("Input")]
    [Tooltip("Equip/unequip key. E is taken by workstation interaction.")]
    [SerializeField] private KeyCode equipKey = KeyCode.Q;

    // ───────────────────────── Networked State ─────────────────────────

    /// <summary>
    /// The currently equipped item's id ("" = nothing). Server-authoritative.
    /// FixedString32Bytes matches InventoryItem.ItemId — item ids are ≤32 chars.
    /// </summary>
    private readonly NetworkVariable<FixedString32Bytes> _equippedItemId =
        new NetworkVariable<FixedString32Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );

    // ───────────────────────── Local State (never networked) ─────────────────────────

    /// <summary>The locally instantiated visual. Cosmetic only.</summary>
    private GameObject _heldInstance;

    /// <summary>The player's equipment inventory (tools live there, not in ResourceInventory).</summary>
    private Inventory _equipment;

    // ───────────────────────── Public Accessors ─────────────────────────

    /// <summary>Currently equipped item id, or "" if nothing is held. Runs on ANY peer.</summary>
    public string EquippedItemId => _equippedItemId.Value.ToString();

    /// <summary>True if this player is holding something. Runs on ANY peer.</summary>
    public bool IsEquipped => !_equippedItemId.Value.IsEmpty;

    // ───────────────────────── Lifecycle ─────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _equipment = GetComponent<Inventory>();

        // Visuals are EVERYONE's business (other players must see the axe),
        // so the subscription is not gated behind IsOwner.
        _equippedItemId.OnValueChanged += OnEquippedChanged;

        // OnValueChanged only fires on CHANGES — a late-joining client (or a
        // respawn) never receives an event for state that already exists,
        // so apply the current value once here.
        UpdateHeldVisual();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _equippedItemId.OnValueChanged -= OnEquippedChanged;

        if (_heldInstance != null)
        {
            Destroy(_heldInstance);
            _heldInstance = null;
        }
    }

    // ───────────────────────── Input (Owner Client Only) ─────────────────────────

    private void Update()
    {
        // Update runs before network spawn too — IsOwner can misreport against
        // a default OwnerClientId in that window, so guard with IsSpawned.
        if (!IsSpawned || !IsOwner) return;

        if (!Input.GetKeyDown(equipKey)) return;

        if (_equippedItemId.Value.IsEmpty)
        {
            string candidate = FindFirstEquippableItemId();
            if (candidate != null)
            {
                EquipServerRpc(candidate);
            }
            else
            {
                Debug.Log("[Equipment] Nothing equippable in the equipment inventory.");
            }
        }
        else
        {
            UnequipServerRpc();
        }
    }

    // ───────────────────────── Candidate Scan (Suggestion Only) ─────────────────────────

    /// <summary>
    /// First item in the equipment inventory that has an equippedPrefab.
    /// Runs on the owner client reading REPLICATED data (safe on any peer).
    /// This is only a SUGGESTION of what to request — the server re-validates
    /// everything in EquipServerRpc before granting it.
    /// </summary>
    private string FindFirstEquippableItemId()
    {
        for (int i = 0; i < _equipment.Items.Count; i++)
        {
            string id = _equipment.Items[i].ItemId.ToString();
            ItemDefinition def = ItemDatabase.GetItem(id);
            if (def != null && def.equippedPrefab != null)
                return id;
        }
        return null;
    }

    // ───────────────────────── ServerRpcs (Server Validates Everything) ─────────────────────────

    /// <summary>
    /// Requests equipping an item. Server validates the full chain:
    /// 1. Sender identity — only the owner may equip on this player
    ///    (same check as Inventory.AddItemServerRpc).
    /// 2. Possession — the player really holds it in the equipment inventory
    ///    (checked against the server's authoritative copy of the list).
    /// 3. Existence — the id resolves via ItemDatabase and the item really
    ///    is equippable (never trust client-sent ids).
    /// The item STAYS in the inventory — equipping is just a held flag.
    /// </summary>
    [ServerRpc]
    private void EquipServerRpc(string itemId, ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Equipment] Client {rpcParams.Receive.SenderClientId} tried to equip " +
                             $"on client {OwnerClientId}'s player — rejected!");
            return;
        }

        if (string.IsNullOrEmpty(itemId)) return;

        if (_equipment == null || !_equipment.HasItem(itemId, 1))
        {
            Debug.LogWarning($"[Equipment] Equip request for '{itemId}' rejected — " +
                             "not in equipment inventory.");
            return;
        }

        ItemDefinition def = ItemDatabase.GetItem(itemId);
        if (def == null || def.equippedPrefab == null)
        {
            Debug.LogWarning($"[Equipment] Equip request for '{itemId}' rejected — " +
                             "unknown id or no equippedPrefab assigned.");
            return;
        }

        _equippedItemId.Value = new FixedString32Bytes(itemId);
        Debug.Log($"[Equipment] Client {OwnerClientId} equipped '{itemId}'.");
    }

    /// <summary>Requests unequipping. Only the owner, server clears the state.</summary>
    [ServerRpc]
    private void UnequipServerRpc(ServerRpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != OwnerClientId)
        {
            Debug.LogWarning($"[Equipment] Client {rpcParams.Receive.SenderClientId} tried to unequip " +
                             $"client {OwnerClientId}'s player — rejected!");
            return;
        }

        _equippedItemId.Value = default;
        Debug.Log($"[Equipment] Client {OwnerClientId} unequipped.");
    }

    // ───────────────────────── Visuals (Every Peer) ─────────────────────────

    /// <summary>NetworkVariable callback — runs on every peer whenever the server changes the state.</summary>
    private void OnEquippedChanged(FixedString32Bytes previous, FixedString32Bytes current)
    {
        UpdateHeldVisual();
    }

    /// <summary>
    /// Rebuilds the held visual from scratch. Runs on ANY peer.
    /// Destroy-then-recreate is intentional: simpler than diffing, and NGO
    /// doesn't even fire the change event when the value is identical, so
    /// double-equips are a non-issue.
    /// </summary>
    private void UpdateHeldVisual()
    {
        if (_heldInstance != null)
        {
            Destroy(_heldInstance);
            _heldInstance = null;
        }

        string id = _equippedItemId.Value.ToString();
        if (string.IsNullOrEmpty(id)) return;   // nothing equipped

        ItemDefinition def = ItemDatabase.GetItem(id);
        if (def == null || def.equippedPrefab == null)
        {
            Debug.LogWarning($"[Equipment] No equippedPrefab for '{id}' — " +
                             "check the ItemDef and that ItemDatabaseLoader is in the scene.");
            return;
        }

        if (handSocket == null)
        {
            Debug.LogWarning("[Equipment] handSocket not assigned — " +
                             "run Tools > OakHaven > Setup Equipment.");
            return;
        }

        // worldPositionStays = false: the prefab's pivot is authored AT the
        // grip (Phase 0), so it lands exactly on the socket with zero offset.
        _heldInstance = Instantiate(def.equippedPrefab, handSocket, false);
    }
}