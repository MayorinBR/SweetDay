using UnityEngine;
using Unity.Netcode;
using System.Collections;
using UnityEngine.SceneManagement;

public class GameManager : NetworkBehaviour
{
    public NetworkVariable<int> score = new NetworkVariable<int>(0);
    public NetworkVariable<int> playerLives = new NetworkVariable<int>(3);
    public NetworkVariable<float> gameTimer = new NetworkVariable<float>(120.0f);

    public int scoreToWin = 10;
    public GameObject endGamePanel;
    private bool gameEnded = false;
    public CoinSpawner coinSpawner;
    public GameObject guardPrefab;
    public GameObject playerPrefab;
    public Transform[] playerSpawnPoints;

    private UIManager _uiManager;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        score.OnValueChanged += OnScoreChanged;
        playerLives.OnValueChanged += OnLivesChanged;
        gameTimer.OnValueChanged += OnTimerChanged;

        _uiManager = FindFirstObjectByType<UIManager>();

        if (IsServer)
        {
            coinSpawner.SpawnCoins();
            SpawnGuards();
            SpawnPlayersForConnectedClients();
        }

        // Atualiza UI inicial
        if (_uiManager != null)
        {
            _uiManager.UpdateScoreUI(score.Value);
            _uiManager.UpdateLivesUI(playerLives.Value);
            _uiManager.UpdateTimerUI(gameTimer.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        score.OnValueChanged -= OnScoreChanged;
        playerLives.OnValueChanged -= OnLivesChanged;
        gameTimer.OnValueChanged -= OnTimerChanged;
    }

    void Update()
    {
        if (IsServer && !gameEnded)
        {
            if (gameTimer.Value > 0)
            {
                gameTimer.Value -= Time.deltaTime;
            }
            else
            {
                EndGame(false);
            }
        }
    }

    private void SpawnPlayersForConnectedClients()
    {
        if (!IsServer) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            SpawnPlayer(client.ClientId);
        }
    }

    private void SpawnPlayer(ulong clientId)
    {
        if (!IsServer) return;

        Transform spawnPoint = GetAvailableSpawnPoint();
        GameObject player = Instantiate(playerPrefab, spawnPoint.position, spawnPoint.rotation);

        NetworkObject playerNetworkObject = player.GetComponent<NetworkObject>();
        playerNetworkObject.SpawnAsPlayerObject(clientId);
    }

    private Transform GetAvailableSpawnPoint()
    {
        // Lógica simples - pode melhorar com verificação de ocupação
        return playerSpawnPoints[Random.Range(0, playerSpawnPoints.Length)];
    }

    private void OnScoreChanged(int oldScore, int newScore)
    {
        if (IsServer)
        {
            if (newScore >= scoreToWin)
            {
                EndGame(true);
            }
        }

        if (_uiManager != null)
        {
            _uiManager.UpdateScoreUI(newScore);
        }
    }

    private void OnLivesChanged(int oldLives, int newLives)
    {
        if (_uiManager != null)
        {
            _uiManager.UpdateLivesUI(newLives);
        }
    }

    private void OnTimerChanged(float oldTime, float newTime)
    {
        if (_uiManager != null)
        {
            _uiManager.UpdateTimerUI(newTime);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void LoseLifeServerRpc()
    {
        if (gameEnded) return;

        playerLives.Value--;

        if (playerLives.Value <= 0)
        {
            EndGame(false);
        }
        else
        {
            // Respawn do jogador
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                NetworkObject playerObject = client.PlayerObject;
                if (playerObject != null)
                {
                    Transform spawnPoint = GetAvailableSpawnPoint();
                    playerObject.transform.position = spawnPoint.position;
                }
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SpawnCoinsFromButtonServerRpc(NetworkObjectReference buttonNetworkObject, Vector3 position, int count, float radius)
    {
        if (coinSpawner != null && buttonNetworkObject.TryGet(out NetworkObject networkObject))
        {
            ButtonSpawner buttonSpawner = networkObject.GetComponent<ButtonSpawner>();
            if (buttonSpawner != null)
            {
                coinSpawner.SpawnCoinsAroundPosition(position, count, radius);
                buttonSpawner.SetButtonActiveClientRpc(false);
                StartCoroutine(ReactivateButtonAfterDelay(buttonSpawner, 5f));
            }
        }
    }

    private IEnumerator ReactivateButtonAfterDelay(ButtonSpawner button, float delay)
    {
        yield return new WaitForSeconds(delay);
        button.SetButtonActiveClientRpc(true);
    }

    [ClientRpc]
    public void ShowEndGamePanelClientRpc(bool won)
    {
        if (_uiManager != null)
        {
            _uiManager.ShowEndGamePanel(won);
        }
    }

    private void EndGame(bool won)
    {
        gameEnded = true;
        Debug.Log("Game Over! You " + (won ? "Won!" : "Lost!"));
        ShowEndGamePanelClientRpc(won);

        StartCoroutine(RestartGameAfterDelay(5f));
    }

    private IEnumerator RestartGameAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        NetworkManager.Singleton.Shutdown();
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void SpawnGuards()
    {
        if (!IsServer) return;
        // Implemente a lógica de spawn dos guardas aqui
    }

    [ServerRpc(RequireOwnership = false)]
    public void SubtractScoreServerRpc(int value)
    {
        if (gameEnded) return;
        score.Value = Mathf.Max(0, score.Value - value);
    }
}