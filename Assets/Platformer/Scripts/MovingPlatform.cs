using UnityEngine;

/// <summary>
/// Kinematic platform that glides back and forth between its start position and start + offset,
/// pausing briefly at each end. PlayerController rides it and keeps its momentum when jumping off.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class MovingPlatform : MonoBehaviour
{
    [Tooltip("Where the platform travels to, relative to where it starts.")]
    public Vector2 offset = new Vector2(6f, 3f);
    [Min(0)] public float speed = 2f;
    [Min(0), Tooltip("Seconds to wait at each end.")]
    public float pause = 0.5f;

    Rigidbody2D body;
    Vector2 start;
    float progress;          // 0 = start, 1 = start + offset
    int direction = 1;
    float waitUntil;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        start = body.position;
    }

    void FixedUpdate()
    {
        if (Time.time < waitUntil)
        {
            body.MovePosition(body.position);                 // hold still (and report zero velocity)
            return;
        }

        float length = offset.magnitude;
        if (length < 0.001f) return;
        progress += direction * speed * Time.fixedDeltaTime / length;
        if (progress >= 1f) { progress = 1f; direction = -1; waitUntil = Time.time + pause; }
        else if (progress <= 0f) { progress = 0f; direction = 1; waitUntil = Time.time + pause; }
        body.MovePosition(start + offset * progress);
    }

    void OnDrawGizmosSelected()
    {
        Vector3 a = Application.isPlaying ? (Vector3)start : transform.position;
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(a, a + (Vector3)offset);
        Gizmos.DrawWireCube(a + (Vector3)offset, transform.localScale);
    }
}
