using UnityEngine;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private PlayerInputProvider inputProvider;
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField] private float minX = -7f;
    [SerializeField] private float maxX = 7f;
    private GameSession session;
    public PlayerInputProvider InputProvider => inputProvider;

    public void SetSession(GameSession value)
    {
        session = value;
        if (inputProvider != null) inputProvider.SetSession(value);
    }

    private void Awake()
    {
        if (inputProvider == null || minX > maxX)
        {
            Debug.LogError("PlayerController needs an Input Provider and Min X <= Max X.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (session != null && !session.IsPlaying) return;
        Vector3 position = transform.position;
        position.x += inputProvider.HorizontalInput * moveSpeed * Time.deltaTime;
        position.x = Mathf.Clamp(position.x, minX, maxX);

        // Preserve the existing Y and Z positions.
        transform.position = position;
    }
}
