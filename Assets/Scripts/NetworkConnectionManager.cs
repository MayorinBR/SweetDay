using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using Unity.Services.Relay;
using Unity.Networking.Transport.Relay;
using System.Threading.Tasks;
using Unity.Services.Relay.Models;
using Unity.Netcode.Transports.UTP;

public class NetworkConnectionManager : MonoBehaviour
{
    private Lobby _currentLobby;
    private string _playerLobbyId;
    private string _joinCode = "";
    private string _lobbyCode = "";

    void Awake()
    {
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            NetworkManager.Singleton.OnServerStarted += OnServerStarted;
        }
    }

    async void Start()
    {
        try
        {
            await UnityServices.InitializeAsync();
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
            _playerLobbyId = AuthenticationService.Instance.PlayerId;
            Debug.Log($"Autenticação anônima bem-sucedida! Player ID: {_playerLobbyId}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Falha na inicialização do Unity Services: {e.Message}");
        }
    }

    void OnGUI()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        GUILayout.BeginArea(new Rect(10, 10, 300, 400));

        if (!NetworkManager.Singleton.IsClient && !NetworkManager.Singleton.IsServer)
        {
            StartButtons();
        }
        else
        {
            StatusLabels();

            if (GUILayout.Button("Disconnect"))
            {
                Disconnect();
            }
        }

        GUILayout.EndArea();
    }

    void StartButtons()
    {
        if (GUILayout.Button("Host"))
        {
            StartHost();
        }

        GUILayout.Label("OU");

        GUILayout.BeginHorizontal();
        _joinCode = GUILayout.TextField(_joinCode, 6);
        if (GUILayout.Button("Client"))
        {
            StartClient();
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("OU");

        if (GUILayout.Button("Server"))
        {
            StartServer();
        }
    }

    void StatusLabels()
    {
        var mode = NetworkManager.Singleton.IsHost ? "Host" : NetworkManager.Singleton.IsServer ? "Server" : "Client";
        GUILayout.Label("Modo: " + mode);

        if (NetworkManager.Singleton.IsHost)
        {
            GUILayout.Label("Código do Lobby: " + _lobbyCode);
        }
    }

    public async void StartHost()
    {
        try
        {
            int maxPlayers = 4;
            var allocation = await RelayService.Instance.CreateAllocationAsync(maxPlayers);
            var relayJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            Debug.Log($"Relay join code: {relayJoinCode}");

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, "dtls"));

            CreateLobbyOptions options = new CreateLobbyOptions
            {
                IsPrivate = true,
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, relayJoinCode) }
                }
            };
            _currentLobby = await LobbyService.Instance.CreateLobbyAsync("My Awesome Lobby", maxPlayers, options);
            _lobbyCode = _currentLobby.LobbyCode;
            Debug.Log($"Lobby criado com o código: {_currentLobby.LobbyCode}");

            NetworkManager.Singleton.StartHost();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Erro ao iniciar o host do Lobby/Relay: {e.Message}");
        }
    }

    public async void StartClient()
    {
        if (string.IsNullOrEmpty(_joinCode))
        {
            Debug.LogError("Por favor, insira um código de conexão válido.");
            return;
        }

        try
        {
            var lobby = await LobbyService.Instance.JoinLobbyByCodeAsync(_joinCode);
            _currentLobby = lobby;
            _playerLobbyId = AuthenticationService.Instance.PlayerId;

            string relayJoinCode = _currentLobby.Data["RelayJoinCode"].Value;
            Debug.Log($"Lobby encontrado. Código do Relay: {relayJoinCode}");

            var joinAllocation = await RelayService.Instance.JoinAllocationAsync(relayJoinCode);
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, "dtls"));

            NetworkManager.Singleton.StartClient();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Erro ao conectar ao Lobby/Relay: {e.Message}");
        }
    }

    public void StartServer()
    {
        NetworkManager.Singleton.StartServer();
    }

    private void OnClientConnected(ulong clientId)
    {
        Debug.Log($"Cliente conectado: {clientId}");
    }

    private void OnClientDisconnected(ulong clientId)
    {
        Debug.Log($"Cliente desconectado: {clientId}");
    }

    private void OnServerStarted()
    {
        Debug.Log("Servidor iniciado!");
    }

    public void Disconnect()
    {
        NetworkManager.Singleton.Shutdown();
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

    public async void LeaveLobby()
    {
        if (_currentLobby != null)
        {
            await LobbyService.Instance.RemovePlayerAsync(_currentLobby.Id, _playerLobbyId);
            _currentLobby = null;
        }
    }

    private RelayServerData CreateRelayHostData(Allocation allocation, string connectionType = "dtls")
        => AllocationUtils.ToRelayServerData(allocation, connectionType);

    private RelayServerData CreateRelayClientData(JoinAllocation allocation, string connectionType = "dtls")
        => AllocationUtils.ToRelayServerData(allocation, connectionType);

}