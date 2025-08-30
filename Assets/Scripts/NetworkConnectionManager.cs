using UnityEngine;
using Unity.Netcode;
using TMPro;
using Unity.Netcode.Transports.UTP;
using System.Net.Sockets;
using System.Net;

public class NetworkConnectionManager : MonoBehaviour
{
    public TMP_InputField ipInputField;
    public GameObject connectionPanel;
    public GameObject loadingPanel;
    public GameObject inGameUI;

    void Awake()
    {
        // Certifica-se de que o singleton do NetworkManager existe
        if (NetworkManager.Singleton != null)
        {
            // Registra callbacks de eventos de rede
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
    }

    void Start()
    {
        // Ao iniciar, tenta preencher o campo de IP com o IP local para o host
        if (ipInputField != null)
        {
            ipInputField.text = GetLocalIPAddress();
        }
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
            loadingPanel.SetActive(false);
            connectionPanel.SetActive(true);
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

        // Ativa a UI de jogo quando um cliente/host se conecta.
        loadingPanel.SetActive(false);
        inGameUI.SetActive(true);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Cliente desconectado: {clientId}");

        if (NetworkManager.Singleton != null && !NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsHost)
        {
            // Volta para o menu se for cliente e se desconectar
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

    // Método para obter o endereço IP local da máquina
    private string GetLocalIPAddress()
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == AddressFamily.InterNetwork)
            {
                return ip.ToString();
            }
        }
        return "127.0.0.1"; // Retorna o IP de loopback se o IP local não for encontrado
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