using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A complete, self-contained 2D platformer character controller built on Rigidbody2D.
///
/// Drop it on a GameObject that has a Rigidbody2D and any Collider2D (CapsuleCollider2D recommended).
/// Nothing else is required: no other scripts, no sprites, no tags, no layers. It works with the
/// legacy Input Manager AND with the Input System package (Keyboard + Gamepad).
///
/// Feel features:
///   - jump defined by HEIGHT and TIME TO APEX (gravity and take-off speed are derived; the peak is exact)
///   - variable jump height (release early = short hop), faster falling, apex hang, max fall speed
///   - coyote time, jump buffering
///   - double jump (any number of air jumps)
///   - walls: Cling (Blasphemous: grab the wall, scrape slowly down it, jump off or press Down; climb by re-grabbing)
///            or Slide (Celeste: slow slide while pressing into it), plus wall jump
///   - acceleration / deceleration with separate air control and a turn-around boost
///   - ceiling corner correction (clip a corner by a few pixels and you slide past instead of bonking)
///   - one-way platform drop-through (Down + Jump), moving platform velocity inheritance
///   - frictionless collision so you never stick to walls by accident; movement is 100% velocity-driven
///   - events (Jumped, AirJumped, WallJumped, WallGrabbed, WallReleased, Landed) and a State enum for animation
///
/// Input is sampled every frame in Update() so no press is ever missed; physics runs in FixedUpdate().
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    // ------------------------------------------------------------------ inspector

    [Header("Running")]
    [Min(0), Tooltip("Top horizontal speed, in world units (tiles) per second.")]
    public float maxSpeed = 8f;
    [Min(0), Tooltip("Units/s² while a direction is held on the ground.")]
    public float groundAcceleration = 90f;
    [Min(0), Tooltip("Units/s² while no direction is held on the ground.")]
    public float groundDeceleration = 110f;
    [Min(0), Tooltip("Units/s² while a direction is held in the air.")]
    public float airAcceleration = 60f;
    [Min(0), Tooltip("Units/s² while no direction is held in the air (low = keeps momentum).")]
    public float airDeceleration = 20f;
    [Min(1), Tooltip("Acceleration multiplier while reversing direction, so turning feels instant.")]
    public float turnBoost = 1.6f;

    [Header("Jumping")]
    [Min(0.01f), Tooltip("Peak height of a full jump, in world units (tiles).")]
    public float jumpHeight = 3.6f;
    [Min(0.05f), Tooltip("Seconds from take-off to the peak. Gravity = 2h/t², take-off speed = 2h/t.")]
    public float timeToApex = 0.42f;
    [Range(0f, 1f), Tooltip("Fraction of upward speed kept when the jump button is released early. 0.5 => a tap is about a quarter of the full height.")]
    public float jumpCutMultiplier = 0.5f;
    [Min(1), Tooltip("Gravity multiplier while falling. >1 makes jumps snappy rather than floaty.")]
    public float fallGravityMultiplier = 1.5f;
    [Range(0f, 1f), Tooltip("Gravity multiplier near the peak of a jump (|vertical speed| < Apex Threshold): a little hang time.")]
    public float apexGravityMultiplier = 0.7f;
    [Min(0)] public float apexThreshold = 1.5f;
    [Min(0), Tooltip("Terminal velocity, units/s.")]
    public float maxFallSpeed = 22f;
    [Min(0), Tooltip("Seconds after walking off a ledge during which a normal jump still works.")]
    public float coyoteTime = 0.1f;
    [Min(0), Tooltip("Seconds before landing during which a jump press is remembered and executed on touchdown.")]
    public float jumpBufferTime = 0.12f;
    [Min(0), Tooltip("If the head would clip a ceiling corner by less than this, the body slides sideways past it instead of bonking. 0 disables.")]
    public float cornerCorrectionWidth = 0.25f;

    [Header("Air jumps")]
    [Min(0), Tooltip("Extra jumps allowed while airborne. 1 = double jump, 0 = none. Restored on landing and on grabbing a wall.")]
    public int maxAirJumps = 1;
    [Range(0.1f, 2f), Tooltip("Height of an air jump as a fraction of Jump Height.")]
    public float airJumpHeightMultiplier = 1f;

    public enum WallInteraction
    {
        None,
        [InspectorName("Slide (Celeste)")] Slide,
        [InspectorName("Cling (Blasphemous)")] Cling,
    }

    [Header("Walls")]
    [Tooltip("Cling: press toward a wall while airborne to grab it; you scrape slowly down until you jump off or press Down. Slide: slow fall only while pressing into a wall.")]
    public WallInteraction wallInteraction = WallInteraction.Cling;
    public bool wallJump = true;
    [Min(0), Tooltip("Horizontal speed away from the wall on a wall jump. Vertical speed is the normal take-off speed; the arc is fixed (not cut by releasing).")]
    public float wallJumpHorizontalSpeed = 8f;
    [Min(0), Tooltip("Seconds after a wall jump during which horizontal input is ignored, so the jump actually leaves the wall.")]
    public float wallJumpInputLock = 0.15f;
    [Min(0), Tooltip("Seconds after leaving a wall during which a wall jump still works.")]
    public float wallCoyoteTime = 0.1f;

    [Header("Wall cling")]
    [Min(0), Tooltip("Downward speed while hanging on a wall: the Penitent One scrapes slowly down. 0 = fully stuck.")]
    public float wallClingSlideSpeed = 1.5f;
    [Min(0), Tooltip("How quickly the scrape builds up after grabbing (units/s²): the grab arrests the fall, then the slide starts. 0 = full slide speed at once.")]
    public float wallClingSlideAcceleration = 6f;
    [Tooltip("You must be pressing toward the wall to grab it (letting go afterwards does NOT release you).")]
    public bool wallClingRequiresInput = true;
    [Tooltip("Cannot grab while rising faster than this, so jumping straight up beside a wall does not glue you to it. Use a large value to grab at any speed.")]
    public float wallClingMaxRiseSpeed = 3f;
    [Min(0), Tooltip("Seconds you can hang before dropping off. 0 = forever.")]
    public float wallClingMaxTime = 0f;
    [Min(0), Tooltip("Holding the direction AWAY from the wall for this long lets go. 0 = never (only Jump or Down release you).")]
    public float wallClingReleaseDelay = 0f;
    [Min(0), Tooltip("Seconds after letting go or wall-jumping before a wall can be grabbed again.")]
    public float wallClingCooldown = 0.2f;

    [Header("Wall slide")]
    [Min(0), Tooltip("Fall speed while pressing into a wall in Slide mode.")]
    public float wallSlideSpeed = 3f;

    [Header("Platforms")]
    [Tooltip("Down + Jump drops through one-way platforms (colliders with a one-way PlatformEffector2D).")]
    public bool enableDropThrough = true;
    [Min(0.05f), Tooltip("How long collision with the platform is ignored while dropping through.")]
    public float dropThroughDuration = 0.3f;
    [Tooltip("Move along with kinematic or dynamic platforms you stand on, and keep their momentum when jumping off.")]
    public bool inheritPlatformVelocity = true;

    [Header("Collision")]
    [Tooltip("Layers that count as ground, walls and ceilings. This body's own colliders are always ignored, so 'Everything' is fine.")]
    public LayerMask collisionMask = ~0;
    [Range(0f, 89f), Tooltip("Surfaces steeper than this are walls, not ground.")]
    public float maxGroundAngle = 50f;
    [Min(0.01f), Tooltip("How far below the feet to look for ground.")]
    public float groundCheckDistance = 0.06f;
    [Min(0.01f), Tooltip("How far beside the body to look for walls.")]
    public float wallCheckDistance = 0.08f;
    [Tooltip("Assign a frictionless PhysicsMaterial2D to this body's colliders (recommended: prevents sticking to walls by accident).")]
    public bool useFrictionlessMaterial = true;
    [Tooltip("Mirror the SpriteRenderer (on this object or a child) to face the direction of movement.")]
    public bool flipSpriteToFacing = true;

    // ------------------------------------------------------------------ public API

    public enum State { Idle, Run, Jump, Fall, WallSlide, WallCling }

    /// <summary>What the controller is doing right now. Handy for an Animator: set a parameter from this.</summary>
    public State CurrentState { get; private set; }
    public bool IsGrounded { get; private set; }
    public bool IsOnWall { get; private set; }
    /// <summary>True while hanging on a wall (Cling mode).</summary>
    public bool IsClinging { get; private set; }
    /// <summary>-1 wall on the left, +1 wall on the right, 0 no wall.</summary>
    public int WallDirection { get; private set; }
    public int FacingDirection { get; private set; } = 1;
    public int AirJumpsRemaining { get; private set; }
    /// <summary>The character's own velocity (excluding the platform it is riding).</summary>
    public Vector2 Velocity => velocity;
    public Vector2 GroundVelocity => groundVelocity;
    public Collider2D GroundCollider { get; private set; }
    public Rigidbody2D GroundRigidbody { get; private set; }

    /// <summary>Take-off speed derived from jumpHeight / timeToApex.</summary>
    public float JumpVelocity => 2f * jumpHeight / timeToApex;
    /// <summary>Downward acceleration derived from jumpHeight / timeToApex (the controller applies gravity itself).</summary>
    public float Gravity => 2f * jumpHeight / (timeToApex * timeToApex);

    /// <summary>Set false to freeze input (cutscene, death, level complete). Physics keeps running.</summary>
    public bool InputEnabled { get; set; } = true;

    /// <summary>Plug in your own input (AI, replays, tests). Null = keyboard / gamepad.</summary>
    public IInputSource InputSource { get; set; }

    public struct InputState
    {
        public float Horizontal;   // -1 .. 1
        public bool JumpHeld;      // the controller detects presses itself, so just report "is the button down"
        public bool DownHeld;      // drop through one-way platforms / let go of a wall
    }

    public interface IInputSource { InputState Read(); }

    public event Action Jumped;
    public event Action AirJumped;
    public event Action WallJumped;
    public event Action WallGrabbed;
    public event Action WallReleased;
    /// <summary>Fired on touchdown with the impact speed (units/s) — scale a squash or a dust puff with it.</summary>
    public event Action<float> Landed;

    /// <summary>Launch upward (enemy stomp, spring). Not cut by releasing the jump button.</summary>
    public void Bounce(float upwardSpeed)
    {
        if (IsClinging) ReleaseWall(false);
        velocity.y = upwardSpeed;
        isJumping = false;
        LeaveGround();
        rb.linearVelocity = velocity;
    }

    /// <summary>Move instantly and cancel all motion (respawn, cutscene).</summary>
    public void Teleport(Vector3 position)
    {
        rb.position = position;
        transform.position = position;
        velocity = Vector2.zero;
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        appliedGroundVelocity = groundVelocity = Vector2.zero;
        isJumping = false;
        IsClinging = false;
        AirJumpsRemaining = maxAirJumps;
        LeaveGround();
        RestoreDroppedPlatforms();
    }

    /// <summary>Overwrite the character's own velocity (knockback, dash).</summary>
    public void SetVelocity(Vector2 newVelocity)
    {
        if (IsClinging) ReleaseWall(false);
        velocity = newVelocity;
        rb.linearVelocity = velocity + appliedGroundVelocity;
    }

    // ------------------------------------------------------------------ internals

    Rigidbody2D rb;
    Collider2D[] ownColliders = Array.Empty<Collider2D>();
    SpriteRenderer sprite;
    ContactFilter2D filter;
    readonly RaycastHit2D[] hits = new RaycastHit2D[16];
    readonly List<Collider2D> groundContacts = new List<Collider2D>(4);
    readonly List<Collider2D> droppedPlatforms = new List<Collider2D>(4);
    readonly Dictionary<Collider2D, bool> oneWayCache = new Dictionary<Collider2D, bool>();

    Vector2 velocity;                 // our own velocity, excluding the platform we stand on
    Vector2 groundVelocity;           // velocity of the platform we stand on (zero when static / airborne)
    Vector2 appliedGroundVelocity;    // what we added to the rigidbody last step (subtracted on read-back)
    InputState input;
    bool previousJumpHeld;
    bool isJumping, jumpCutDone, wasGrounded;
    float lastGroundedTime = float.NegativeInfinity;
    float lastJumpPressedTime = float.NegativeInfinity;
    float lastOnWallTime = float.NegativeInfinity;
    float wallJumpLockUntil = float.NegativeInfinity;
    float dropThroughUntil = float.NegativeInfinity;
    float wallClingBlockedUntil = float.NegativeInfinity;
    float clingStartTime, pressingAwaySince = -1f;
    float previousAirborneVy;
    int lastWallDirection, clingDirection;
    Rigidbody2D previousGroundBody;
    Vector2 previousGroundPosition;

    bool JumpBuffered => Time.time - lastJumpPressedTime <= jumpBufferTime;
    bool CoyoteGrounded => Time.time - lastGroundedTime <= coyoteTime;
    bool WallJumpAvailable => wallJump && !IsGrounded && (IsClinging || Time.time - lastOnWallTime <= wallCoyoteTime);

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;                                              // gravity is applied by this script
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;   // never tunnel through thin floors
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;           // smooth rendering between physics steps
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

        ownColliders = new Collider2D[Mathf.Max(1, rb.attachedColliderCount)];
        int count = rb.GetAttachedColliders(ownColliders);
        if (count == 0)
        {
            Debug.LogWarning($"{name}: no Collider2D found, adding a CapsuleCollider2D.", this);
            var capsule = gameObject.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(0.7f, 0.9f);
            capsule.direction = CapsuleDirection2D.Vertical;
            ownColliders = new[] { (Collider2D)capsule };
        }
        else if (count < ownColliders.Length)
        {
            Array.Resize(ref ownColliders, count);
        }

        if (useFrictionlessMaterial)
        {
            var material = new PhysicsMaterial2D("Frictionless (PlayerController)") { friction = 0f, bounciness = 0f };
            foreach (var c in ownColliders) c.sharedMaterial = material;
        }

        sprite = GetComponentInChildren<SpriteRenderer>();
        filter = new ContactFilter2D { useTriggers = false };
        filter.SetLayerMask(collisionMask);
        AirJumpsRemaining = maxAirJumps;
    }

    void OnDisable() => RestoreDroppedPlatforms();

    void Update()
    {
        InputState s = default;
        if (InputEnabled) s = InputSource != null ? InputSource.Read() : ReadDeviceInput();

        if (s.JumpHeld && !previousJumpHeld) lastJumpPressedTime = Time.time;   // press edge, remembered for jumpBufferTime
        previousJumpHeld = s.JumpHeld;
        input = s;

        if (input.Horizontal != 0f && Time.time >= wallJumpLockUntil && !IsClinging)
            SetFacing(input.Horizontal > 0f ? 1 : -1);
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        filter.SetLayerMask(collisionMask);

        // 1. Adopt whatever the physics step did to us (walls, ceilings, landings), relative to the platform we rode.
        velocity = rb.linearVelocity - appliedGroundVelocity;

        if (droppedPlatforms.Count > 0 && Time.time >= dropThroughUntil) RestoreDroppedPlatforms();

        // 2. Sense the world.
        SenseGround();
        SenseWalls();

        // 3. Landing.
        if (IsGrounded && !wasGrounded)
        {
            isJumping = false;
            AirJumpsRemaining = maxAirJumps;
            Landed?.Invoke(Mathf.Abs(previousAirborneVy));
        }
        if (IsGrounded) lastGroundedTime = Time.time;
        if (IsOnWall) { lastOnWallTime = Time.time; lastWallDirection = WallDirection; }
        if (AirJumpsRemaining > maxAirJumps) AirJumpsRemaining = maxAirJumps;   // maxAirJumps may be changed at runtime

        // 4. Wall cling: grab, keep holding, or let go.
        UpdateWallCling();

        // 5. Jump-type actions, in priority order.
        if (enableDropThrough && IsGrounded && input.DownHeld && JumpBuffered && GroundHasOneWayPlatform())
            DropThrough();
        else if (JumpBuffered && CoyoteGrounded)
            Jump();
        else if (JumpBuffered && WallJumpAvailable)
            WallJump();
        else if (JumpBuffered && !IsGrounded && AirJumpsRemaining > 0)
            AirJump();

        // 6. Variable jump height: release early, lose most of the remaining rise. A tap is always a short hop,
        //    even when it was buffered before landing, so the same input always gives the same jump.
        if (isJumping && !jumpCutDone && !input.JumpHeld && velocity.y > 0f)
        {
            velocity.y *= jumpCutMultiplier;
            jumpCutDone = true;
        }

        if (IsClinging)
        {
            // 7a. Hang: no gravity, a tiny push into the wall keeps contact, and a slow scrape downward that
            //     builds up from the grab (the grab itself stops the fall dead).
            float slide = wallClingSlideAcceleration > 0f
                ? Mathf.MoveTowards(velocity.y, -wallClingSlideSpeed, wallClingSlideAcceleration * dt)
                : -wallClingSlideSpeed;
            velocity = new Vector2(clingDirection * 0.5f, Mathf.Min(slide, 0f));
        }
        else
        {
            // 7b. Horizontal.
            Run(dt);

            // 8. Vertical.
            if (IsGrounded)
            {
                if (velocity.y < 0f) velocity.y = Mathf.Max(velocity.y, -1f);   // gentle downward pressure keeps ground contact
            }
            else
            {
                float g = Gravity;
                if (velocity.y < 0f) g *= fallGravityMultiplier;
                else if (isJumping && velocity.y < apexThreshold) g *= apexGravityMultiplier;
                velocity.y -= g * dt;

                if (wallInteraction == WallInteraction.Slide && IsOnWall && input.Horizontal * WallDirection > 0f && velocity.y < -wallSlideSpeed)
                    velocity.y = -wallSlideSpeed;

                if (velocity.y < -maxFallSpeed) velocity.y = -maxFallSpeed;
                previousAirborneVy = velocity.y;
            }

            // 9. Slide past ceiling corners instead of bonking on them.
            if (velocity.y > 0f && cornerCorrectionWidth > 0f) CornerCorrect(dt);
        }

        // 10. Platform velocity, then apply. The platform's speed comes from its position delta between physics
        //     steps, which is reliable for kinematic MovePosition, transform-animated and dynamic bodies alike
        //     (linearVelocity of a kinematic body depends on script execution order within the step).
        groundVelocity = Vector2.zero;
        if (inheritPlatformVelocity && IsGrounded && GroundRigidbody != null && GroundRigidbody.bodyType != RigidbodyType2D.Static)
        {
            groundVelocity = GroundRigidbody == previousGroundBody
                ? (GroundRigidbody.position - previousGroundPosition) / dt
                : GroundRigidbody.linearVelocity;
            previousGroundBody = GroundRigidbody;
            previousGroundPosition = GroundRigidbody.position;
        }
        else
        {
            previousGroundBody = null;
        }
        appliedGroundVelocity = groundVelocity;
        rb.linearVelocity = velocity + groundVelocity;

        wasGrounded = IsGrounded;
        UpdateState();
    }

    // ------------------------------------------------------------------ movement pieces

    void Run(float dt)
    {
        if (Time.time < wallJumpLockUntil) return;            // let the wall jump carry us away first

        float h = input.Horizontal;
        bool hasInput = Mathf.Abs(h) > 0.01f;
        float target = h * maxSpeed;
        float accel = IsGrounded
            ? (hasInput ? groundAcceleration : groundDeceleration)
            : (hasInput ? airAcceleration : airDeceleration);
        if (hasInput && Mathf.Abs(velocity.x) > 0.01f && Mathf.Sign(h) != Mathf.Sign(velocity.x)) accel *= turnBoost;

        velocity.x = Mathf.MoveTowards(velocity.x, target, accel * dt);
    }

    float TakeOffSpeed(float heightMultiplier)
    {
        // + g*dt/2 compensates for the fixed-step integrator so the peak lands exactly on the requested height.
        return JumpVelocity * Mathf.Sqrt(heightMultiplier) + 0.5f * Gravity * Time.fixedDeltaTime;
    }

    void Jump()
    {
        lastJumpPressedTime = float.NegativeInfinity;
        velocity.x += groundVelocity.x;                                       // keep the platform's momentum
        velocity.y = TakeOffSpeed(1f) + Mathf.Max(0f, groundVelocity.y);
        isJumping = true;
        jumpCutDone = false;
        LeaveGround();
        Jumped?.Invoke();
    }

    void AirJump()
    {
        lastJumpPressedTime = float.NegativeInfinity;
        if (IsClinging) ReleaseWall(true);
        AirJumpsRemaining--;
        velocity.y = TakeOffSpeed(airJumpHeightMultiplier);
        isJumping = true;
        jumpCutDone = false;
        AirJumped?.Invoke();
    }

    void WallJump()
    {
        lastJumpPressedTime = float.NegativeInfinity;
        lastOnWallTime = float.NegativeInfinity;
        int away = -lastWallDirection;
        if (IsClinging) ReleaseWall(true);
        else wallClingBlockedUntil = Time.time + wallClingCooldown;
        velocity = new Vector2(away * wallJumpHorizontalSpeed, TakeOffSpeed(1f));
        wallJumpLockUntil = Time.time + wallJumpInputLock;
        isJumping = true;
        jumpCutDone = true;                                                   // fixed arc: a tap climbs as far as a hold
        SetFacing(away);
        WallJumped?.Invoke();
    }

    void DropThrough()
    {
        lastJumpPressedTime = float.NegativeInfinity;
        foreach (var platform in groundContacts)
        {
            if (!IsOneWay(platform)) continue;
            foreach (var own in ownColliders) Physics2D.IgnoreCollision(own, platform, true);
            droppedPlatforms.Add(platform);
        }
        dropThroughUntil = Time.time + dropThroughDuration;
        velocity.y = Mathf.Min(velocity.y, -1f);
        LeaveGround();
    }

    void UpdateWallCling()
    {
        if (IsClinging)
        {
            bool lost = IsGrounded || !IsOnWall || WallDirection != clingDirection;
            bool timedOut = wallClingMaxTime > 0f && Time.time - clingStartTime >= wallClingMaxTime;

            bool pressingAway = input.Horizontal * clingDirection < 0f;
            if (pressingAway && pressingAwaySince < 0f) pressingAwaySince = Time.time;
            if (!pressingAway) pressingAwaySince = -1f;
            bool letGo = wallClingReleaseDelay > 0f && pressingAwaySince >= 0f && Time.time - pressingAwaySince >= wallClingReleaseDelay;

            if (lost || timedOut || letGo || input.DownHeld) ReleaseWall(!lost);
            return;
        }

        if (wallInteraction != WallInteraction.Cling || IsGrounded || !IsOnWall) return;
        if (Time.time < wallClingBlockedUntil) return;
        if (velocity.y > wallClingMaxRiseSpeed) return;
        if (wallClingRequiresInput && input.Horizontal * WallDirection <= 0f) return;
        GrabWall();
    }

    void GrabWall()
    {
        IsClinging = true;
        clingDirection = WallDirection;
        clingStartTime = Time.time;
        pressingAwaySince = -1f;
        isJumping = false;
        jumpCutDone = true;
        AirJumpsRemaining = maxAirJumps;                                      // a wall is as good as the ground
        velocity = Vector2.zero;
        SetFacing(clingDirection);
        WallGrabbed?.Invoke();
    }

    void ReleaseWall(bool applyCooldown)
    {
        IsClinging = false;
        if (applyCooldown) wallClingBlockedUntil = Time.time + wallClingCooldown;
        WallReleased?.Invoke();
    }

    void LeaveGround()
    {
        IsGrounded = false;
        GroundCollider = null;
        GroundRigidbody = null;
        groundContacts.Clear();
        lastGroundedTime = float.NegativeInfinity;                            // no second coyote jump
    }

    void RestoreDroppedPlatforms()
    {
        foreach (var platform in droppedPlatforms)
        {
            if (platform == null) continue;
            foreach (var own in ownColliders) if (own != null) Physics2D.IgnoreCollision(own, platform, false);
        }
        droppedPlatforms.Clear();
    }

    void SetFacing(int direction)
    {
        FacingDirection = direction;
        if (flipSpriteToFacing && sprite != null) sprite.flipX = direction < 0;
    }

    void UpdateState()
    {
        if (IsClinging) CurrentState = State.WallCling;
        else if (IsGrounded) CurrentState = Mathf.Abs(velocity.x) > 0.1f ? State.Run : State.Idle;
        else if (wallInteraction == WallInteraction.Slide && IsOnWall && velocity.y < 0f && input.Horizontal * WallDirection > 0f) CurrentState = State.WallSlide;
        else CurrentState = velocity.y > 0f ? State.Jump : State.Fall;
    }

    // ------------------------------------------------------------------ sensing

    void SenseGround()
    {
        IsGrounded = false;
        GroundCollider = null;
        GroundRigidbody = null;
        groundContacts.Clear();
        if (velocity.y > 0.05f) return;                                       // rising: we just jumped, ignore the floor we left

        float minNormalY = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
        float feetY = GetBounds().min.y;
        int n = rb.Cast(Vector2.down, filter, hits, groundCheckDistance);     // casts our own shapes; never reports ourselves
        float best = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            var hit = hits[i];
            if (hit.normal.y < minNormalY) continue;                          // a wall, not a floor
            if (droppedPlatforms.Contains(hit.collider)) continue;
            if (IsOneWay(hit.collider) && feetY < hit.collider.bounds.max.y - 0.05f) continue;   // still passing up through it
            groundContacts.Add(hit.collider);
            if (hit.distance < best)
            {
                best = hit.distance;
                GroundCollider = hit.collider;
                GroundRigidbody = hit.rigidbody;
            }
        }
        IsGrounded = GroundCollider != null;
    }

    void SenseWalls()
    {
        IsOnWall = false;
        WallDirection = 0;
        if (wallInteraction == WallInteraction.None && !wallJump) return;

        int first = IsClinging ? clingDirection
                  : input.Horizontal != 0f ? (input.Horizontal > 0f ? 1 : -1)
                  : FacingDirection;
        if (ProbeWall(first) || ProbeWall(-first)) IsOnWall = true;
    }

    bool ProbeWall(int direction)
    {
        int n = rb.Cast(Vector2.right * direction, filter, hits, wallCheckDistance);
        for (int i = 0; i < n; i++)
        {
            var hit = hits[i];
            if (Mathf.Abs(hit.normal.x) < 0.7f || Mathf.Sign(hit.normal.x) == direction) continue;
            if (IsOneWay(hit.collider) || droppedPlatforms.Contains(hit.collider)) continue;
            WallDirection = direction;
            return true;
        }
        return false;
    }

    /// <summary>
    /// If the head is about to hit a ceiling but only one top corner is blocked and the blocking edge is within
    /// cornerCorrectionWidth, shift sideways so the jump continues. Classic Celeste / Mario trick.
    /// </summary>
    void CornerCorrect(float dt)
    {
        Bounds b = GetBounds();
        const float skin = 0.02f;
        float top = b.max.y + 0.01f;
        float reach = velocity.y * dt + 0.05f;

        bool leftBlocked = RayUp(b.min.x + skin, top, reach);
        bool rightBlocked = RayUp(b.max.x - skin, top, reach);
        if (leftBlocked == rightBlocked) return;                              // clear, or a real bonk

        int dir = leftBlocked ? 1 : -1;                                       // move away from the blocked corner
        float edgeX = leftBlocked ? b.min.x + skin : b.max.x - skin;
        for (float d = 0.02f; d <= cornerCorrectionWidth; d += 0.02f)
        {
            if (RayUp(edgeX + dir * d, top, reach)) continue;                 // still under the ceiling
            float shift = d + 2f * skin;
            if (rb.Cast(Vector2.right * dir, filter, hits, shift) > 0) return; // no room to slide
            rb.position += Vector2.right * (dir * shift);
            return;
        }
    }

    bool RayUp(float x, float y, float distance)
    {
        int n = Physics2D.Raycast(new Vector2(x, y), Vector2.up, filter, hits, distance);
        for (int i = 0; i < n; i++)
        {
            var c = hits[i].collider;
            if (c.attachedRigidbody == rb || IsOneWay(c) || droppedPlatforms.Contains(c)) continue;
            return true;
        }
        return false;
    }

    bool GroundHasOneWayPlatform()
    {
        foreach (var c in groundContacts) if (IsOneWay(c)) return true;
        return false;
    }

    bool IsOneWay(Collider2D c)
    {
        if (c == null) return false;
        if (!oneWayCache.TryGetValue(c, out bool oneWay))
        {
            oneWay = c.usedByEffector && c.TryGetComponent<PlatformEffector2D>(out var effector) && effector.useOneWay;
            oneWayCache[c] = oneWay;
        }
        return oneWay;
    }

    Bounds GetBounds()
    {
        var b = ownColliders[0].bounds;
        for (int i = 1; i < ownColliders.Length; i++) b.Encapsulate(ownColliders[i].bounds);
        return b;
    }

    // ------------------------------------------------------------------ input

    static InputState ReadDeviceInput()
    {
        var s = new InputState();
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        // Input System package (Edit > Project Settings > Player > Active Input Handling = "Input System Package").
        var kb = UnityEngine.InputSystem.Keyboard.current;
        var pad = UnityEngine.InputSystem.Gamepad.current;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) s.Horizontal -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) s.Horizontal += 1f;
            s.JumpHeld = kb.spaceKey.isPressed || kb.wKey.isPressed || kb.upArrowKey.isPressed;
            s.DownHeld = kb.sKey.isPressed || kb.downArrowKey.isPressed;
        }
        if (pad != null)
        {
            float stick = pad.leftStick.x.ReadValue();
            if (Mathf.Abs(stick) > 0.3f) s.Horizontal = Mathf.Sign(stick);
            if (pad.dpad.left.isPressed) s.Horizontal = -1f;
            if (pad.dpad.right.isPressed) s.Horizontal = 1f;
            s.JumpHeld |= pad.buttonSouth.isPressed;
            s.DownHeld |= pad.dpad.down.isPressed || pad.leftStick.y.ReadValue() < -0.5f;
        }
