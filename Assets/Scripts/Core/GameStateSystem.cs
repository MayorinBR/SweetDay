using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Handles score, hit processing, the countdown-to-start sequence, win/lose
/// resolution, pause state, and both reset flows (in-place restart and full
/// reset). Plain <see cref="MonoBehaviour"/>: it reads and writes the
/// NetworkVariables owned by <see cref="GameManager"/> and calls the ClientRpc
/// wrappers exposed there, since RPCs must live on a NetworkBehaviour.
/// </summary>
public class GameStateSystem : MonoBehaviour
{
    // ====================================================================
    // Inspector Fields
    // ====================================================================

    /// <summary>Score the runners must reach to win.</summary>
    public int scoreToWin = 20;

    // ====================================================================
    // Private State
    // ====================================================================

    private GameManager _gameManager;
    private GameSpawnSystem _spawnSystem;
    private bool _gameEnded;

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void Awake()
    {
        _gameManager = GetComponent<GameManager>();
        _spawnSystem = GetComponent<GameSpawnSystem>();
    }

    private void Update()
    {
        if (!_gameManager.IsServer || !_gameManager.gameStarted.Value || _gameEnded || _gameManager.isPaused.Value)
            return;

        _gameManager.gameTimer.Value -= Time.deltaTime;

        if (_gameManager.gameTimer.Value <= 0f)
        {
            _gameManager.gameTimer.Value = 0f;
            EndGame(false);
        }
    }

    // ====================================================================
    // Public – Score / Hit
    // ====================================================================

    /// <summary>Adds <paramref name="value"/> to the global score. Ends the game if the target score is reached.</summary>
    public void AddScore(int value)
    {
        if (_gameEnded) return;

        _gameManager.score.Value += value;

        if (_gameManager.score.Value >= scoreToWin)
        {
            _gameManager.score.Value = scoreToWin;
            EndGame(true);
        }
    }

    /// <summary>Subtracts <paramref name="value"/> from the global score, clamped to zero.</summary>
    public void SubtractScore(int value)
    {
        if (_gameEnded) return;
        _gameManager.score.Value = Mathf.Max(0, _gameManager.score.Value - value);
    }

    /// <summary>
    /// Processes a hit on the player referenced by <paramref name="playerRef"/>:
    /// checks invulnerability, decrements lives, drops coins, and checks for game-over.
    /// </summary>
    public void ProcessPlayerHit(NetworkObjectReference playerRef)
    {
        if (!playerRef.TryGet(out NetworkObject playerNetObj))
        {
            Debug.LogWarning("[GameStateSystem] ProcessPlayerHit: NetworkObject not found.");
            return;
        }

        if (!playerNetObj.TryGetComponent<PlayerMovement>(out var pm))
        {
            Debug.LogWarning("[GameStateSystem] ProcessPlayerHit: PlayerMovement not found.");
            return;
        }

        if (pm.IsInvulnerable.Value) return;

        if (_gameManager.playerLives.Value > 0) _gameManager.playerLives.Value--;

        int coinsLost = _spawnSystem.coinSpawner != null
            ? pm.ReceiveHitAndDropCoins_Server(_spawnSystem.coinSpawner)
            : 0;

        if (coinsLost > 0)
            _gameManager.score.Value = Mathf.Max(0, _gameManager.score.Value - coinsLost);

        if (_gameManager.playerLives.Value <= 0)
            EndGame(false);
    }

    // ====================================================================
    // Public – Lifecycle
    // ====================================================================

    /// <summary>Starts the match: sets the game-started flag. Server-only.</summary>
    public void StartGame()
    {
        if (!_gameManager.IsServer) return;

        if (_gameManager.gameStarted.Value)
        {
            Debug.LogWarning("[GameStateSystem] StartGame called but game is already running.");
            return;
        }

        _gameManager.gameStarted.Value = true;
    }

