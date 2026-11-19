using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor tool that safely adds the AxeSwing state and transitions to
/// PlayerAnimationController without hand-editing Unity YAML.
///
/// Usage: Tools > OakHaven > Add Axe Swing to Player Animator
/// </summary>
public static class AxeSwingAnimatorSetup
{
    private const string ControllerPath =
        "Assets/ImportedAssets/ThirdPartyAssets/Characters/AnimationController/PlayerAnimationController.controller";

    private const string AxeSwingClipPath =
        "Assets/ImportedAssets/ThirdPartyAssets/Characters/Animations/AxeSwing.anim";

    [MenuItem("Tools/OakHaven/Add Axe Swing to Player Animator")]
    public static void AddAxeSwing()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[AxeSwingAnimatorSetup] Could not load controller at '{ControllerPath}'.");
            return;
        }

        // ── Parameter ──
        if (controller.parameters.All(p => p.name != "AxeSwing"))
        {
            controller.AddParameter("AxeSwing", AnimatorControllerParameterType.Trigger);
        }

        // ── Layer & state machine ──
        AnimatorControllerLayer upperBody = controller.layers.First(l => l.name == "UpperBody");
        AnimatorStateMachine sm = upperBody.stateMachine;

        AnimatorState emptyState = sm.states.First(s => s.state.name == "Empty").state;
        AnimatorState punchState = sm.states.First(s => s.state.name == "Punch").state;

        // ── AxeSwing state ──
        AnimatorState axeSwingState = sm.states.FirstOrDefault(s => s.state.name == "AxeSwing").state;
        if (axeSwingState == null)
        {
            axeSwingState = sm.AddState("AxeSwing");
        }

        var axeMotion = AssetDatabase.LoadAssetAtPath<Motion>(AxeSwingClipPath);
        if (axeMotion == null)
        {
            Debug.LogError($"[AxeSwingAnimatorSetup] Could not load clip at '{AxeSwingClipPath}'.");
            return;
        }

        axeSwingState.motion = axeMotion;
        axeSwingState.writeDefaultValues = true;

        // ── Any State -> AxeSwing ──
        AnimatorStateTransition anyAxe = sm.anyStateTransitions
            .FirstOrDefault(t => t.destinationState == axeSwingState);
        if (anyAxe == null)
        {
            anyAxe = sm.AddAnyStateTransition(axeSwingState);
        }
        anyAxe.conditions = new[]
        {
            new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "AxeSwing", threshold = 0 },
            new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "AxeEquipped", threshold = 0 },
            new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "isGrounded", threshold = 0 },
            new AnimatorCondition { mode = AnimatorConditionMode.Less, parameter = "Speed", threshold = 5.5f }
        };
        anyAxe.hasExitTime = false;
        anyAxe.hasFixedDuration = true;
        anyAxe.duration = 0.25f;
        anyAxe.canTransitionToSelf = false;

        // ── AxeSwing -> Empty ──
        AnimatorStateTransition axeExit = axeSwingState.transitions
            .FirstOrDefault(t => t.destinationState == emptyState);
        if (axeExit == null)
        {
            axeExit = axeSwingState.AddTransition(emptyState);
        }
        axeExit.conditions = System.Array.Empty<AnimatorCondition>();
        axeExit.hasExitTime = true;
        axeExit.exitTime = 0.85f;
        axeExit.hasFixedDuration = true;
        axeExit.duration = 0.2f;
        axeExit.canTransitionToSelf = true;

        // ── Punch Any State transition: only when axe is NOT equipped ──
        AnimatorStateTransition punchAny = sm.anyStateTransitions
            .FirstOrDefault(t => t.destinationState == punchState);
        if (punchAny != null)
        {
            punchAny.conditions = new[]
            {
                new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "Punch", threshold = 0 },
                new AnimatorCondition { mode = AnimatorConditionMode.IfNot, parameter = "AxeEquipped", threshold = 0 },
                new AnimatorCondition { mode = AnimatorConditionMode.If, parameter = "isGrounded", threshold = 0 },
                new AnimatorCondition { mode = AnimatorConditionMode.Less, parameter = "Speed", threshold = 5.5f }
            };
            punchAny.hasExitTime = false;
            punchAny.hasFixedDuration = true;
            punchAny.duration = 0.25f;
            punchAny.canTransitionToSelf = false;
        }

        // Ensure layer changes are persisted
        var layers = controller.layers;
        int idx = System.Array.FindIndex(layers, l => l.name == "UpperBody");
        if (idx >= 0)
        {
            layers[idx] = upperBody;
            controller.layers = layers;
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        Debug.Log("[AxeSwingAnimatorSetup] AxeSwing state and transitions added successfully. " +
                  "Remember to open NetworkPlayer.prefab and click the NetworkAnimator to rebuild its transition list.");
    }
}
