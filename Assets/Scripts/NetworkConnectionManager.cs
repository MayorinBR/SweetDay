using UnityEngine;
using Unity.Netcode;
using TMPro;
using Unity.Netcode.Transports.UTP;

public class NetworkConnectionManager : MonoBehaviour
{
    public TMP_InputField ipInputField;
    public GameObject connectionPanel;
    public GameObject loadingPanel;
    public GameObject inGameUI;

    void Start()
    {
        // Registra callbacks de eventos de rede
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
        NetworkManager.Singleton.OnServerStarted += OnServerStarted;
    }

    public void StartHost()
    {
        connectionPanel.SetActive(false);
        loadingPanel.SetActive(true);
        NetworkManager.Singleton.StartHost();
    }

    public void StartClient()
    {
        if (!string.IsNullOrEmpty(ipInputField.text))
        {
            // Configura o transporte com o IP inserido
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetConnectionData(ipInputField.text, 7777);

            connectionPanel.SetActive(false);
            loadingPanel.SetActive(true);
            NetworkManager.Singleton.StartClient();
        }
        else
        {
            Debug.LogError("Por favor, insira um endereço IP válido");
        }
    }

    public void StartServer()
    {
        connectionPanel.SetActive(false);
        loadingPanel.SetActive(true);
        NetworkManager.Singleton.StartServer();
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Cliente conectado: {clientId}");

        if (NetworkManager.Singleton.IsHost || NetworkManager.Singleton.IsServer)
        {
            // Host/Server já tem a UI de jogo ativa
        }
        else
        {
            // Cliente mostra UI de jogo quando conecta
            loadingPanel.SetActive(false);
            inGameUI.SetActive(true);
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Cliente desconectado: {clientId}");

        if (!NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost)
        {
            // Volta para o menu se for cliente
            loadingPanel.SetActive(false);
            connectionPanel.SetActive(true);
        }
    }

    private void OnServerStarted()
    {
        Debug.Log("Servidor iniciado!");
        loadingPanel.SetActive(false);
        inGameUI.SetActive(true);
    }

    public void Disconnect()
    {
        NetworkManager.Singleton.Shutdown();
        connectionPanel.SetActive(true);
        inGameUI.SetActive(false);
        loadingPanel.SetActive(false);
    }

    void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted -= OnServerStarted;
        }
    }
}