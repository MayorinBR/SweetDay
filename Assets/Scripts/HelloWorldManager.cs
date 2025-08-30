using Unity.Netcode;
using UnityEngine;
using TMPro;
using Unity.Netcode.Transports.UTP;

namespace HelloWorld
{
    public class HelloWorldManager : MonoBehaviour
    {
        private NetworkManager m_NetworkManager;

        [Header("UI References")]
        public TMP_InputField ipInputField;

        void Awake()
        {
            m_NetworkManager = GetComponent<NetworkManager>();

            // Configura callbacks de rede
            m_NetworkManager.OnClientConnectedCallback += OnClientConnected;
            m_NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 300));

            // Mostra apenas os botões de conexão se não estiver conectado
            if (!m_NetworkManager.IsClient && !m_NetworkManager.IsServer)
            {
                StartButtons();
            }
            else
            {
                StatusLabels();

                // Botão de desconexão
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
            if (GUILayout.Button("Client"))
            {
                StartClient();
            }
            if (GUILayout.Button("Server"))
            {
                StartServer();
            }
            if (GUILayout.Button("Quit"))
            {
                QuitGame();
            }
        }

        void StatusLabels()
        {
            var mode = m_NetworkManager.IsHost ? "Host" : m_NetworkManager.IsServer ? "Server" : "Client";
            GUILayout.Label("Transport: " + m_NetworkManager.NetworkConfig.NetworkTransport.GetType().Name);
            GUILayout.Label("Mode: " + mode);
            GUILayout.Label("Connected Clients: " + m_NetworkManager.ConnectedClients.Count);

            // Mostra o IP do host para outros jogadores se conectarem
            if (m_NetworkManager.IsHost || m_NetworkManager.IsServer)
            {
                GUILayout.Label("Host IP: " + GetLocalIPAddress());
            }
        }

        public void StartHost()
        {
            m_NetworkManager.StartHost();
        }

        public void StartClient()
        {
            if (!string.IsNullOrEmpty(ipInputField.text))
            {
                // Configura o transporte com o IP inserido
                var transport = m_NetworkManager.GetComponent<UnityTransport>();
                if (transport != null)
                {
                    transport.SetConnectionData(ipInputField.text, 7777);
                }
                m_NetworkManager.StartClient();
            }
            else
            {
                Debug.LogError("Please enter a valid IP address");
            }
        }

        public void StartServer()
        {
            m_NetworkManager.StartServer();
        }

        public void Disconnect()
        {
            m_NetworkManager.Shutdown();
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"Client connected: {clientId}");

            // Ativa a UI de jogo quando conectado
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"Client disconnected: {clientId}");
        }

        // Método para obter o IP local do host
        private string GetLocalIPAddress()
        {
            var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
            return "127.0.0.1";
        }

        void OnDestroy()
        {
            if (m_NetworkManager != null)
            {
                m_NetworkManager.OnClientConnectedCallback -= OnClientConnected;
                m_NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        // Método para o botão Quit (pode ser chamado por UI Button)
        public void QuitGame()
        {
            Application.Quit();
        }
    }
}