    /// <summary>Ends the game, stops the timer, and notifies all clients of the result.</summary>
    /// <param name="runnersWon"><c>true</c> if the runners reached the score target; <c>false</c> if catchers won.</param>
    public void EndGame(bool runnersWon)
    {
        if (!_gameManager.IsServer || _gameEnded) return;

        _gameEnded = true;
        _gameManager.gameStarted.Value = false;

        _gameManager.NotifyEndGameClientRpc(runnersWon);
    }

    /// <summary>
    /// Runs the 3-2-1-Go countdown on all clients, then calls <see cref="StartGame"/>.
    /// Players are already spawned but movement is blocked until <c>gameStarted</c>
    /// becomes <c>true</c> at the end of the sequence.
    /// </summary>
    public IEnumerator CountdownAndStart()
    {
        _spawnSystem.SpawnCoins();

        _gameManager.StartCountdownClientRpc();
        yield return new WaitForSeconds(4.5f);
        StartGame();
    }

    /// <summary>Sets or clears the global pause state.</summary>
    public void SetPaused(bool paused) => _gameManager.isPaused.Value = paused;

    // ====================================================================
    // Public – Reset Flows
    // ====================================================================

    /// <summary>
    /// Fully resets the game: despawns players and coins, resets variables,
    /// resets button zones, and respawns coins.
    /// </summary>
    public void ResetGame()
    {
        if (!_gameManager.IsServer) return;

        _spawnSystem.ResetSpawnState();
        _spawnSystem.DespawnAllPlayers();

        _gameManager.score.Value = 0;
        _gameManager.playerLives.Value = GameManager.InitialLives;
        _gameManager.gameTimer.Value = GameManager.GameDuration;
        _gameManager.gameStarted.Value = false;
        _gameEnded = false;

        _spawnSystem.DespawnAllCoins();
        _spawnSystem.SpawnCoins();

        var bm = FindAnyObjectByType<ButtonManager>();
        bm?.FullReset();
        bm?.RespawnInitialButtons();

        _gameManager.HideEndGamePanelClientRpc();
        _gameManager.ReassignCamerasClientRpc();
    }

    /// <summary>
    /// Performs a full in-place restart without disconnecting any player:
    /// resets positions, coins, lives, timer, button zones, and re-runs the
    /// countdown sequence.
    /// </summary>
    public IEnumerator FullRestartSequence()
    {
        _gameManager.HideEndGamePanelClientRpc();

        // Block input while resetting.
        _gameManager.gameStarted.Value = false;
        _gameManager.isPaused.Value = false;
        _gameEnded = false;

        // Reset global state.
        _gameManager.score.Value = 0;
        _gameManager.playerLives.Value = GameManager.InitialLives;
        _gameManager.gameTimer.Value = GameManager.GameDuration;

        // Reset per-player state without despawning.
        foreach (var pm in FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude))
        {
            pm.coinsCarried.Value = 0;
            pm.IsInvulnerable.Value = false;
        }
        foreach (var g in FindObjectsByType<Guard>(FindObjectsInactive.Exclude))
        {
            g.AttackCooldownRemaining.Value = 0f;
        }

        _spawnSystem.ResetAllPlayersPosition();

        // Clear coins and button zones, then rebuild button pools.
        _spawnSystem.DespawnAllCoins();

        var bm = FindAnyObjectByType<ButtonManager>();
        bm?.FullReset();
        bm?.RespawnInitialButtons();

        // One frame so all despawns settle before spawning new objects.
        yield return null;

        // Spawns coins, shows 3-2-1-Go, then sets gameStarted = true.
        StartCoroutine(CountdownAndStart());
    }

    // ====================================================================
    // Player Disconnect During Game
    // ====================================================================

    /// <summary>
    /// Server-side handler for <c>NetworkManager.OnClientDisconnectCallback</c>
    /// while a match is running. Subscribed to from <see cref="GameManager"/>
    /// since the <see cref="GameManager.NotifyPlayerLeftClientRpc"/> it triggers
    /// must live on a NetworkBehaviour.
    /// </summary>
    public void OnClientDisconnectedDuringGame(ulong clientId)
    {
        if (!_gameManager.gameStarted.Value) return;
        _gameManager.NotifyPlayerLeftClientRpc();
    }
}
