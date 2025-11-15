using UnityEngine;
using Unity.Netcode;
using System.Collections;

public class Guard : NetworkBehaviour
{
    // Public and Inspector-visible variables
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 720f;

    [Header("Attack Settings")]
    public GameObject attackHitboxPrefab; // Arraste o 'CatcherAttackHitbox' para cá no Inspector
    public float attackCooldown = 1f;
    private float _lastAttackTime = -10f; // Inicializa para poder atacar imediatamente
    private CatcherAttack _catcherAttackScript; // Referência ao script do ataque
    public float swingDuration = 0.2f;
    private bool _isSwinging = false;

    [Header("Weapon Visual Settings")]
    public Vector3 idleLocalPosition = new Vector3(0.6f, 0.0f, 0.35f); // Posição vertical de descanso
    public Vector3 attackLocalPosition = new Vector3(0f, 0f, 0.6f); // Posição do ataque
    public Vector3 idleLocalRotationEuler = new Vector3(0f, 90f, 0f);

    private float _verticalVelocity;
    [Header("Physics Settings")]
    public float gravity = -9.81f; // Valor padrão para gravidade

    // Variables for smoothing movement on remote clients
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    public float networkMovementSmoothness = 5f;

    // Private variables
    private CharacterController _characterController;
    private GameManager _gameManager;

