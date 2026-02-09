using UnityEngine;
using Unity.Netcode;

public class CameraFollow : MonoBehaviour
{
    private Transform _target;
    public Transform Target
    {
        get { return _target; }
        set { _target = value; }
    }

    public Vector3 offset = new Vector3(0f, 5f, -8f);
    public Vector3 cameraRotation = new Vector3(30f, 0f, 0f);
    public float smoothSpeed = 0.125f;

    void Update()
    {
        // Se não tem target, tenta encontrar automaticamente
        if (_target == null)
        {
            FindAndAssignTarget();
        }
        // Se tem target mas não é mais válido (player foi resetado)
        else if (_target.GetComponent<NetworkObject>() != null &&
                 !_target.GetComponent<NetworkObject>().IsSpawned)
        {
            FindAndAssignTarget();
        }
    }
    void LateUpdate()
    {
        // Se o _target não foi definido, tentamos encontrá-lo
        if (_target == null)
        {
            return;
        }

        Vector3 desiredPosition = _target.position + offset;
        Vector3 smoothedPosition = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed);
        transform.position = smoothedPosition;
        transform.rotation = Quaternion.Euler(cameraRotation);

        // Mantém a câmera olhando para baixo (top-down)
        //transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private void FindAndAssignTarget()
    {
        // Procura por todos os NetworkObjects que pertencem ao cliente local
        NetworkObject[] allNetworkObjects = FindObjectsByType<NetworkObject>(FindObjectsSortMode.None);

        foreach (NetworkObject netObj in allNetworkObjects)
        {
            if (netObj.IsOwner && netObj.IsSpawned)
            {
                // Verifica se é um personagem jogável
                if (netObj.GetComponent<PlayerMovement>() != null ||
                    netObj.GetComponent<Guard>() != null)
                {
                    _target = netObj.transform;
                    ForcePosition();
                    Debug.Log($"CameraFollow: Target atribuído a {netObj.name}");
                    return;
                }
            }
        }
    }

    public void ForcePosition()
    {
        if (_target == null)
        {
            // Tenta encontrar o target se for null
            var localPlayer = FindFirstObjectByType<PlayerMovement>();
            if (localPlayer != null && localPlayer.IsOwner)
            {
                _target = localPlayer.transform;
            }
            else
            {
                var localGuard = FindFirstObjectByType<Guard>();
                if (localGuard != null && localGuard.IsOwner)
                {
                    _target = localGuard.transform;
                }
                return;
            }
        }

        // Teleporta imediatamente para a posição do target
        transform.position = _target.position + offset;

        Debug.Log($"CameraForcePosition: {transform.position}, Target: {_target.position}");
    }
}