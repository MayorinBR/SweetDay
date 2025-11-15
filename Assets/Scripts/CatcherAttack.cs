// CatcherAttack.cs

using UnityEngine;
using Unity.Netcode;

public class CatcherAttack : MonoBehaviour
{
    // A referência ao NetworkObject do guarda/catcher que possui este ataque.
    // Usamos NetworkObject para garantir que apenas o Guarda "proprietário" cause dano.
    [HideInInspector] public NetworkObject ownerNetworkObject;
    private bool _hasHit = false; // Flag para garantir que o ataque só cause dano uma vez

    // Tempo que o hitbox deve ficar ativo (geralmente muito rápido)
    public float activeTime = 0.2f;

    // Referência ao GameManager
    private GameManager _gameManager;
    private Collider _collider;

    void Start()
    {
        // Encontra o GameManager
        _gameManager = FindFirstObjectByType<GameManager>();
    }

    void OnEnable()
    {
        //Ativa o Collider quando o script é ativado
        if (_collider != null) _collider.enabled = true;

        // Reinicia o estado ao ativar
        _hasHit = false;
        // Inicia o contador para desativar o dano após 'activeTime'
        Invoke(nameof(DeactivateDamage), activeTime); // Renomeamos Deactivate para DeactivateDamage
    }

    private void DeactivateDamage()
    {
        if (_collider != null) _collider.enabled = false;
        enabled = false; // Desativa o próprio script, parando o OnTriggerEnter
    }

    void Awake()
    {
        // Pega a referência do collider
        _collider = GetComponent<Collider>();
        if (_collider == null)
        {
            Debug.LogError("Collider not found on CatcherAttack Hitbox!");
        }
    }

    void OnTriggerEnter(Collider other)
    {
        // 1. Apenas o servidor deve processar a colisão de ataque
        if (!_gameManager.IsServer) return;

        if (ownerNetworkObject == null || _hasHit) return;

        // 2. Verifica se colidiu com um jogador (o "ladrão")
        if (other.CompareTag("Player"))
        {
            var playerNetworkObject = other.GetComponent<NetworkObject>();

            if (playerNetworkObject != null)
            {
                // 3. Garante que não está acertando o próprio guarda
                if (playerNetworkObject.OwnerClientId != ownerNetworkObject.OwnerClientId)
                {
                    // Obtém o ID do cliente do jogador atingido
                    ulong playerHitId = playerNetworkObject.OwnerClientId;

                    _hasHit = true;

                    if (_gameManager != null)
                    {
                        // 4. CHAMA O NOVO RPC DE PROCESSAMENTO DE DANO NO GAMEMANAGER
                        // Em vez de LoseLifeServerRpc(), chamamos o novo método centralizado
                        _gameManager.ProcessPlayerHitServerRpc(playerHitId);

                        // 5. Desativa o dano imediatamente após o hit.
                        DeactivateDamage();
                        CancelInvoke(nameof(DeactivateDamage)); // Cancela o Invoke do timer
                    }
                }
            }
        }
    }
}