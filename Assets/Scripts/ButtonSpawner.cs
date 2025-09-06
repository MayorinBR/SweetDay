using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;
using System.Collections;

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

    void Start()
    {
        _gameManager = FindFirstObjectByType<GameManager>();
    }

    void OnTriggerEnter(Collider other)
    {
        // Verifica se o objeto que entrou é um jogador e se ele é o jogador local deste cliente
        if (other.CompareTag("Player") && other.GetComponent<NetworkObject>().IsOwner)
        {
            _playerGameObject = other.gameObject;
            ShowProgressBar();
        }
    }

    void OnTriggerExit(Collider other)
    {
        // Verifica se o objeto que saiu é um jogador e se ele é o jogador local deste cliente
        if (other.CompareTag("Player") && other.GetComponent<NetworkObject>().IsOwner)
        {
            _currentStayTime = 0f;
            HideProgressBar();
        }
    }

    void OnTriggerStay(Collider other)
    {
        // CORRIGIDO: Agora verificamos se o jogador (other) é o dono deste cliente.
        // O `ButtonSpawner` em si não é de propriedade do cliente, então IsOwner será falso para ele.
        if (other.CompareTag("Player") && other.GetComponent<NetworkObject>().IsOwner)
        {
            _currentStayTime += Time.deltaTime;
            UpdateProgressBar(_currentStayTime / timeToStayInZone);

            if (_currentStayTime >= timeToStayInZone)
            {
                HideProgressBar();
                _currentStayTime = 0f;
                // O cliente solicita ao servidor para spawnar as moedas via RPC.
                if (_gameManager != null)
                {
                    _gameManager.SpawnCoinsFromButtonServerRpc(
                        new NetworkObjectReference(this.NetworkObject),
                        transform.position,
                        coinsToSpawn,
                        coinSpawnRadius);
                }
            }
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
        GameObject progressBarGO = Instantiate(progressBarUIPrefab, _playerGameObject.transform.position + progressBarOffset, Quaternion.identity);
        _currentProgressBarInstance = progressBarGO.GetComponentInChildren<Slider>();
        if (_currentProgressBarInstance != null)
        {
            _currentProgressBarInstance.gameObject.SetActive(true);
            _currentProgressBarInstance.minValue = 0;
            _currentProgressBarInstance.maxValue = 1;
            _currentProgressBarInstance.value = 0;
        }
    }

    private void UpdateProgressBar(float progress)
    {
        if (_currentProgressBarInstance != null)
        {
            _currentProgressBarInstance.value = progress;
        }
    }

    private void HideProgressBar()
    {
        if (_currentProgressBarInstance != null)
        {
            Destroy(_currentProgressBarInstance.gameObject);
            _currentProgressBarInstance = null;
        }
    }
}