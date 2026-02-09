using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class ButtonSpawner : NetworkBehaviour
{
    private ButtonManager _buttonManager;

    [Header("Settings")]
    public float timeToStayInZone = 5f;
    public int coinsToSpawn = 5;
    public float coinSpawnRadius = 3f;

    [Header("UI Settings")]
    public GameObject progressBarUIPrefab;
    public Vector3 progressBarOffset = new Vector3(0, 2f, 0);

    // Variável de rede para que todos vejam o progresso da barra
    private NetworkVariable<float> _currentStayTimeNet = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private GameObject _playerGameObject;
    private Slider _currentProgressBarInstance;
    private GameManager _gameManager;
    private bool _isPlayerInZone = false;
    private bool _isInitialized = false;

    void OnEnable()
    {
        // Garante que o botão sempre começa "limpo" quando é ativado
        // Mas só mexe em network variables se já estiver spawned
        if (IsSpawned && IsServer)
        {
            _isPlayerInZone = false;
            _currentStayTimeNet.Value = 0f;
        }
        _playerGameObject = null;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Garante inicialização em builds
        InitializeReferences();

        // Registra callback APÓS garantir que está inicializado
        _currentStayTimeNet.OnValueChanged += OnProgressChanged;

        // Se já tinha progresso ao spawnar, atualiza
        if (_currentStayTimeNet.Value > 0)
        {
            OnProgressChanged(0, _currentStayTimeNet.Value);
        }

        _isInitialized = true;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _currentStayTimeNet.OnValueChanged -= OnProgressChanged;
        HideProgressBar();
    }

    private void InitializeReferences()
    {
        if (_gameManager == null)
        {
            _gameManager = FindFirstObjectByType<GameManager>();
        }

        if (_buttonManager == null)
        {
            _buttonManager = FindFirstObjectByType<ButtonManager>();
        }
    }

    private void OnProgressChanged(float previous, float current)
    {
        if (!_isInitialized) return;

        if (current > 0)
        {
            if (_currentProgressBarInstance == null)
            {
                ShowProgressBar();
            }
            UpdateProgressBar(current / timeToStayInZone);
        }
        else
        {
            HideProgressBar();
        }
    }

    void Update()
    {
        if (!_isInitialized) return;

        // Apenas o SERVIDOR processa o incremento do tempo
        if (IsServer && _isPlayerInZone)
        {
            _currentStayTimeNet.Value += Time.deltaTime;

            if (_currentStayTimeNet.Value >= timeToStayInZone)
            {
                FinishButtonServer();
            }
        }
    }

    private void FinishButtonServer()
    {
        if (!IsServer) return;

        // Garante que as referências existem
        InitializeReferences();

        // 1. Spawna as moedas para todos (via GameManager)
        if (_gameManager != null)
        {
            _gameManager.SpawnCoinsFromButtonServerRpc(
                new NetworkObjectReference(this.NetworkObject),
                transform.position,
                coinsToSpawn,
                coinSpawnRadius
            );
        }
        else
        {
            Debug.LogError($"[ButtonSpawner] GameManager is null when trying to spawn coins!");
        }

        // 2. Avisa o Manager para desativar este botão e marcar o respawn
        if (_buttonManager != null)
        {
            _buttonManager.OnButtonCompleted(this);
        }
        else
        {
            Debug.LogError($"[ButtonSpawner] ButtonManager is null when trying to complete button!");
        }

        // 3. Reseta o tempo para o próximo uso
        _currentStayTimeNet.Value = 0f;
    }

    // Chamado pelo ButtonManager para limpar o estado visual
    public void ResetButton()
    {
        // Reseta TODOS os estados
        if (IsServer)
        {
            _currentStayTimeNet.Value = 0f;
            _isPlayerInZone = false; 
        }

        _playerGameObject = null; // Limpa referência do jogador

        // Limpa a UI local também
        HideProgressBar();
    }

    void OnTriggerEnter(Collider other)
    {
        if (!_isInitialized) return;

        // Detecta se QUALQUER jogador entrou (seja host ou client)
        if (other.CompareTag("Player"))
        {
            _playerGameObject = other.gameObject;

            if (IsServer)
            {
                _isPlayerInZone = true;
            }
            else
            {
                NotifyServerPlayerEnteredServerRpc(true);
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        if (!_isInitialized) return;

        if (other.CompareTag("Player") && other.gameObject == _playerGameObject)
        {
            _playerGameObject = null;

            if (IsServer)
            {
                _isPlayerInZone = false;
                _currentStayTimeNet.Value = 0f; // Reseta se sair
            }
            else
            {
                NotifyServerPlayerEnteredServerRpc(false);
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void NotifyServerPlayerEnteredServerRpc(bool inside)
    {
        _isPlayerInZone = inside;
        if (!inside)
        {
            _currentStayTimeNet.Value = 0f;
        }
    }

    // --- Lógica de UI (Barra de Carregamento) ---

    private void ShowProgressBar()
    {
        if (_currentProgressBarInstance != null) return;
        if (progressBarUIPrefab == null)
        {
            Debug.LogError($"[ButtonSpawner] progressBarUIPrefab is null!");
            return;
        }

        GameObject progressBarGO = Instantiate(
            progressBarUIPrefab,
            transform.position + progressBarOffset,
            Quaternion.identity
        );

        progressBarGO.transform.SetParent(transform); // Fixa a barra no botão
        _currentProgressBarInstance = progressBarGO.GetComponentInChildren<Slider>();

        if (_currentProgressBarInstance != null)
        {
            _currentProgressBarInstance.maxValue = 1f; // Usaremos 0 a 1
            _currentProgressBarInstance.value = 0f;
        }
        else
        {
            Debug.LogError($"[ButtonSpawner] Could not find Slider in progressBarUIPrefab!");
        }
    }

    private void UpdateProgressBar(float progressNormalized)
    {
        if (_currentProgressBarInstance != null)
        {
            _currentProgressBarInstance.value = Mathf.Clamp01(progressNormalized);
        }
    }

    private void HideProgressBar()
    {
        if (_currentProgressBarInstance != null)
        {
            // Destrói apenas o GameObject da barra (progressBarGO)
            if (_currentProgressBarInstance.transform.parent != null)
            {
                Destroy(_currentProgressBarInstance.transform.parent.gameObject);
            }
            else
            {
                Destroy(_currentProgressBarInstance.gameObject);
            }
            _currentProgressBarInstance = null;
        }
    }

    // Debug helper
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, coinSpawnRadius);
    }
}