using UnityEngine;

public class WorkbenchVisuals : MonoBehaviour
{
    [SerializeField] private Workstation workstation;
    [SerializeField] private GameObject damagedVisual;
    [SerializeField] private GameObject repairedVisual;

    private void Awake()
    {
        if (workstation == null)
        {
            workstation = GetComponent<Workstation>();
        }
    }

    private void OnEnable()
    {
        if (workstation != null)
        {
            workstation.IsRepaired.OnValueChanged += HandleRepairedChanged;
            HandleRepairedChanged(workstation.IsRepaired.Value, workstation.IsRepaired.Value);
        }
    }

    private void OnDisable()
    {
        if (workstation != null)
        {
            workstation.IsRepaired.OnValueChanged -= HandleRepairedChanged;
        }
    }

    private void HandleRepairedChanged(bool previousValue, bool newValue)
    {
        if (damagedVisual != null)
        {
            damagedVisual.SetActive(!newValue);
        }
        if (repairedVisual != null)
        {
            repairedVisual.SetActive(newValue);
        }
    }
}