    private float _lastPositionUpdateTime = 0f;
    private const float POSITION_UPDATE_INTERVAL = 0.1f; // 10 updates per second

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Initialize CharacterController and network position/rotation
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            Debug.LogError("CharacterController not found on Guard GameObject. Please add one!");
        }

        if (attackHitboxPrefab != null)
        {
            GameObject hitboxInstance = Instantiate(attackHitboxPrefab, transform);
            _catcherAttackScript = hitboxInstance.GetComponent<CatcherAttack>();

            if (_catcherAttackScript != null)
            {
                _catcherAttackScript.ownerNetworkObject = GetComponent<NetworkObject>();
            }

            // Garante que o objeto visual esteja ATIVO
            hitboxInstance.SetActive(true);

            // Define a posição e rotação inicial de descanso (vertical)
            hitboxInstance.transform.localPosition = idleLocalPosition;
            hitboxInstance.transform.localRotation = Quaternion.Euler(idleLocalRotationEuler);

            // A ativação/desativação do dano será feita em PerformAttack/CatcherAttack.cs
            _catcherAttackScript.enabled = false;
            Collider col = hitboxInstance.GetComponent<Collider>();
            if (col != null) col.enabled = false;
        }

        // Encontra o GameManager
        _gameManager = FindFirstObjectByType<GameManager>();

        _networkPosition = transform.position;
        _networkRotation = transform.rotation;
    }

    void Update()
    {
        if (IsOwner)
        {
            // Se estiver balançando, não lida com movimento
            if (!_isSwinging)
            {
                HandleMovement();
            }
            HandleAttackInput();

            // Sincroniza a posição do proprietário para o servidor em intervalos regulares
            if (Time.time > _lastPositionUpdateTime + POSITION_UPDATE_INTERVAL)
            {
                SubmitPositionServerRpc(transform.position, transform.rotation);
                _lastPositionUpdateTime = Time.time;
            }
        }
        else
        {
            // Interpola o movimento em clientes remotos
            transform.position = Vector3.Lerp(transform.position, _networkPosition, Time.deltaTime * networkMovementSmoothness);
            transform.rotation = Quaternion.Lerp(transform.rotation, _networkRotation, Time.deltaTime * networkMovementSmoothness);
        }

        // Aplica gravidade localmente para colisões/chão
        ApplyGravity();
    }

    private void ApplyGravity()
    {
        if (_characterController.isGrounded)
        {
            _verticalVelocity = -0.5f; // Valor pequeno para garantir que o isGrounded seja verdadeiro
        }
        else
        {
            _verticalVelocity += gravity * Time.deltaTime;
        }

        // Move o CharacterController com a gravidade
        _characterController.Move(new Vector3(0, _verticalVelocity * Time.deltaTime, 0));
    }

    private void HandleMovement()
    {
        // Pega os inputs do jogador (assumindo inputs do Unity)
        float horizontalInput = Input.GetAxis("Horizontal");
        float verticalInput = Input.GetAxis("Vertical");

        // Calcule o movimento horizontal (X e Z)
        Vector3 movement = new Vector3(horizontalInput, 0f, verticalInput);
        if (movement.magnitude > 1f)
        {
            movement.Normalize();
        }

        // Move APENAS no plano XZ (horizontal)
        _characterController.Move(movement * moveSpeed * Time.deltaTime);

        // Lógica de rotação baseada no movimento
        if (movement != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    private void HandleAttackInput()
    {
        // Exemplo: Botão Esquerdo do Mouse ou uma tecla (ex: 'Space')
        if (Input.GetButtonDown("Fire1")  || Input.GetKeyDown(KeyCode.X) && Time.time >= _lastAttackTime + attackCooldown)
        {
            // Chama o RPC para sincronizar o ataque para todos os clientes, mas
            // APENAS o Servidor deve gerenciar a ativação do hitbox e o dano.
            // Para simplicidade, vamos diretamente para o RPC.
            RequestAttackServerRpc();

            // Atualiza o tempo do último ataque no lado do cliente (para o cooldown visual/lógico local)
            _lastAttackTime = Time.time;
        }
    }

    // RPC para sincronizar a posição e rotação do guarda
    [ServerRpc(RequireOwnership = false)]
    private void SubmitPositionServerRpc(Vector3 pos, Quaternion rot)
    {
        _networkPosition = pos;
        _networkRotation = rot;
    }

    void OnTriggerEnter(Collider other)
    {
        // Apenas o servidor processa a colisão
        if (!IsServer) return;

        // O guarda colidiu com um jogador (ladrão)
        if (other.CompareTag("Player"))
        {
            var gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager != null)
            {
                //_gameManager.LoseLifeServerRpc();

                // O Guard deve ser desativado temporariamente ou teleportado para evitar multi-colisão
                // O GameManager lida com o teleport dos jogadores (ladrões) após a perda de vida.
            }
        }
    }

    // RPC chamado pelo cliente para pedir ao servidor para executar o ataque
    [ServerRpc]
    private void RequestAttackServerRpc()
    {
        // 1. O Servidor executa o ataque (ativa o hitbox para o dano)
        PerformAttack(); // O hitbox fica ativo pelo tempo de dano (0.2s, definido em CatcherAttack.cs)

        // 2. O Servidor diz a TODOS os clientes para executarem a animação visual.
        AnimateAttackClientRpc();
    }

    [ClientRpc]
    private void AnimateAttackClientRpc()
    {
        // Inicia a coroutine de animação em todos os clientes
        StartCoroutine(SwingAnimationCoroutine(swingDuration));
    }

    // Coroutine para a animação do swing da arma
    private IEnumerator SwingAnimationCoroutine(float duration)
    {
        if (_catcherAttackScript == null) yield break;

        _isSwinging = true;
        Transform hitboxTransform = _catcherAttackScript.transform;

        // --- Posições e Rotações ---
        Vector3 idlePos = idleLocalPosition;
        Vector3 attackPos = attackLocalPosition;

        // Rotações com base no novo Idle (0, 90, 0)
        Quaternion startRotation = Quaternion.Euler(idleLocalRotationEuler); // (0, 90, 0)

        // Rotação horizontal inicial: (0, 90, 0) + giro de 90 graus no Eixo X local
        // O Quaternion.Euler(90, 90, 0) alcança isso.
        Quaternion midRotation = Quaternion.Euler(90, 90, 0);

        // Rotação horizontal final: (90, 90, 0) + arco de 150 graus no Eixo Y local
        // Isso move o arco de 90 graus para 240 graus no Y, ou seja, 90 + 150.
        // Para um movimento mais natural, vamos girar em Y de 90 para 240.
        Quaternion endRotation = Quaternion.Euler(90, -90, 0);

        // --- FASE 1: Transição Rápida para a Posição de Ataque (Horizontal) ---
        // ... (código existente da Fase 1 - Interpola Posição de idlePos para attackPos)
        float preSwingTime = duration * 0.1f;
        float elapsedTime = 0f;

        hitboxTransform.localPosition = idlePos;
        hitboxTransform.localRotation = startRotation; // Garante o ponto de partida

        while (elapsedTime < preSwingTime)
        {
            float t = elapsedTime / preSwingTime;
            hitboxTransform.localPosition = Vector3.Lerp(idlePos, attackPos, t);
            hitboxTransform.localRotation = Quaternion.Lerp(startRotation, midRotation, t);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        hitboxTransform.localPosition = attackPos;
        hitboxTransform.localRotation = midRotation;

        // --- FASE 2: O giro de "meia-lua" horizontal (Dano Ativo Aqui) ---
        // ... (código existente da Fase 2 - Interpola Rotação de midRotation para endRotation)
        float swingRotateTime = duration * 0.8f;
        float swingElapsedTime = 0f;

        while (swingElapsedTime < swingRotateTime)
        {
            float t = swingElapsedTime / swingRotateTime;
            hitboxTransform.localRotation = Quaternion.Lerp(midRotation, endRotation, t);
            swingElapsedTime += Time.deltaTime;
            yield return null;
        }
        hitboxTransform.localRotation = endRotation;

        // --- FASE 3: Retorno à Posição de Descanso ---
        // ... (código existente da Fase 3 - Interpola Posição de attackPos para idlePos e Rotação de endRotation para startRotation)
        float postSwingTime = duration * 0.1f;
        float elapsedReturnTime = 0f;

        while (elapsedReturnTime < postSwingTime)
        {
            float t = elapsedReturnTime / postSwingTime;
            hitboxTransform.localPosition = Vector3.Lerp(attackPos, idlePos, t);
            hitboxTransform.localRotation = Quaternion.Lerp(endRotation, startRotation, t);
            elapsedReturnTime += Time.deltaTime;
            yield return null;
        }

        // Garante o estado final de descanso
        hitboxTransform.localPosition = idlePos;
        hitboxTransform.localRotation = startRotation;

        _isSwinging = false;
    }

    // A função PerformAttack() deve ser modificada para ativar apenas o script:
    private void PerformAttack()
    {
        if (_catcherAttackScript != null)
        {
            // ATIVA O SCRIPT. O OnEnable do CatcherAttack.cs vai ATIVAR o Collider
            _catcherAttackScript.enabled = true;
        }
    }
}