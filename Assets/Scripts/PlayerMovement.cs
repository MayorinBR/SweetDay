using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Controls the movement of the player character in a networked environment.
/// </summary>
public class PlayerMovement : NetworkBehaviour
{
    private Vector2 _inputMove;

    // Public and Inspector-visible variables
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 720f;
    public float speedReductionPerCoin = 0.25f;

    [Header("Backpack Settings")]
    public Transform backpackTransform;
    public Vector3 initialBackpackScale = new Vector3(0.4f, 0.2f, 0.4f);
    public Vector3[] backpackScales = new Vector3[MAX_BACKPACK_LEVELS];

    [Header("Coin Interaction Settings")]
    public float collectionDuration = 0.5f;
    public float dropDistance = 2.0f;

    private float _verticalVelocity;
    [Header("Physics Settings")]
    public float gravity = -9.81f; // Valor padrão para gravidade

    [Header("Damage Settings")]
    [SerializeField] private MeshRenderer meshRenderer;

    [Header("Backpack Size Progression")]
    private const int MAX_BACKPACK_LEVELS = 20; // O número total de níveis/tamanhos (de 0 a 20)
    private int _scoreToWin = 20; // Valor inicial, será atualizado pelo GameManager
    private float _coinsPerLevel = 1.0f; // Quantas moedas para passar de um nível para o próximo

    // Variáveis de Dano e Invulnerabilidade
    public NetworkVariable<bool> IsInvulnerable = new NetworkVariable<bool>(false);
    public float invulnerabilityDuration = 3.0f; // 3 segundos

    // Network variables
    public NetworkVariable<int> coinsCarried = new NetworkVariable<int>(0);
    public NetworkVariable<bool> IsRunner = new NetworkVariable<bool>(true);
    public NetworkVariable<float> DashCooldownRemaining = new NetworkVariable<float>(0f);
    public NetworkVariable<int> playerNumber = new NetworkVariable<int>(0);

    // Variables for smoothing movement on remote clients
    private Vector3 _networkPosition;
    private Quaternion _networkRotation;
    public float networkMovementSmoothness = 5f;

    // Private variables
    private float baseMoveSpeed;
    private CharacterController _characterController;
    private Coin _currentNearbyCoin;
    private bool _isCollecting = false;

    private CoinSpawner _coinSpawner;
    private GameManager _gameManager;

    private float _lastPositionUpdateTime = 0f;
    private const float POSITION_UPDATE_INTERVAL = 0.1f; // 10 updates per second

    private bool _isDashing = false;
    private const float DASH_COOLDOWN = 5.0f; // Cooldown fixo de 5.0 segundos
    private float _localDashCooldown = 0f;
    private bool _dashOnCooldown = false;
    private const float DASH_DURATION = 0.15f; // Duração curta para o dash
    private const float DASH_DISTANCE_MULTIPLIER = 1.5f; // Aumenta a distância percorrida

    [Header("Mobile Input State")]
    private Vector2 _mobileMoveVector = Vector2.zero;
    private bool _dashPressed = false; // Corresponde ao Z (Dash) no seu mapeamento
    private bool _collectPressed = false; // Corresponde ao X (Coletar) no seu mapeamento
    private bool _dropPressed = false; // Corresponde ao C (Soltar) no seu mapeamento

