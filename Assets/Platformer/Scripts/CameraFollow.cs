using UnityEngine;

/// <summary>Smoothly follows a target and never shows anything outside the level bounds.</summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    public Transform target;
    [Tooltip("Where the target sits relative to the screen centre (positive y = look up a bit).")]
    public Vector2 offset = new Vector2(0f, 1.5f);
    [Tooltip("Seconds to catch up with the target. Smaller = tighter.")]
    public float smoothTime = 0.12f;
    public bool useBounds;
    public Rect bounds;

    Camera cam;
    Vector3 velocity;

    Camera Cam => cam != null ? cam : (cam = GetComponent<Camera>());

    void LateUpdate()                                     // after the player has moved this frame
    {
        if (target == null) return;
        transform.position = Vector3.SmoothDamp(transform.position, Desired(), ref velocity, smoothTime);
    }

    /// <summary>Jump straight to the target (level start, respawn).</summary>
    public void Snap()
    {
        if (target == null) return;
        transform.position = Desired();
        velocity = Vector3.zero;
    }

    Vector3 Desired()
    {
        Vector3 p = target.position + (Vector3)offset;
        p.z = transform.position.z;                       // keep the camera on its own z plane
        if (!useBounds) return p;

        float halfH = Cam.orthographicSize;
        float halfW = halfH * Cam.aspect;
        p.x = ClampCentred(p.x, bounds.xMin + halfW, bounds.xMax - halfW);
        p.y = ClampCentred(p.y, bounds.yMin + halfH, bounds.yMax - halfH);
        return p;
    }

    // If the level is smaller than the view, centre it instead of clamping to an inverted range.
    static float ClampCentred(float v, float min, float max) => min > max ? (min + max) * 0.5f : Mathf.Clamp(v, min, max);
}
