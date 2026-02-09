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

    // Network variables
    public NetworkVariable<bool> IsCatcher = new NetworkVariable<bool>(true);
    public NetworkVariable<float> AttackCooldownRemaining = new NetworkVariable<float>(0f);
    public NetworkVariable<int> playerNumber = new NetworkVariable<int>(0);

    // Variável local para o cooldown
    private float _localAttackCooldown = 0f;
    private bool _attackOnCooldown = false;

    // Private variables
    private CharacterController _characterController;
    private GameManager _gameManager;

    private float _lastPositionUpdateTime = 0f;
    private const float POSITION_UPDATE_INTERVAL = 0.1f; // 10 updates per second

    [Header("Mobile Input State")]
    private Vector2 _mobileMoveVector = Vector2.zero;
    private bool _attackPressed = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Atualiza o nome do objeto localmente para facilitar a depuração
        // O valor será definido pelo servidor
        playerNumber.OnValueChanged += (oldVal, newVal) => {
            gameObject.name = (this is PlayerMovement ? "Runner_P" : "Catcher_P") + newVal;
        };

        // Se o valor já estiver definido ao spawnar (para quem entra depois)
        if (playerNumber.Value > 0)
        {
            gameObject.name = (this is PlayerMovement ? "Runner_P" : "Catcher_P") + playerNumber.Value;
        }

        // Initialize CharacterController and network position/rotation
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            Debug.LogError("CharacterController not found on Guard GameObject. Please add one!");
        }

        // Inicializa o cooldown
        if (IsOwner)
        {
            _localAttackCooldown = AttackCooldownRemaining.Value;
            _attackOnCooldown = AttackCooldownRemaining.Value > 0f;

            if (IsOwner)
            {
                // Força o setup dos botões mobile a rodar novamente para este novo objeto
                var mobileUI = FindFirstObjectByType<MobileButtonsSetup>();
                if (mobileUI != null) mobileUI.FindAndSetupButtons();

                // Se tiver o SplitScreenManager, aproveite para dar refresh na câmera aqui também
                var ssm = FindFirstObjectByType<SplitScreenManager>();
                if (ssm != null) ssm.RefreshCameras();
            }

            // Notifica o UIManager
            UIManager uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager != null)
            {
                uiManager.SetLocalCatcher(this);
            }
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
        AttackCooldownRemaining.OnValueChanged += OnAttackCooldownChanged;
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        AttackCooldownRemaining.OnValueChanged -= OnAttackCooldownChanged;
    }

    private void OnAttackCooldownChanged(float oldValue, float newValue)
    {
        // Atualiza o cooldown local quando a NetworkVariable mudar
        if (IsOwner && Mathf.Abs(_localAttackCooldown - newValue) > 0.1f)
        {
            _localAttackCooldown = newValue;
            _attackOnCooldown = newValue > 0f;
        }
    }

    // MÉTODOS PÚBLICOS PARA CONEXÃO DA UI MÓVEL
    public void SetMoveVector(Vector2 direction) => _mobileMoveVector = direction;
    public void OnAttackButtonClicked() => _attackPressed = true;

    void Update()
    {
        if (IsOwner)
        {
            // Atualiza o cooldown local
            if (_localAttackCooldown > 0f)
            {
                _localAttackCooldown -= Time.deltaTime;
                if (_localAttackCooldown <= 0f)
                {
                    _localAttackCooldown = 0f;
                    _attackOnCooldown = false;
                }

                // Se for Host/Server, atualiza a NetworkVariable
                if (IsServer)
                {
                    AttackCooldownRemaining.Value = _localAttackCooldown;
                }
                else
                {
                    // Client apenas: Sincroniza com server periodicamente
                    SyncAttackCooldownServerRpc(_localAttackCooldown);
                }
            }

            // Se estiver balançando, não lida com movimento
            if (!_isSwinging)
            {
                HandleMovement();
            }
            HandleAttackInput();
            /*
            // Sincroniza a posição do proprietário para o servidor em intervalos regulares
            if (Time.time > _lastPositionUpdateTime + POSITION_UPDATE_INTERVAL)
            {
                SubmitPositionServerRpc(transform.position, transform.rotation);
                _lastPositionUpdateTime = Time.time;
            }
            */
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

    [ServerRpc]
    private void SyncAttackCooldownServerRpc(float clientCooldown)
    {
        // Server recebe o cooldown do client e atualiza a NetworkVariable
        if (clientCooldown < AttackCooldownRemaining.Value)
        {
            AttackCooldownRemaining.Value = clientCooldown;
        }
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

    public void HandleMovement()
    {
        {
            // Pega os inputs do jogador (PC e Mobile)
            float horizontalInput = 0f;
            float verticalInput = 0f;

            // Tenta obter o input do Mobile Input State primeiro
            bool isMobileInput = _mobileMoveVector.magnitude > 0.1f;

            if (isMobileInput)
            {
                horizontalInput = _mobileMoveVector.x;
                verticalInput = _mobileMoveVector.y;
            }
            else
            {
                horizontalInput = Input.GetAxis("Horizontal");
                verticalInput = Input.GetAxis("Vertical");
            }

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
    }

    [ClientRpc]
    public void TeleportPlayerClientRpc(Vector3 newPosition)
    {
        CharacterController cc = GetComponent<CharacterController>();

        // PASSO 1: Desativar o CharacterController é OBRIGATÓRIO para teleportar no Unity
        if (cc != null) cc.enabled = false;

        // PASSO 2: Mover o transform
        transform.position = newPosition;

        // PASSO 3: Reativar o controller
        if (cc != null) cc.enabled = true;

        // PASSO 4: IMPORTANTE: Forçar a câmera a atualizar IMEDIATAMENTE
        // Para o jogador local, precisamos atualizar a câmera associada
        if (IsOwner)
        {
            // Encontra todas as câmeras que podem estar seguindo este jogador
            CameraFollow[] cameraFollows = FindObjectsByType<CameraFollow>(FindObjectsSortMode.None);
            foreach (CameraFollow cameraFollow in cameraFollows)
            {
                // Verifica se esta câmera está seguindo este jogador
                if (cameraFollow.Target == transform)
                {
                    // Força a câmera a atualizar sua posição imediatamente
                    cameraFollow.ForcePosition();
                }
            }

            // Também verifica o SplitScreenManager
            SplitScreenManager splitManager = FindFirstObjectByType<SplitScreenManager>();
            if (splitManager != null)
            {
                splitManager.ResetCameraForPlayer(this);
            }
        }
    }

    // =======================================================
    // SERVICOS RPC
    // =======================================================

    [ServerRpc]
    private void AttackServerRpc()
    {
        // Apenas o servidor deve executar a lógica do ataque
        if (!IsServer) return;

        // Verifica se o ataque está disponível
        if (AttackCooldownRemaining.Value > 0f)
        {
            return; // Ainda em cooldown
        }

        // Inicia o cooldown
        _localAttackCooldown = attackCooldown;
        AttackCooldownRemaining.Value = attackCooldown;

        // Sincroniza o cooldown com todos os clients
        SetAttackCooldownClientRpc(attackCooldown);

        // Atualiza o tempo do último ataque no servidor
        _lastAttackTime = Time.time;

        // Inicia a corrotina de animação do balanço no servidor
        StartCoroutine(SwingAnimationCoroutine(swingDuration));

        // Executa o ataque
        PerformAttack();

        // Sincroniza a animação com todos os clients
        AnimateAttackClientRpc();
    }

    [ClientRpc]
    private void SetAttackCooldownClientRpc(float cooldownValue)
    {
        // Todos os clients recebem este valor
        if (IsOwner)
        {
            _localAttackCooldown = cooldownValue;
            _attackOnCooldown = cooldownValue > 0f;
        }
    }

    private void HandleAttackInput()
    {
        // Ataque: Apenas se for o proprietário, não estiver balançando e o cooldown tiver passado
        if (IsOwner && !_isSwinging && AttackCooldownRemaining.Value <= 0f)
        {
            // 1. Verifica o input de ataque do PC (botão Z do teclado)
            bool pcAttackInput = Input.GetKeyDown(KeyCode.Z);

            // 2. Verifica o input de ataque do Mobile
            bool mobileAttackInput = _attackPressed;

            if (pcAttackInput || mobileAttackInput)
            {
                // Chama o RPC para que o servidor autorize e execute o ataque
                AttackServerRpc();
            }
        }
        // RESETAR O ESTADO DO BOTÃO MÓVEL APÓS A LEITURA
        _attackPressed = false;
    }
    /*
    // RPC para sincronizar a posição e rotação do guarda
    [ServerRpc(RequireOwnership = false)]
    private void SubmitPositionServerRpc(Vector3 pos, Quaternion rot)
    {
        _networkPosition = pos;
        _networkRotation = rot;
    }
    */
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

        Quaternion midRotation = Quaternion.Euler(90, 90, 0);

        Quaternion endRotation = Quaternion.Euler(90, -90, 0);

        // --- 1. Transição Rápida para a Posição de Ataque (Horizontal) ---
        float preSwingTime = duration * 0.1f;
        float elapsedTime = 0f;

        hitboxTransform.localPosition = idlePos;
        hitboxTransform.localRotation = startRotation;

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

        // --- 2. O giro de "meia-lua" horizontal ---
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

        // --- 3. Retorno à Posição de Descanso ---
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
            _catcherAttackScript.enabled = true;
        }
    }
}