    void Awake()
    {
        // Define backpack scales
        backpackScales[0] = new Vector3(0.5f, 0.2f, 0.5f);
        backpackScales[1] = new Vector3(0.55f, 0.225f, 0.55f);
        backpackScales[2] = new Vector3(0.6f, 0.25f, 0.6f);
        backpackScales[3] = new Vector3(0.65f, 0.275f, 0.65f);
        backpackScales[4] = new Vector3(0.7f, 0.3f, 0.7f);
        backpackScales[5] = new Vector3(0.75f, 0.325f, 0.75f);
        backpackScales[6] = new Vector3(0.8f, 0.35f, 0.8f);
        backpackScales[7] = new Vector3(0.85f, 0.375f, 0.85f);
        backpackScales[8] = new Vector3(0.9f, 0.4f, 0.9f);
        backpackScales[9] = new Vector3(0.95f, 0.425f, 0.95f);
        backpackScales[10] = new Vector3(1.0f, 0.45f, 1.0f);
        backpackScales[11] = new Vector3(1.05f, 0.475f, 1.05f);
        backpackScales[12] = new Vector3(1.1f, 0.5f, 1.1f);
        backpackScales[13] = new Vector3(1.15f, 0.525f, 1.15f);
        backpackScales[14] = new Vector3(1.2f, 0.55f, 1.2f);
        backpackScales[15] = new Vector3(1.25f, 0.575f, 1.25f);
        backpackScales[16] = new Vector3(1.3f, 0.6f, 1.3f);
        backpackScales[17] = new Vector3(1.35f, 0.625f, 1.35f);
        backpackScales[18] = new Vector3(1.4f, 0.65f, 1.4f);
        backpackScales[19] = new Vector3(1.45f, 0.675f, 1.45f);
        backpackScales[20] = new Vector3(1.5f, 0.7f, 1.5f);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Atualiza o nome do objeto localmente para facilitar a depuração
        // O valor será definido pelo servidor
        playerNumber.OnValueChanged += (oldVal, newVal) =>
        {
            gameObject.name = (this is PlayerMovement ? "Runner_P" : "Catcher_P") + newVal;
        };

        // Se o valor já estiver definido ao spawnar (para quem entra depois)
        if (playerNumber.Value > 0)
        {
            gameObject.name = (this is PlayerMovement ? "Runner_P" : "Catcher_P") + playerNumber.Value;
        }

        // Initialize components and variables
        baseMoveSpeed = moveSpeed; // Armazena a velocidade base
        _characterController = GetComponent<CharacterController>();
        if (_characterController == null)
        {
            Debug.LogError("CharacterController not found on player GameObject. Please add one!");
        }

        if (IsOwner)
        {
            // Inicializa cooldown com valor da NetworkVariable
            _localDashCooldown = DashCooldownRemaining.Value;
            _dashOnCooldown = DashCooldownRemaining.Value > 0f;

            // Força o setup dos botões mobile a rodar novamente para este novo objeto
            var mobileUI = FindFirstObjectByType<MobileButtonsSetup>();
            if (mobileUI != null) mobileUI.FindAndSetupButtons();

            var ssm = FindFirstObjectByType<SplitScreenManager>();
            if (ssm != null)
            {
                ssm.RefreshCameras();
            }

            // Encontra o UIManager na cena
            UIManager uiManager = FindFirstObjectByType<UIManager>();
            if (uiManager != null)
            {
                // Passa a referência desta instância (o jogador local)
                uiManager.SetLocalPlayerMovement(this);
            }
        }

        // Find CoinSpawner and GameManager in the scene
        _coinSpawner = FindFirstObjectByType<CoinSpawner>();
        _gameManager = FindFirstObjectByType<GameManager>();

        // Subscribe to the network variable change event
        coinsCarried.OnValueChanged += OnCoinsCarriedChanged;

        // Subscreve também para mudanças no DashCooldownRemaining
        DashCooldownRemaining.OnValueChanged += OnDashCooldownChanged;

        // Ensure the initial state is correct for all clients
        UpdateBackpackModel();
        UpdateMoveSpeed();
    }

    private void OnDashCooldownChanged(float oldValue, float newValue)
    {
        // Atualiza o cooldown local quando a NetworkVariable mudar
        if (IsOwner && Mathf.Abs(_localDashCooldown - newValue) > 0.1f)
        {
            _localDashCooldown = newValue;
            _dashOnCooldown = newValue > 0f;
        }
    }

    public void OnMove(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        _inputMove = context.ReadValue<Vector2>();
    }

    private void Start()
    {
        _gameManager = FindFirstObjectByType<GameManager>();

        // O GameManager estará disponível no Server e Host. O valor do scoreToWin
        // deve ser copiado do GameManager para este script.
        if (_gameManager != null)
        {
            _scoreToWin = _gameManager.scoreToWin;
            CalculateProgressionFactor();
        }

        // Inicializa o cooldown local com o valor da NetworkVariable
        if (IsOwner && DashCooldownRemaining.Value > 0f)
        {
            _localDashCooldown = DashCooldownRemaining.Value;
            _dashOnCooldown = true;
        }
    }

