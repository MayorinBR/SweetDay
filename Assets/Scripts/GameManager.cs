using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
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
            var playerObject = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
            if (playerObject != null)
            {
                playerObject.transform.position = new Vector3(0, 1, 0);
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

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            var player = client.PlayerObject.GetComponent<PlayerMovement>();
            if (player != null)
            {
                // Implemente um RPC ou método para desabilitar o movimento se necessário
            }
        }

        StartCoroutine(RestartGameAfterDelay(3f));
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
        // Adicione aqui a lógica para spawnar os guardas
    }

    // ⭐ CORREÇÃO: スコアを減らすための新しいRPCメソッドを追加
    [ServerRpc(RequireOwnership = false)]
    public void SubtractScoreServerRpc(int value)
    {
        if (gameEnded) return;

        score.Value = Mathf.Max(0, score.Value - value);
    }
}