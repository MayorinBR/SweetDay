using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Unity.Netcode;
using System.Linq;

/// <summary>
/// Controla o joystick virtual, envia o vetor de movimento para o PlayerMovement
/// ou Guard local (o jogador proprietário) e gerencia o visual da alavanca.
/// Deve ser anexado ao objeto da ALAVANCA (o handle).
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    [Header("UI References")]
    // O objeto UI que representa o fundo fixo do joystick
    public RectTransform background;
    // O objeto UI que representa a alavanca que se move
    public RectTransform handle;

    [Header("Settings")]
    // Raio máximo de movimento do handle (em unidades de pixel do Canvas)
    public float maxRadius = 100f;

    // Referências dinâmicas ao jogador local
    private PlayerMovement _localPlayerMovement;
    private Guard _localGuard;
    private bool _isPlayerFound = false;

    void Awake()
    {
        // Define handle e background se não forem definidos no Inspector
        if (handle == null)
        {
            handle = GetComponent<RectTransform>();
        }
        if (background == null)
        {
            background = transform.parent.GetComponent<RectTransform>();
        }

        // Garante que a alavanca comece no centro
        handle.anchoredPosition = Vector2.zero;

        if (background != null)
        {
            background.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        // Tenta encontrar o jogador local assim que o script é inicializado
        FindLocalPlayer();
    }

    void Update()
    {
        // Se o jogador não foi encontrado, tenta novamente a cada frame até que seja
        if (!_isPlayerFound && NetworkManager.Singleton != null && NetworkManager.Singleton.IsClient)
        {
            FindLocalPlayer();
        }
    }

    /// <summary>
    /// Busca e armazena a referência ao PlayerMovement ou Guard que pertence
    /// ao cliente local (IsOwner == true).
    /// </summary>
    private void FindLocalPlayer()
    {
        // A maneira mais simples e direta de encontrar o jogador local em Netcode
        // É importante que esta função só seja chamada APÓS o jogador ter sido spawnado na rede.

        // 1. Tenta encontrar o PlayerMovement (Runner)
        _localPlayerMovement = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)
                                .FirstOrDefault(p => p.IsOwner);

        if (_localPlayerMovement != null)
        {
            _isPlayerFound = true;
            Debug.Log("VirtualJoystick conectado ao PlayerMovement (Runner) local.");
            return;
        }

        // 2. Tenta encontrar o Guard (Catcher)
        _localGuard = FindObjectsByType<Guard>(FindObjectsSortMode.None)
                        .FirstOrDefault(g => g.IsOwner);

        if (_localGuard != null)
        {
            _isPlayerFound = true;
            Debug.Log("VirtualJoystick conectado ao Guard (Catcher) local.");
            return;
        }

        _isPlayerFound = false;
    }

    private void CheckForLocalPlayer()
    {
        // Se a referência atual é nula, tenta achar o novo player spawnado
        if (_localPlayerMovement == null && _localGuard == null)
        {
            var player = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)
                .FirstOrDefault(p => p.IsOwner);
            if (player != null) _localPlayerMovement = player;

            var guard = FindObjectsByType<Guard>(FindObjectsSortMode.None)
                .FirstOrDefault(g => g.IsOwner);
            if (guard != null) _localGuard = guard;
        }
    }

    // Chamado quando o toque/clique começa
    public void OnPointerDown(PointerEventData eventData)
    {
        // Sempre valida se ainda temos o player antes de mover
        CheckForLocalPlayer();
        OnDrag(eventData);
    }

    // Chamado enquanto o toque/clique está sendo arrastado
    public void OnDrag(PointerEventData eventData)
    {
        if (background == null || !_isPlayerFound) return;

        Vector2 localPoint;
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            background,
            eventData.position,
            eventData.pressEventCamera,
            out localPoint))
        {
            // Calcula o vetor de direção normalizado
            Vector2 direction = localPoint / maxRadius;

            // Clampa o vetor para garantir que a magnitude não exceda 1.0 (dentro do círculo)
            if (direction.magnitude > 1.0f)
            {
                direction.Normalize();
                localPoint = direction * maxRadius;
            }

            // Move o visual da alavanca (handle)
            handle.anchoredPosition = localPoint;

            // Envia o vetor de direção NORMALIZADO para o script de movimento do jogador local
            if (_localPlayerMovement != null)
            {
                _localPlayerMovement.SetMoveVector(direction);
            }
            else if (_localGuard != null)
            {
                _localGuard.SetMoveVector(direction);
            }
        }
    }

    // Chamado quando o toque/clique termina
    public void OnPointerUp(PointerEventData eventData)
    {
        // 1. Retorna a alavanca para o centro (visual)
        handle.anchoredPosition = Vector2.zero;

        // 2. Reseta o vetor de movimento para zero (lógico)
        if (_localPlayerMovement != null)
        {
            _localPlayerMovement.SetMoveVector(Vector2.zero);
        }
        else if (_localGuard != null)
        {
            _localGuard.SetMoveVector(Vector2.zero);
        }
    }
}