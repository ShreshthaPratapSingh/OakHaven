using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Central interaction handler for the local player.
/// Reads the Interact action, finds the nearest eligible WorldPickup or Workstation,
/// shows a world-space prompt, and routes the button press to the selected target.
///
/// NETWORKING FLOW:
/// - Detection, prompt rendering, and input happen only on the owning client.
/// - World pickups call a ServerRpc on the pickup itself.
/// - Workstations route through PlayerCrafting, which keeps the existing UI flow.
///
/// SETUP:
/// 1. Add this component to the player prefab root.
/// 2. Make sure PlayerCrafting is also on the same GameObject.
/// 3. Pickups and workstations need trigger colliders on the layers included in
///    interactableLayers.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PlayerCrafting))]
public class PlayerInteractor : NetworkBehaviour
{
    [Header("Detection")]
    [Tooltip("Radius of the scan sphere centered on the player.")]
    [SerializeField] private float scanRadius = 3f;

    [Tooltip("Layers considered when scanning for interactables.")]
    [SerializeField] private LayerMask interactableLayers = ~0;

    [Header("Prompt")]
    [Tooltip("Height above the target where the prompt appears.")]
    [SerializeField] private float promptHeight = 0.5f;

    private InputSystem_Actions _inputActions;
    private Camera _camera;
    private PlayerCrafting _crafting;
    private Component _currentTarget;
    private GameObject _promptRoot;
    private TextMesh _promptText;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _crafting = GetComponent<PlayerCrafting>();

        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        _inputActions = new InputSystem_Actions();
        _inputActions.Player.Enable();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (_inputActions != null)
        {
            _inputActions.Player.Disable();
            _inputActions.Dispose();
            _inputActions = null;
        }

        if (_promptRoot != null)
        {
            Destroy(_promptRoot);
            _promptRoot = null;
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (_camera == null)
        {
            _camera = Camera.main;
            if (_camera == null) return;
        }

        // Don't show prompts or process interaction while a UI menu has the cursor.
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            HidePrompt();
            return;
        }

        Component target = FindBestTarget();
        _currentTarget = target;

        if (_currentTarget != null)
        {
            ShowPrompt(_currentTarget, GetPromptText(_currentTarget));
        }
        else
        {
            HidePrompt();
        }

        if (_inputActions.Player.Interact.WasPressedThisFrame())
        {
            if (_currentTarget is WorldPickup pickup)
            {
                pickup.RequestPickupServerRpc();
            }
            else if (_currentTarget is Workstation)
            {
                _crafting.OpenCurrentWorkstation();
            }
        }
    }

    /// <summary>
    /// Finds the closest valid interactable (WorldPickup or Workstation) within range.
    /// </summary>
    private Component FindBestTarget()
    {
        Vector3 origin = transform.position + Vector3.up * 1.2f;
        Collider[] hits = Physics.OverlapSphere(
            origin, scanRadius, interactableLayers, QueryTriggerInteraction.Collide);

        Component best = null;
        float bestDist = float.MaxValue;

        foreach (Collider col in hits)
        {
            // Pickups
            WorldPickup pickup = col.GetComponentInParent<WorldPickup>();
            if (pickup != null && IsValidTarget(pickup.transform, pickup.InteractionRange, origin, out float pickupDist))
            {
                if (pickupDist < bestDist)
                {
                    bestDist = pickupDist;
                    best = pickup;
                }
            }

            // Workstations
            Workstation workstation = col.GetComponentInParent<Workstation>();
            if (workstation != null && IsValidTarget(workstation.transform, workstation.InteractRange, origin, out float wsDist))
            {
                if (wsDist < bestDist)
                {
                    bestDist = wsDist;
                    best = workstation;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Checks distance and line-of-sight for a candidate target.
    /// </summary>
    private bool IsValidTarget(Transform target, float range, Vector3 origin, out float distance)
    {
        distance = Vector3.Distance(origin, target.position);
        if (distance > range)
            return false;

        Vector3 targetPos = target.position + Vector3.up * 0.3f;
        if (Physics.Linecast(origin, targetPos, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
        {
            Transform hitRoot = hit.transform;
            if (hitRoot != target && !hitRoot.IsChildOf(target))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Builds the prompt text for the selected target.
    /// </summary>
    private string GetPromptText(Component target)
    {
        if (target is WorldPickup pickup)
        {
            ItemDefinition def = ItemDatabase.GetItem(pickup.ItemId);
            string name = def != null ? def.displayName : pickup.ItemId;
            return $"E - Pick up {name}";
        }

        if (target is Workstation ws)
        {
            var def = ws.Definition;
            string name = def != null ? def.displayName : "Workstation";
            return $"E - Use {name}";
        }

        return "E - Interact";
    }

    /// <summary>
    /// Lazily creates the world-space TextMesh prompt.
    /// </summary>
    private void EnsurePrompt()
    {
        if (_promptRoot != null) return;

        _promptRoot = new GameObject("InteractPrompt");
        _promptRoot.transform.localScale = Vector3.one * 0.04f;
        _promptText = _promptRoot.AddComponent<TextMesh>();
        _promptText.fontSize = 24;
        _promptText.color = Color.white;
        _promptText.anchor = TextAnchor.MiddleCenter;
        _promptText.alignment = TextAlignment.Center;
    }

    private void ShowPrompt(Component target, string text)
    {
        EnsurePrompt();
        _promptRoot.SetActive(true);

        Vector3 worldPos = target.transform.position + Vector3.up * promptHeight;
        _promptRoot.transform.position = worldPos;
        _promptRoot.transform.rotation = Quaternion.LookRotation(worldPos - _camera.transform.position);

        _promptText.text = text;
    }

    private void HidePrompt()
    {
        if (_promptRoot != null)
            _promptRoot.SetActive(false);
    }
}
