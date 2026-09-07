#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Feeds PlayerController from Input System actions instead of raw device polling, so bindings
/// can be edited in the .inputactions asset (rebinding, control schemes, touch, etc.).
/// By default it uses the project-wide actions asset (Edit > Project Settings > Input System Package),
/// which in a fresh Unity 6 project is InputSystem_Actions with a "Player" map containing "Move" and "Jump".
/// Remove this component to fall back to the controller's built-in keyboard/gamepad polling.
/// </summary>
[RequireComponent(typeof(PlayerController))]
[DisallowMultipleComponent]
public class InputActionsSource : MonoBehaviour, PlayerController.IInputSource
{
    [Tooltip("Leave empty to use the project-wide actions asset.")]
    public InputActionAsset actions;
    public string actionMap = "Player";
    public string moveAction = "Move";     // Vector2: WASD / arrows / left stick / d-pad
    public string jumpAction = "Jump";     // Button: Space / gamepad south

    InputAction move, jump;

    void Awake()
    {
        var asset = actions != null ? actions : InputSystem.actions;
        if (asset == null)
        {
            Debug.LogWarning($"{name}: no InputActionAsset assigned and no project-wide actions set; using device polling instead.", this);
            enabled = false;
            return;
        }
        var map = asset.FindActionMap(actionMap, throwIfNotFound: false);
        move = map?.FindAction(moveAction, throwIfNotFound: false);
        jump = map?.FindAction(jumpAction, throwIfNotFound: false);
        if (move == null || jump == null)
        {
            Debug.LogWarning($"{name}: actions '{actionMap}/{moveAction}' or '{actionMap}/{jumpAction}' not found in {asset.name}; using device polling instead.", this);
            enabled = false;
            return;
        }
        GetComponent<PlayerController>().InputSource = this;
    }

    void OnEnable()
    {
        move?.Enable();
        jump?.Enable();
    }

    void OnDisable()
    {
        var controller = GetComponent<PlayerController>();
        if (controller != null && ReferenceEquals(controller.InputSource, this)) controller.InputSource = null;
    }

    public PlayerController.InputState Read()
    {
        Vector2 v = move.ReadValue<Vector2>();
        return new PlayerController.InputState
        {
            Horizontal = Mathf.Clamp(v.x, -1f, 1f),
            JumpHeld = jump.IsPressed(),
            DownHeld = v.y < -0.5f,
        };
    }
}
#endif