    // MÉTODOS PÚBLICOS PARA CONEXÃO DA UI MÓVEL
    public void SetMoveVector(Vector2 direction) => _mobileMoveVector = direction;
    public void OnDashButtonClicked() => _dashPressed = true; // Botão Z
    public void OnCollectButtonClicked() => _collectPressed = true; // Botão X
    public void OnDropButtonClicked() => _dropPressed = true; // Botão C

    void Update()
    {
        // PASSO 1: O Servidor é o ÚNICO que diminui o tempo da NetworkVariable
        if (IsServer)
        {
            if (DashCooldownRemaining.Value > 0f)
            {
                DashCooldownRemaining.Value -= Time.deltaTime;
                if (DashCooldownRemaining.Value < 0f) DashCooldownRemaining.Value = 0f;
            }
        }

        if (!IsOwner) return;

        // PASSO 2: O Cliente apenas lê o valor para a lógica visual (coleta, movimento, etc.)
        if (_isCollecting)
        {
            ApplyGravity();
            return;
        }

        //if (_gameManager != null && !_gameManager.gameStarted.Value) return;
        /*
        // Sincronização de posição (RPC)
        if (Time.time - _lastPositionUpdateTime > POSITION_UPDATE_INTERVAL)
        {
            SubmitPositionServerRpc(transform.position, transform.rotation);
            _lastPositionUpdateTime = Time.time;
        }
        */
        ApplyGravity();
        HandlePlayerInput();
    }

    /// <summary>
    /// Aplica a gravidade ao CharacterController.
    /// </summary>
    private void ApplyGravity()
    {
        if (_characterController == null) return;

        // Se o personagem está no chão, garante que a velocidade vertical seja negativa (para forçar a colisão com inclinações)
        if (_characterController.isGrounded)
        {
            _verticalVelocity = -2f; // Pequeno valor negativo para manter o personagem "colado" ao chão
        }
        else
        {
            // Aplica a gravidade continuamente
            _verticalVelocity += gravity * Time.deltaTime;
        }

        // Move o CharacterController verticalmente
        _characterController.Move(new Vector3(0, _verticalVelocity, 0) * Time.deltaTime);
    }

    private void CalculateProgressionFactor()
    {
        // A mochila deve atingir o tamanho máximo (Nível 20) quando o jogador atingir o scoreToWin.
        // Portanto, o número de moedas por nível é (Score Total / Níveis Totais).
        // Ex: Se scoreToWin = 20, _coinsPerLevel = 20 / 20 = 1.0 (Aumenta a cada moeda)
        // Ex: Se scoreToWin = 40, _coinsPerLevel = 40 / 20 = 2.0 (Aumenta a cada 2 moedas)
        _coinsPerLevel = (float)_scoreToWin / MAX_BACKPACK_LEVELS;

        // Garante um mínimo para evitar divisão por zero ou lógica quebrada.
        if (_coinsPerLevel < 1.0f)
        {
            _coinsPerLevel = 1.0f;
        }
    }

