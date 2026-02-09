using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public class ButtonManager : NetworkBehaviour
{
    [Header("Settings")]
    public int activeButtonsAtOnce = 4;
    public float respawnDelay = 10f;
    public float checkRadius = 1.5f;
    public LayerMask obstructionLayers;

    [Header("References")]
    public List<ButtonSpawner> allButtons = new List<ButtonSpawner>();

    private List<ButtonSpawner> _inactiveButtons = new List<ButtonSpawner>();
    private int _currentlyActiveCount = 0;
    private bool _isInitialized = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!IsServer) return;

        Debug.Log($"[ButtonManager] Initializing on Server with {allButtons.Count} buttons");

        _inactiveButtons.Clear();
        _currentlyActiveCount = 0;

        // Valida e adiciona todos os botões à lista de inativos
        foreach (var btn in allButtons)
        {
            if (btn == null)
            {
                Debug.LogError("[ButtonManager] Null button found in allButtons list!");
                continue;
            }

            if (!btn.IsSpawned)
            {
                Debug.LogWarning($"[ButtonManager] Button {btn.name} is not spawned yet!");
                continue;
            }

            _inactiveButtons.Add(btn);

            // No servidor, desativamos o GameObject diretamente
            btn.gameObject.SetActive(false);
            btn.ResetButton();

            // Sincroniza com os clientes
            DeactivateButtonClientRpc(btn.NetworkObjectId);
        }

        _isInitialized = true;
        Debug.Log($"[ButtonManager] Initialized with {_inactiveButtons.Count} inactive buttons");

        StartCoroutine(InitialSpawn());
    }

    IEnumerator InitialSpawn()
    {
        // Espera um pouco para garantir que a rede estabilizou
        yield return new WaitForSeconds(2f);

        Debug.Log($"[ButtonManager] Starting initial spawn. Inactive buttons: {_inactiveButtons.Count}");

        // Ativa apenas a quantidade permitida
        for (int i = 0; i < activeButtonsAtOnce; i++)
        {
            if (_inactiveButtons.Count > 0)
            {
                TrySpawnRandomButton();
            }
            else
            {
                Debug.LogWarning($"[ButtonManager] No inactive buttons available for spawn {i}");
            }

            // Pequeno delay entre spawns para evitar problemas
            yield return new WaitForSeconds(0.2f);
        }

        Debug.Log($"[ButtonManager] Initial spawn complete. Active: {_currentlyActiveCount}, Inactive: {_inactiveButtons.Count}");
    }

    public void TrySpawnRandomButton()
    {
        if (!IsServer)
        {
            Debug.LogWarning("[ButtonManager] TrySpawnRandomButton called on client!");
            return;
        }

        if (!_isInitialized)
        {
            Debug.LogWarning("[ButtonManager] TrySpawnRandomButton called before initialization!");
            return;
        }

        if (_inactiveButtons.Count == 0)
        {
            Debug.LogWarning("[ButtonManager] No inactive buttons available to spawn");
            return;
        }

        if (_currentlyActiveCount >= activeButtonsAtOnce)
        {
            Debug.LogWarning($"[ButtonManager] Already at max active buttons ({_currentlyActiveCount}/{activeButtonsAtOnce})");
            return;
        }

        StartCoroutine(SpawnSequence());
    }

    IEnumerator SpawnSequence()
    {
        bool spawned = false;
        int attempts = 0;
        int maxAttempts = Mathf.Min(10, _inactiveButtons.Count * 2);

        while (!spawned && attempts < maxAttempts && _inactiveButtons.Count > 0)
        {
            attempts++;
            int randomIndex = Random.Range(0, _inactiveButtons.Count);
            ButtonSpawner candidate = _inactiveButtons[randomIndex];

            if (candidate == null)
            {
                Debug.LogError($"[ButtonManager] Null candidate at index {randomIndex}!");
                _inactiveButtons.RemoveAt(randomIndex);
                continue;
            }

            // Verifica se o local está livre
            bool isBlocked = Physics.CheckSphere(candidate.transform.position, checkRadius, obstructionLayers);

            if (!isBlocked)
            {
                _inactiveButtons.RemoveAt(randomIndex);
                _currentlyActiveCount++;

                Debug.Log($"[ButtonManager] Spawning button {candidate.name} at position {candidate.transform.position}. Active: {_currentlyActiveCount}");

                // Ativa no Servidor
                candidate.gameObject.SetActive(true);
                candidate.ResetButton();

                // Sincroniza com os Clientes
                ActivateButtonClientRpc(candidate.NetworkObjectId);

                spawned = true;
            }
            else
            {
                Debug.LogWarning($"[ButtonManager] Button spawn location blocked (attempt {attempts}), retrying...");
                yield return new WaitForSeconds(0.5f);
            }
        }

        if (!spawned)
        {
            Debug.LogError($"[ButtonManager] Failed to spawn button after {attempts} attempts!");
        }
    }

    [ClientRpc]
    private void ActivateButtonClientRpc(ulong networkObjectId)
    {
        if (IsServer) return; // Servidor já ativou localmente

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
        {
            Debug.LogError("[ButtonManager] NetworkManager or SpawnManager is null in ActivateButtonClientRpc!");
            return;
        }

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var netObj))
        {
            if (netObj == null)
            {
                Debug.LogError($"[ButtonManager] NetworkObject {networkObjectId} is null!");
                return;
            }

            netObj.gameObject.SetActive(true);

            var spawner = netObj.GetComponent<ButtonSpawner>();
            if (spawner != null)
            {
                spawner.ResetButton();
                Debug.Log($"[ButtonManager Client] Activated button {netObj.name}");
            }
            else
            {
                Debug.LogError($"[ButtonManager] No ButtonSpawner component on {netObj.name}!");
            }
        }
        else
        {
            Debug.LogError($"[ButtonManager] Could not find NetworkObject with ID {networkObjectId}!");
        }
    }

    [ClientRpc]
    private void DeactivateButtonClientRpc(ulong networkObjectId)
    {
        if (IsServer) return; // Servidor já desativou localmente

        if (NetworkManager.Singleton == null || NetworkManager.Singleton.SpawnManager == null)
        {
            Debug.LogError("[ButtonManager] NetworkManager or SpawnManager is null in DeactivateButtonClientRpc!");
            return;
        }

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out var netObj))
        {
            if (netObj != null)
            {
                netObj.gameObject.SetActive(false);
                Debug.Log($"[ButtonManager Client] Deactivated button {netObj.name}");
            }
        }
        else
        {
            Debug.LogWarning($"[ButtonManager] Could not find NetworkObject with ID {networkObjectId} to deactivate");
        }
    }

    public void OnButtonCompleted(ButtonSpawner button)
    {
        if (!IsServer)
        {
            Debug.LogError("[ButtonManager] OnButtonCompleted called on client!");
            return;
        }

        if (button == null)
        {
            Debug.LogError("[ButtonManager] OnButtonCompleted called with null button!");
            return;
        }

        Debug.Log($"[ButtonManager] Button {button.name} completed. Active before: {_currentlyActiveCount}");

        _currentlyActiveCount--;

        // Desativa no Servidor
        button.gameObject.SetActive(false);
        button.ResetButton();

        // Sincroniza com os Clientes
        DeactivateButtonClientRpc(button.NetworkObjectId);

        // Adiciona de volta à lista de inativos se não estiver lá
        if (!_inactiveButtons.Contains(button))
        {
            _inactiveButtons.Add(button);
        }

        Debug.Log($"[ButtonManager] Button completed. Active: {_currentlyActiveCount}, Inactive: {_inactiveButtons.Count}");

        // Agenda o próximo spawn
        StartCoroutine(RespawnAfterDelay());
    }

    IEnumerator RespawnAfterDelay()
    {
        Debug.Log($"[ButtonManager] Waiting {respawnDelay}s before respawn...");
        yield return new WaitForSeconds(respawnDelay);
        TrySpawnRandomButton();
    }

    // Debug helper
    void OnDrawGizmos()
    {
        if (allButtons == null) return;

        foreach (var btn in allButtons)
        {
            if (btn == null) continue;

            Gizmos.color = btn.gameObject.activeSelf ? Color.green : Color.red;
            Gizmos.DrawWireSphere(btn.transform.position, checkRadius);
        }
    }
}