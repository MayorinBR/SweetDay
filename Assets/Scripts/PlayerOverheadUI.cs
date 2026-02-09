using UnityEngine;
using TMPro;
using Unity.Netcode;

public class PlayerOverheadUI : NetworkBehaviour
{
    [SerializeField] private TextMeshProUGUI idText;
    
    // Referências aos scripts que guardam o playerNumber
    private PlayerMovement _playerMovement;
    private Guard _guard;

    public override void OnNetworkSpawn()
    {
        if (idText == null)
            idText = GetComponentInChildren<TextMeshProUGUI>();

        // Tenta pegar um dos dois componentes
        _playerMovement = GetComponentInParent<PlayerMovement>();
        _guard = GetComponentInParent<Guard>();

        // Se o valor já estiver definido, atualiza agora
        UpdateDisplay();

        // Inscreve-se para mudanças futuras no valor (importante para rede)
        if (_playerMovement != null) 
            _playerMovement.playerNumber.OnValueChanged += OnIdChanged;
        else if (_guard != null) 
            _guard.playerNumber.OnValueChanged += OnIdChanged;
    }

    private void OnIdChanged(int previousValue, int newValue)
    {
        UpdateDisplay();
    }

    private void UpdateDisplay()
    {
        if (idText == null) return;

        int finalId = 0;

        // Puxa o valor de onde estiver disponível
        if (_playerMovement != null) finalId = _playerMovement.playerNumber.Value;
        else if (_guard != null) finalId = _guard.playerNumber.Value;

        // Se o ID ainda for 0 (não inicializado), usa o OwnerClientId como fallback
        if (finalId == 0) finalId = (int)OwnerClientId + 1;

        idText.text = $"P{finalId}";

        // Cores baseadas no ID final para diferenciar P1, P2, P3...
        if (_playerMovement != null)
        {
            idText.color = Color.yellow;
        }
        else
        {
            idText.color = Color.red;
        }
    }

    void LateUpdate()
    {
        // Faz o texto olhar sempre para a câmera atual
        if (Camera.main != null)
        {
            transform.LookAt(transform.position + Camera.main.transform.rotation * Vector3.forward,
                             Camera.main.transform.rotation * Vector3.up);
        }
    }

    public override void OnNetworkDespawn()
    {
        // Limpeza de eventos
        if (_playerMovement != null) _playerMovement.playerNumber.OnValueChanged -= OnIdChanged;
        if (_guard != null) _guard.playerNumber.OnValueChanged -= OnIdChanged;
    }
}