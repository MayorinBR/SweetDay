using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

public class LocalSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject runnerPrefab;
    [SerializeField] private GameObject guardPrefab;

    private List<GameObject> _localPlayers = new List<GameObject>();

    // Chamado pelo Host quando a partida come�a
    public void SpawnLocalPlayers()
    {
        if (!IsServer) return;

        Debug.Log($"=== SPAWNING LOCAL PLAYERS ===");
        Debug.Log($"LocalPlayerCount: {GameSettings.LocalPlayerCount}");

        // Primeiro, limpa qualquer jogador local existente
        CleanupLocalPlayers();

        // Se for split screen (mais de 1 jogador local)
        if (GameSettings.LocalPlayerCount > 1)
        {
            Debug.Log($"Spawnando {GameSettings.LocalPlayerCount - 1} jogador(es) local(is) adicional(is)");

            // Para cada jogador local adicional (come�ando do 1 porque o jogador 0 j� existe)
            for (int i = 1; i < GameSettings.LocalPlayerCount; i++)
            {
                SpawnAdditionalLocalPlayer(i);
            }
        }

        Debug.Log($"Total de jogadores locais spawnados: {_localPlayers.Count}");

        // Notifica o SplitScreenManager para configurar as c�meras
        StartCoroutine(NotifySplitScreenManager());
    }

    private System.Collections.IEnumerator NotifySplitScreenManager()
    {
        // Espera um frame para garantir que todos os jogadores foram spawnados
        yield return null;

        SplitScreenManager splitScreenManager = FindFirstObjectByType<SplitScreenManager>();
        if (splitScreenManager != null)
        {
            Debug.Log("Notificando SplitScreenManager para configurar c�meras...");
            splitScreenManager.RefreshCameras();
        }
        else
        {
            Debug.LogWarning("SplitScreenManager n�o encontrado!");
        }
    }
    private void SpawnAdditionalLocalPlayer(int playerIndex)
    {
        // 1. Defini��o do Prefab e Tipo
        PlayerType hostPlayerType = GameSettings.IsCatcher ? PlayerType.Catcher : PlayerType.Runner;
        GameObject prefabToSpawn = (hostPlayerType == PlayerType.Runner) ? runnerPrefab : guardPrefab;

        // 2. Escolha do ponto de spawn
        Vector3 spawnPos = Vector3.zero;
        if (GameManager.Instance != null)
        {
            spawnPos = (hostPlayerType == PlayerType.Runner) ?
                GameManager.Instance.playerSpawnPoints[playerIndex % GameManager.Instance.playerSpawnPoints.Length].position :
                GameManager.Instance.guardSpawnPoints[playerIndex % GameManager.Instance.guardSpawnPoints.Length].position;
        }

        // 3. Instancia o prefab correto
        GameObject newPlayer = Instantiate(prefabToSpawn, spawnPos, Quaternion.identity);

        // 4. Configuração de Rede
        NetworkObject netObj = newPlayer.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            netObj.SpawnAsPlayerObject(NetworkManager.Singleton.LocalClientId, false);
        }

        // 5. Configuração de IDs e Nomes PRIMEIRO (antes dos controles)
        int displayId = playerIndex + 1;
        if (newPlayer.TryGetComponent<PlayerMovement>(out var pm))
        {
            pm.playerNumber.Value = displayId;
            newPlayer.name = "Runner_P" + displayId;
        }
        else if (newPlayer.TryGetComponent<Guard>(out var g))
        {
            g.playerNumber.Value = displayId;
            newPlayer.name = "Catcher_P" + displayId;
        }

        // 6. ATRIBUIÇÃO DE CONTROLE (CORRIGIDA - removendo UnpairDevices problemático)
        // Adia a configuração de controles para o próximo frame
        StartCoroutine(AssignControlsNextFrame(newPlayer, playerIndex));

        _localPlayers.Add(newPlayer);
    }

    private System.Collections.IEnumerator AssignControlsNextFrame(GameObject player, int playerIndex)
    {
        // Espera até o próximo frame para garantir que o PlayerInput está inicializado
        yield return null;

        // Garante que o ControlSetupManager existe
        EnsureControlSetupManager();

        if (ControlSetupManager.Instance != null)
        {
            ControlSetupManager.Instance.AssignControlScheme(player, playerIndex);
        }
        else
        {
            Debug.LogWarning("ControlSetupManager não encontrado, usando configuração básica");
            TryBasicControlSetup(player, playerIndex);
        }
    }

    private void TryBasicControlSetup(GameObject player, int playerIndex)
    {
        PlayerInput pInput = player.GetComponent<PlayerInput>();
        if (pInput == null) return;

        // Configuração básica tolerante a falta de dispositivos
        var gamepads = Gamepad.all;

        if (playerIndex == 0)
        {
            // Player 1 tenta usar teclado ou primeiro gamepad
            if (Keyboard.current != null)
            {
                pInput.SwitchCurrentControlScheme("Keyboard", Keyboard.current, Mouse.current);
            }
            else if (gamepads.Count > 0)
            {
                pInput.SwitchCurrentControlScheme("Gamepad", gamepads[0]);
            }
            else
            {
                Debug.LogWarning("Player 1: Nenhum dispositivo de entrada encontrado!");
            }
        }
        else if (playerIndex < gamepads.Count)
        {
            // Jogadores 2, 3, 4 usam gamepads se disponíveis
            pInput.SwitchCurrentControlScheme("Gamepad", gamepads[playerIndex]);
        }
        else
        {
            // Para jogadores sem controle específico, podem compartilhar teclado
            // ou simplesmente não terão controle (movimento pelo código)
            Debug.LogWarning($"Player {playerIndex + 1}: Nenhum controle específico disponível. Pode precisar de controle alternativo.");
        }
    }

    private void EnsureControlSetupManager()
    {
        if (ControlSetupManager.Instance == null)
        {
            GameObject managerObj = new GameObject("ControlSetupManager");
            managerObj.AddComponent<ControlSetupManager>();
            DontDestroyOnLoad(managerObj);
            Debug.Log("ControlSetupManager criado automaticamente");
        }
    }

    private void CleanupLocalPlayers()
    {
        foreach (var player in _localPlayers)
        {
            if (player != null)
            {
                NetworkObject netObj = player.GetComponent<NetworkObject>();
                if (netObj != null && netObj.IsSpawned)
                {
                    netObj.Despawn();
                }
                Destroy(player);
            }
        }
        _localPlayers.Clear();
    }

    // Chamado quando o jogo termina ou reinicia
    public void DespawnLocalPlayers()
    {
        if (!IsServer) return;
        CleanupLocalPlayers();
    }

    void OnDestroy()
    {
        // Limpa os jogadores locais quando este objeto for destruído
        CleanupLocalPlayers();

        // Libera dispositivos se o ControlSetupManager existir
        if (ControlSetupManager.Instance != null)
        {
            for (int i = 0; i < GameSettings.LocalPlayerCount; i++)
            {
                ControlSetupManager.Instance.ReleasePlayerDevice(i);
            }
        }
    }
}