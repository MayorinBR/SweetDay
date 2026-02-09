using UnityEngine;
using Unity.Netcode;
public class CatcherAttack : MonoBehaviour
{
    // A referÍncia ao NetworkObject do guarda/catcher que possui este ataque.
    // Usamos NetworkObject para garantir que apenas o Guarda "propriet·rio" cause dano.
    [HideInInspector] public NetworkObject ownerNetworkObject;
    private bool _hasHit = false; // Flag para garantir que o ataque sÅEcause dano uma vez

    // Tempo que o hitbox deve ficar ativo (geralmente muito r·pido)
    public float activeTime = 0.2f;

    // ReferÍncia ao GameManager
    private GameManager _gameManager;
    private Collider _collider;

    void Start()
    {
        // Encontra o GameManager
        _gameManager = FindFirstObjectByType<GameManager>();
    }

    void OnEnable()
    {
        //Ativa o Collider quando o script ÅEativado
        if (_collider != null) _collider.enabled = true;

        // Reinicia o estado ao ativar
        _hasHit = false;
        // Inicia o contador para desativar o dano apÛs 'activeTime'
        Invoke(nameof(DeactivateDamage), activeTime); // Renomeamos Deactivate para DeactivateDamage
    }

    private void DeactivateDamage()
    {
        if (_collider != null) _collider.enabled = false;
        enabled = false; // Desativa o prÛprio script, parando o OnTriggerEnter
    }

    void Awake()
    {
        // Pega a referÍncia do collider
        _collider = GetComponent<Collider>();
        if (_collider == null)
        {
            Debug.LogError("Collider not found on CatcherAttack Hitbox!");
        }
    }

    // CatcherAttack.cs - Modificar o mÈtodo OnTriggerEnter
    void OnTriggerEnter(Collider other)
    {
        // 1. Apenas o servidor deve processar a colis„o de ataque
        if (!_gameManager.IsServer) return;

        if (ownerNetworkObject == null || _hasHit) return;

        // 2. Verifica se colidiu com um jogador
        if (other.CompareTag("Player"))
        {
            var playerMovement = other.GetComponent<PlayerMovement>();
            if (playerMovement == null) return;

            var playerNetworkObject = other.GetComponent<NetworkObject>();
            if (playerNetworkObject != null)
            {
                // 3. Garante que n„o est· acertando o prÛprio guarda
                if (playerNetworkObject.OwnerClientId != ownerNetworkObject.OwnerClientId)
                {
                    // OBTER O ID CORRETO PARA SPLITSCREEN
                    // No splitscreen, todos os jogadores locais compartilham o mesmo OwnerClientId
                    // Precisamos usar um mÈtodo diferente para identificar qual jogador foi atingido

                    _hasHit = true;

                    if (_gameManager != null)
                    {
                        // 4. Passar o NetworkObject do jogador atingido em vez do ID
                        // Isso garante que o dano seja aplicado ao objeto correto
                        ulong playerHitId = playerNetworkObject.NetworkObjectId;

                        // Alternativa: passar o NetworkObjectReference
                        NetworkObjectReference playerRef = new NetworkObjectReference(playerNetworkObject);

                        // Chamar um novo mÈtodo que aceita NetworkObjectReference
                        _gameManager.ProcessPlayerHitWithReferenceServerRpc(playerRef);

                        // 5. Desativa o dano imediatamente apÛs o hit.
                        DeactivateDamage();
                        CancelInvoke(nameof(DeactivateDamage));
                    }
                }
            }
        }
    }
}