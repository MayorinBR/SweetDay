using UnityEngine;
using Unity.Netcode;

public class CameraFollow : MonoBehaviour
{
    public static CameraFollow Instance { get; private set; }

    private Transform _target;
    public Transform Target
    {
        get { return _target; }
        set { _target = value; }
    }

    public Vector3 offset = new Vector3(0f, 15f, 0f);
    public float smoothSpeed = 0.125f;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    void LateUpdate()
    {
        // Se o _target não foi definido, tentamos encontrá-lo
        if (_target == null)
        {
            // O `NetworkManager` só existe depois que a conexão é iniciada.
            // Checamos se ele não é nulo antes de tentar pegar o objeto do jogador local.
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.SpawnManager != null)
            {
                NetworkObject localPlayerObject = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
                if (localPlayerObject != null)
                {
                    Target = localPlayerObject.transform;
                }
            }
            // Se o _target ainda for nulo, saímos para evitar o erro.
            if (_target == null)
            {
                return;
            }
        }

        Vector3 desiredPosition = _target.position + offset;
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;
    }
}