#else
        // Legacy Input Manager (Edit > Project Settings > Input Manager): "Horizontal", "Vertical", "Jump" axes.
        s.Horizontal = Input.GetAxisRaw("Horizontal");
        s.JumpHeld = Input.GetButton("Jump") || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
        s.DownHeld = Input.GetAxisRaw("Vertical") < -0.5f;
#endif
        return s;
    }

    // ------------------------------------------------------------------ editor helpers

    void OnValidate()
    {
        if (Application.isPlaying && rb != null) filter.SetLayerMask(collisionMask);
    }

    void OnDrawGizmosSelected()
    {
        var colliders = GetComponents<Collider2D>();
        if (colliders.Length == 0) return;
        var b = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++) b.Encapsulate(colliders[i].bounds);

        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireCube(new Vector3(b.center.x, b.min.y - groundCheckDistance * 0.5f, 0f), new Vector3(b.size.x, groundCheckDistance, 0f));
        if (wallInteraction != WallInteraction.None || wallJump)
        {
            Gizmos.color = IsClinging ? Color.magenta : IsOnWall ? Color.cyan : Color.gray;
            Gizmos.DrawWireCube(new Vector3(b.max.x + wallCheckDistance * 0.5f, b.center.y, 0f), new Vector3(wallCheckDistance, b.size.y, 0f));
            Gizmos.DrawWireCube(new Vector3(b.min.x - wallCheckDistance * 0.5f, b.center.y, 0f), new Vector3(wallCheckDistance, b.size.y, 0f));
        }
        if (cornerCorrectionWidth > 0f)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(new Vector3(b.min.x, b.max.y, 0f), new Vector3(b.min.x + cornerCorrectionWidth, b.max.y, 0f));
            Gizmos.DrawLine(new Vector3(b.max.x, b.max.y, 0f), new Vector3(b.max.x - cornerCorrectionWidth, b.max.y, 0f));
        }
    }
}