    /// <summary>
    /// Captures player input and moves the CharacterController.
    /// </summary>
    public void HandlePlayerInput()
    {
        // Apenas o Owner deve processar input
        if (!IsOwner || !IsRunner.Value) return;

        if (_characterController == null) return;

        float horizontalInput = _inputMove.x;
        float verticalInput = _inputMove.y;

        // Tenta obter o input do Mobile Input State primeiro
        bool isMobileInput = _mobileMoveVector.magnitude > 0.1f;

        if (isMobileInput)
        {
            horizontalInput = _mobileMoveVector.x;
            verticalInput = _mobileMoveVector.y;
        }
        else
        {
            // Fallback: Usa o input do PC
            //horizontalInput = Input.GetAxis("Horizontal");
            //verticalInput = Input.GetAxis("Vertical");
            horizontalInput = _inputMove.x;
            verticalInput = _inputMove.y;
        }

        // Calcule o movimento horizontal (X e Z)
        Vector3 movement = new Vector3(horizontalInput, 0f, verticalInput);
        if (movement.magnitude > 1f)
        {
            movement.Normalize();
        }

        // Usa a velocidade atualizada (que pode ter sido modificada pelas moedas)
        _characterController.Move(movement * moveSpeed * Time.deltaTime);

        // Rotation logic based on movement
        if (movement != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }

        // Input logic for collecting a coin ('X' PC or Mobile Button)
        bool collectInput = _collectPressed;
        if (collectInput)
        {
            if (_currentNearbyCoin != null && _currentNearbyCoin.IsSpawned)
            {
                CollectCoinServerRpc(_currentNearbyCoin.GetComponent<NetworkObject>());
            }
            else
            {
                //Debug.Log("No coin nearby or active to collect.");
                _currentNearbyCoin = null;
            }
        }

        // Input logic for dropping a coin ('C' PC or Mobile Button)
        bool dropInput = _dropPressed;

        // 2. Verifica se há moedas e se o jogador não está em processo de coleta
        if (dropInput)
        {
            if (coinsCarried.Value > 0)
            {
                DropCoinServerRpc();
            }
            else
            {
                //Debug.Log("You have no coins to drop.");
            }
        }

        // 2. INPUT DE DASH (PC: Z, Mobile: Botão Z)
        bool dashInput = _dashPressed;
        //bool mobileDashInput = _dashPressed;

        // VERIFICA O COOLDOWN DA NETWORKVARIABLE (em vez do local)
        if ((dashInput) && IsRunner.Value &&
            DashCooldownRemaining.Value <= 0f && !_isDashing && !_isCollecting)
        {
            _dashOnCooldown = true;
            _localDashCooldown = DASH_COOLDOWN;

            Vector3 dashDirection = movement.magnitude > 0.1f ? movement : transform.forward;
            DashServerRpc(dashDirection);
        }

        // 3. RESETAR O ESTADO DOS BOTÕES MÓVEIS APÓS A LEITURA
        _dashPressed = false;
        _collectPressed = false;
        _dropPressed = false;
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

    /*
    // RPC to synchronize player position and rotation
    [ServerRpc(RequireOwnership = false)]
    private void SubmitPositionServerRpc(Vector3 pos, Quaternion rot)
    {
        _networkPosition = pos;
        _networkRotation = rot;
    }
    */
    // RPC that runs on the server to collect a coin
    [ServerRpc]
    private void CollectCoinServerRpc(NetworkObjectReference coinObjectReference)
    {
        if (coinsCarried.Value < _scoreToWin)
        {
            StartCoroutine(CollectCoinCoroutine(coinObjectReference));
        }
        else
        {
            //Debug.Log("Backpack is full!");
        }
    }

    // Coroutine to handle the collection process with a delay on the server
    IEnumerator CollectCoinCoroutine(NetworkObjectReference coinObjectReference)
    {
        _isCollecting = true;
        yield return new WaitForSeconds(collectionDuration);

        // Get the NetworkObject and Coin script
        coinObjectReference.TryGet(out NetworkObject coinNetworkObject);
        if (coinNetworkObject != null && coinNetworkObject.IsSpawned)
        {
            Coin coinToCollect = coinNetworkObject.GetComponent<Coin>();
            if (coinToCollect != null)
            {
                _gameManager.AddScoreServerRpc(coinToCollect.scoreValue);
                coinsCarried.Value++;
                coinToCollect.GetComponent<NetworkObject>().Despawn();
            }
        }
        _isCollecting = false;
    }

    // RPC to drop a coin
    [ServerRpc]
    private void DropCoinServerRpc()
    {
        if (coinsCarried.Value > 0)
        {
            _gameManager.AddScoreServerRpc(-1);
            coinsCarried.Value--;

            Vector3 dropPosition = transform.position - transform.forward * dropDistance;
            _coinSpawner.SpawnSingleCoinServerRpc(dropPosition, Quaternion.identity);
        }
    }

    /// <summary>
    /// This method is called automatically when `coinsCarried.Value` changes.
    /// It's the ideal place to update the UI, backpack model, and player speed.
    /// </summary>
    private void OnCoinsCarriedChanged(int previousValue, int newValue)
    {
        UpdateBackpackModel();
        UpdateMoveSpeed();
    }

    public int ReceiveHitAndDropCoins_Server(CoinSpawner coinSpawner)
    {
        if (!IsServer) return 0;

        if (IsInvulnerable.Value) return 0;

        // 1. Soltar todas as moedas
        int coinsToDrop = coinsCarried.Value;

        coinsCarried.Value = 0;
        UpdateMoveSpeed();

        if (coinsToDrop > 0 && coinSpawner != null)
        {
            // Usar a posição atual deste jogador específico
            coinSpawner.SpawnDroppedCoins_Server(transform.position, coinsToDrop, dropDistance);
        }

        // 2. Iniciar Invulnerabilidade apenas para ESTE jogador
        IsInvulnerable.Value = true;
        StartCoroutine(InvulnerabilityTimerCoroutine());

        // Chamar para todos os clientes (cada um filtra internamente)
        StartInvulnerabilityVisualsForAllClientRpc();

        // 3. Retorna o valor de moedas perdidas
        return coinsToDrop;
    }

    [ClientRpc]
    private void StartInvulnerabilityVisualsForAllClientRpc()
    {
        StartCoroutine(FlashVisualsCoroutine(invulnerabilityDuration));
    }

    // Corrotina para o timer de invulnerabilidade no Servidor
    private IEnumerator InvulnerabilityTimerCoroutine()
    {
        yield return new WaitForSeconds(invulnerabilityDuration);
        IsInvulnerable.Value = false;
    }

    /// <summary>
    /// Retorna o tempo restante de cooldown (local) para a UI.
    /// </summary>
    public float GetLocalDashCooldown()
    {
        return _localDashCooldown;
    }

    // ===================================================================
    // Lógica de Dash
    // ===================================================================

    /// <summary>
    /// Chamado pelo Owner para iniciar o Dash no Servidor.
    /// </summary>
    [ServerRpc]
    private void DashServerRpc(Vector3 dashDirection)
    {
        // Apenas o Server executa. Checa se não está em dash, é um runner e o cooldown terminou.
        if (!_isDashing && IsRunner.Value && DashCooldownRemaining.Value <= 0f)
        {
            // ATUALIZA O COOLDOWN NO SERVER PRIMEIRO
            _dashOnCooldown = true;
            _localDashCooldown = DASH_COOLDOWN;
            DashCooldownRemaining.Value = DASH_COOLDOWN;

            // Sincroniza o Dash visualmente com todos os Clients
            DashClientRpc(dashDirection, DASH_COOLDOWN);
        }
    }

    [ClientRpc]
    private void DashClientRpc(Vector3 dashDirection, float cooldownValue)
    {
        if (IsOwner)
        {
            _localDashCooldown = cooldownValue;
            _dashOnCooldown = cooldownValue > 0f;
        }

        StartCoroutine(DashCoroutine(dashDirection));
    }

    /// <summary>
    /// Executa o movimento de Dash no Servidor.
    /// </summary>
    IEnumerator DashCoroutine(Vector3 direction)
    {
        _isDashing = true;

        // Calcula a distância do dash (proporcional à velocidade atual)
        // moveSpeed é a velocidade atual, que já é reduzida pelas moedas.
        float dashDistance = moveSpeed * DASH_DISTANCE_MULTIPLIER;
        float dashSpeed = dashDistance / DASH_DURATION;

        float startTime = Time.time;
        while (Time.time < startTime + DASH_DURATION)
        {
            // O CharacterController.Move lida com as colisões de forma nativa do Unity.
            // É importante que _isDashing seja true para ignorar o HandlePlayerInput no Update.
            _characterController.Move(direction * dashSpeed * Time.deltaTime);
            yield return null; // Espera o próximo frame
        }

        _isDashing = false;
    }

    // Corrotina para o efeito de piscar/mudar de cor no Cliente
    private IEnumerator FlashVisualsCoroutine(float duration)
    {
        if (meshRenderer == null) yield break;

        float startTime = Time.time;

        while (Time.time < startTime + duration)
        {
            // Pisca o MeshRenderer (alterna visibilidade) a cada 0.1 segundo
            meshRenderer.enabled = !meshRenderer.enabled;
            yield return new WaitForSeconds(0.1f);
        }

        meshRenderer.enabled = true; // Garante que fica visível no final
    }

    /// <summary>
    /// Updates the size of the backpack model based on the number of coins carried.
    /// </summary>
    private void UpdateBackpackModel()
    {
        if (backpackTransform == null) return;

        if (coinsCarried.Value == 0)
        {
            backpackTransform.localScale = initialBackpackScale;
        }
        else
        {
            int index = coinsCarried.Value - 1;
            if (index >= 0 && index < backpackScales.Length)
            {
                backpackTransform.localScale = backpackScales[index];
            }
            else if (index >= backpackScales.Length)
            {
                backpackTransform.localScale = backpackScales[backpackScales.Length - 1];
            }
        }
    }

    /// <summary>
    /// Updates the player's movement speed based on the number of coins carried.
    /// </summary>
    public void UpdateMoveSpeed()
    {
        float newSpeed = baseMoveSpeed - (coinsCarried.Value * speedReductionPerCoin);
        moveSpeed = Mathf.Max(1f, newSpeed); // Garante que a velocidade nunca fique abaixo de 1

        //Debug.Log($"Velocidade atualizada: {moveSpeed} (Moedas: {coinsCarried.Value}, Redução: {coinsCarried.Value * speedReductionPerCoin})");
    }

    /// <summary>
    /// Detects if a coin enters the collection area.
    /// </summary>
    void OnTriggerEnter(Collider other)
    {
        if (IsOwner && other.CompareTag("Coin"))
        {
            Coin coin = other.GetComponent<Coin>();
            if (coin != null)
            {
                _currentNearbyCoin = coin;
            }
        }
    }

    /// <summary>
    /// Clears the coin reference when the player exits the collection area.
    /// </summary>
    void OnTriggerExit(Collider other)
    {
        if (IsOwner && other.CompareTag("Coin"))
        {
            if (_currentNearbyCoin != null && _currentNearbyCoin.gameObject == other.gameObject)
            {
                _currentNearbyCoin = null;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        coinsCarried.OnValueChanged -= OnCoinsCarriedChanged;
        DashCooldownRemaining.OnValueChanged -= OnDashCooldownChanged;
        UpdateBackpackScale();
    }

    /// <summary>
    /// Calcula e aplica a escala da mochila com base nas moedas carregadas
    /// e o score necessário para vencer.
    /// </summary>
    private void UpdateBackpackScale()
    {
        // 1. Calcula o nível de escala atual
        int currentCoins = coinsCarried.Value;

        // O nível atual é o número de moedas dividido pela taxa de aumento.
        // O Clamp garante que o nível esteja sempre entre 0 e MAX_BACKPACK_LEVELS.
        int currentLevel = Mathf.FloorToInt(currentCoins / _coinsPerLevel);
        currentLevel = Mathf.Clamp(currentLevel, 0, MAX_BACKPACK_LEVELS);

        // 2. Aplica a escala. Assumindo que a mochila tem 20 tamanhos no array
        // backpackScales[0] deve ser para nível 0 (0 moedas)
        // backpackScales[20] deve ser para nível 20 (max score)

        // Precisamos de um array de 21 tamanhos (Nível 0 a Nível 20)
        if (backpackScales.Length > currentLevel)
        {
            // Correto, acessa o índice
            backpackTransform.localScale = backpackScales[currentLevel];
        }
        else
        {
            Debug.LogWarning($"Backpack scale array size is incorrect. Expected at least {MAX_BACKPACK_LEVELS + 1} elements, but found {backpackScales.Length}.");
        }
    }

    // Vincula ao "Dash" no Player Input
    public void OnDash(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.performed) return;
        _dashPressed = true;
    }

    // Vincula ao "Collect" no Player Input
    public void OnCollect(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.performed) return;
        _collectPressed = true;
    }

    // Vincula ao "Drop" no Player Input
    public void OnDrop(InputAction.CallbackContext context)
    {
        if (!IsOwner || !context.performed) return;
        _dropPressed = true;
    }
}
