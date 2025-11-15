using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Collections;
using TMPro; // Adicionado se o ProgressBar usar TextMeshPro

public class ButtonSpawner : NetworkBehaviour
{
    [Header("Coin Spawner Button Settings")]
    public float timeToStayInZone = 5f;
    public int coinsToSpawn = 5;
    public float coinSpawnRadius = 3f;

    [Header("UI Settings")]
    public GameObject progressBarUIPrefab;
    public Vector3 progressBarOffset = new Vector3(0, 2f, 0);

    private float _currentStayTime = 0f;
    private GameObject _playerGameObject;
    private Slider _currentProgressBarInstance;
    private GameManager _gameManager;
    private bool _isPlayerInZone = false; // Novo estado para controlar a entrada/saída

    void Start()
    {
        _gameManager = FindFirstObjectByType<GameManager>();
        gameObject.SetActive(false); // Inicia desativado (será ativado pelo GameManager)
    }

    void Update()
    {
        if (!IsOwner || !_isPlayerInZone) return;

        // Apenas o proprietário (o cliente local) deve processar a lógica do timer
        _currentStayTime += Time.deltaTime;

        UpdateProgressBar(_currentStayTime / timeToStayInZone);

        if (_currentStayTime >= timeToStayInZone)
        {
            // Timer completado
            _currentStayTime = 0f;
            HideProgressBar();
            _isPlayerInZone = false;

            // Chama o RPC no servidor
            if (_gameManager != null)
            {
                // CORREÇÃO CS1061: A assinatura do RPC está correta para a implementação em GameManager
                _gameManager.SpawnCoinsFromButtonServerRpc(
                    new NetworkObjectReference(this.NetworkObject), // Referência do próprio objeto
                    transform.position,
                    coinsToSpawn,
                    coinSpawnRadius);
            }

            // Desativa o botão para que não seja usado novamente imediatamente
            // O GameManager ou o próprio ButtonSpawner pode reativá-lo após um cooldown
            SetButtonActiveClientRpc(false);
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // Verifica se o objeto que entrou é um jogador e se ele é o jogador local deste cliente
        if (other.CompareTag("Player") && other.GetComponent<NetworkObject>() != null && other.GetComponent<NetworkObject>().IsOwner)
        {
            _playerGameObject = other.gameObject;
            _isPlayerInZone = true;
            ShowProgressBar();
        }
    }

    void OnTriggerExit(Collider other)
    {
        // Verifica se o objeto que saiu é um jogador e se ele é o jogador local deste cliente
        if (other.CompareTag("Player") && other.GetComponent<NetworkObject>() != null && other.GetComponent<NetworkObject>().IsOwner)
        {
            _currentStayTime = 0f;
            _isPlayerInZone = false;
            HideProgressBar();
        }
    }

    [ClientRpc]
    public void SetButtonActiveClientRpc(bool isActive)
    {
        gameObject.SetActive(isActive);
    }

    private void ShowProgressBar()
    {
        if (_playerGameObject == null || progressBarUIPrefab == null) return;

        // Tenta encontrar a barra de progresso já existente para não instanciar múltiplas
        if (_currentProgressBarInstance != null)
        {
            _currentProgressBarInstance.gameObject.SetActive(true);
            return;
        }

        GameObject progressBarGO = Instantiate(progressBarUIPrefab, _playerGameObject.transform.position + progressBarOffset, Quaternion.identity);
        // O progressBarGO é o Canvas/Root. O Slider está dentro.
        _currentProgressBarInstance = progressBarGO.GetComponentInChildren<Slider>();

        if (_currentProgressBarInstance != null)
        {
            _currentProgressBarInstance.gameObject.SetActive(true);
            _currentProgressBarInstance.minValue = 0;
            _currentProgressBarInstance.maxValue = timeToStayInZone; // O máximo deve ser o tempo total
            _currentProgressBarInstance.value = _currentStayTime;
        }
    }

    private void UpdateProgressBar(float progress)
    {
        if (_currentProgressBarInstance != null)
        {
            // O valor aqui já é o tempo atual (_currentStayTime) e o maxValue é o timeToStayInZone.
            _currentProgressBarInstance.value = _currentStayTime;
        }
    }

    private void HideProgressBar()
    {
        // Destrói o objeto pai, que contém o Canvas e o Slider
        if (_currentProgressBarInstance != null)
        {
            Destroy(_currentProgressBarInstance.transform.root.gameObject);
            _currentProgressBarInstance = null;
        }
    }
}