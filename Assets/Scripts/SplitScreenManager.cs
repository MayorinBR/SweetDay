using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class SplitScreenManager : MonoBehaviour
{
    [SerializeField] private GameObject cameraPrefab;
    private List<Camera> _playerCameras = new List<Camera>();
    private List<CameraFollow> _cameraFollows = new List<CameraFollow>();

    void Start()
    {
        // Espera um pouco para garantir que o Netcode instanciou os players
        StartCoroutine(WaitAndSetup());
    }
    // SplitScreenManager.cs - Atualizar a corrotina WaitAndSetup
    IEnumerator WaitAndSetup()
    {
        CleanupCameras();
        int expectedCount = GameSettings.LocalPlayerCount;

        List<GameObject> localObjects = new List<GameObject>();
        float timeout = 5f;
        float timer = 0f;

        // 1. Aguarda todos os jogadores locais spawnarem
        while (localObjects.Count < expectedCount && timer < timeout)
        {
            localObjects.Clear();

            // Busca Runners e Guards locais
            var players = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None)
                .Where(p => p.IsSpawned && p.IsOwner).Select(p => p.gameObject);
            var guards = FindObjectsByType<Guard>(FindObjectsSortMode.None)
                .Where(g => g.IsSpawned && g.IsOwner).Select(g => g.gameObject);

            localObjects.AddRange(players);
            localObjects.AddRange(guards);

            if (localObjects.Count < expectedCount)
            {
                timer += 0.5f;
                yield return new WaitForSeconds(0.5f);
            }
        }

        // 2. ORDENAÇÃO CRUCIAL: Garante que P1 pegue o Rect 0, P2 o Rect 1, etc.
        // Ordenamos pelo nome ou por uma variável de ID se você tiver
        localObjects = localObjects
    .Where(obj => obj != null) // Filtra objetos que foram destruídos durante o processo
    .OrderBy(obj =>
    {
        if (obj == null) return 99;

        // Use GetComponent em vez de TryGetComponent dentro do OrderBy para evitar erros de referência
        var pm = obj.GetComponent<PlayerMovement>();
        if (pm != null) return pm.playerNumber.Value;

        var g = obj.GetComponent<Guard>();
        if (g != null) return g.playerNumber.Value;

        return 99;
    }).ToList();

        // 3. Inicializa o SplitScreen com a lista ordenada
        if (localObjects.Count > 0)
        {
            SetupSplitScreen(localObjects);
        }
    }

    private void SetupSplitScreen(List<GameObject> targets)
    {
        CleanupCameras();

        if (Camera.main != null && targets.Count > 1)
        {
            Camera.main.enabled = false;
        }

        for (int i = 0; i < targets.Count; i++)
        {
            GameObject camObj = Instantiate(cameraPrefab);
            Camera cam = camObj.GetComponent<Camera>();
            CameraFollow follow = camObj.GetComponent<CameraFollow>();

            // Configuração de Áudio (apenas P1 ouve o mundo)
            if (i > 0)
            {
                AudioListener listener = camObj.GetComponentInChildren<AudioListener>();
                if (listener != null) Destroy(listener);
            }

            // APLICAÇÃO DA POSIÇÃO NA TELA
            cam.rect = GetViewportRect(i, targets.Count);

            if (follow != null)
            {
                follow.Target = targets[i].transform;
                _cameraFollows.Add(follow);
            }

            camObj.name = $"PlayerCamera_P{i + 1}";
            _playerCameras.Add(cam);
        }
    }

    // Lógica exata pedida: P1 sempre no topo/esquerda, etc.
    private Rect GetViewportRect(int index, int total)
    {
        if (total <= 1) return new Rect(0, 0, 1, 1);

        if (total == 2)
        {
            // P1: Esquerda | P2: Direita
            return (index == 0) ? new Rect(0f, 0f, 0.5f, 1f) : new Rect(0.5f, 0f, 0.5f, 1f);
        }

        if (total == 3)
        {
            // P1: Cima (inteiro) | P2: Baixo-Esq | P3: Baixo-Dir
            if (index == 0) return new Rect(0f, 0.5f, 1f, 0.5f);
            if (index == 1) return new Rect(0f, 0f, 0.5f, 0.5f);
            return new Rect(0.5f, 0f, 0.5f, 0.5f);
        }

        if (total == 4)
        {
            // P1: Top-Esq | P2: Top-Dir | P3: Bot-Esq | P4: Bot-Dir
            if (index == 0) return new Rect(0f, 0.5f, 0.5f, 0.5f);
            if (index == 1) return new Rect(0.5f, 0.5f, 0.5f, 0.5f);
            if (index == 2) return new Rect(0f, 0f, 0.5f, 0.5f);
            return new Rect(0.5f, 0f, 0.5f, 0.5f);
        }

        return new Rect(0, 0, 1, 1);
    }

    /*
    private void ConfigureCameraViewport(Camera cam, int playerIndex, int totalPlayers)
    {
        // O Rect é (x, y, largura, altura)
        switch (totalPlayers)
        {
            case 2:
                // P1: Esquerda, P2: Direita
                if (playerIndex == 0) cam.rect = new Rect(0f, 0f, 0.5f, 1f);
                else cam.rect = new Rect(0.5f, 0f, 0.5f, 1f);
                break;

            case 3:
                // P1: Cima (tela toda), P2: Baixo-Esquerda, P3: Baixo-Direita
                if (playerIndex == 0) cam.rect = new Rect(0f, 0.5f, 1f, 0.5f);
                else if (playerIndex == 1) cam.rect = new Rect(0f, 0f, 0.5f, 0.5f);
                else cam.rect = new Rect(0.5f, 0f, 0.5f, 0.5f);
                break;

            case 4:
                // P1: Cima-Esq, P2: Cima-Dir, P3: Baixo-Esq, P4: Baixo-Dir
                if (playerIndex == 0) cam.rect = new Rect(0f, 0.5f, 0.5f, 0.5f);
                else if (playerIndex == 1) cam.rect = new Rect(0.5f, 0.5f, 0.5f, 0.5f);
                else if (playerIndex == 2) cam.rect = new Rect(0f, 0f, 0.5f, 0.5f);
                else cam.rect = new Rect(0.5f, 0f, 0.5f, 0.5f);
                break;

            default:
                // 1 Jogador (Tela cheia)
                cam.rect = new Rect(0f, 0f, 1f, 1f);
                break;
        }
    }
    */
    public void CleanupCameras()
    {
        StopAllCoroutines();

        foreach (var cam in _playerCameras)
        {
            if (cam != null) Destroy(cam.gameObject);
        }
        _playerCameras.Clear();
        _cameraFollows.Clear();

        // 2. Limpeza de SEGURANÇA: Procura por qualquer câmera que tenha o nome padrão na cena
        GameObject[] oldCameras = GameObject.FindGameObjectsWithTag("MainCamera");
        foreach (GameObject oldCam in oldCameras)
        {
            // Se o nome contém PlayerCamera, nós destruímos (ajuste o nome se necessário)
            if (oldCam.name.Contains("PlayerCamera"))
            {
                Destroy(oldCam);
            }
        }
    }

    public void ResetAllCameras()
    {
        Debug.Log("ResetAllCameras chamado");

        // Encontra todas as câmeras e força atualização
        CameraFollow[] allCameraFollows = FindObjectsByType<CameraFollow>(FindObjectsSortMode.None);
        foreach (CameraFollow cameraFollow in allCameraFollows)
        {
            if (cameraFollow.Target != null)
            {
                cameraFollow.ForcePosition();
            }
        }

        // Alternativa: encontrar jogadores locais e resetar suas câmeras
        PlayerMovement[] allPlayers = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
        foreach (PlayerMovement player in allPlayers)
        {
            if (player.IsOwner)
            {
                // Encontra a câmera seguindo este jogador
                CameraFollow playerCamera = FindCameraFollowing(player.transform);
                if (playerCamera != null)
                {
                    playerCamera.ForcePosition();
                }
            }
        }

        Guard[] allGuards = FindObjectsByType<Guard>(FindObjectsSortMode.None);
        foreach (Guard guard in allGuards)
        {
            if (guard.IsOwner)
            {
                CameraFollow guardCamera = FindCameraFollowing(guard.transform);
                if (guardCamera != null)
                {
                    guardCamera.ForcePosition();
                }
            }
        }
    }

    // Método auxiliar para encontrar câmera que segue um transform específico
    private CameraFollow FindCameraFollowing(Transform target)
    {
        CameraFollow[] allCameraFollows = FindObjectsByType<CameraFollow>(FindObjectsSortMode.None);
        foreach (CameraFollow cameraFollow in allCameraFollows)
        {
            if (cameraFollow.Target == target)
            {
                return cameraFollow;
            }
        }
        return null;
    }

    public void ResetCameraForPlayer(PlayerMovement player)
    {
        if (player == null || !player.IsOwner) return;

        // Encontra a câmera associada a este jogador
        CameraFollow cameraFollow = player.GetComponentInChildren<CameraFollow>();
        if (cameraFollow == null)
        {
            // Tenta encontrar no canvas ou em outra hierarquia
            cameraFollow = FindFirstObjectByType<CameraFollow>();
        }

        if (cameraFollow != null)
        {
            cameraFollow.ForcePosition();
        }
    }

    // Método similar para Guard
    public void ResetCameraForPlayer(Guard guard)
    {
        if (guard == null || !guard.IsOwner) return;

        CameraFollow cameraFollow = guard.GetComponentInChildren<CameraFollow>();
        if (cameraFollow == null)
        {
            cameraFollow = FindFirstObjectByType<CameraFollow>();
        }

        if (cameraFollow != null)
        {
            cameraFollow.ForcePosition();
        }
    }

    public void ReassignCamerasAfterReset()
    {
        Debug.Log("ReassignCamerasAfterReset chamado");

        // Encontra todas as câmeras
        CameraFollow[] allCameraFollows = FindObjectsByType<CameraFollow>(FindObjectsSortMode.None);
        Debug.Log($"Encontradas {allCameraFollows.Length} câmeras");

        foreach (CameraFollow cameraFollow in allCameraFollows)
        {
            // Tenta encontrar o jogador local
            bool foundLocalPlayer = false;

            // Procura primeiro por PlayerMovement local
            PlayerMovement[] players = FindObjectsByType<PlayerMovement>(FindObjectsSortMode.None);
            foreach (PlayerMovement player in players)
            {
                if (player.IsOwner)
                {
                    cameraFollow.Target = player.transform;
                    cameraFollow.ForcePosition();
                    foundLocalPlayer = true;
                    Debug.Log($"Câmera atribuída ao Player {player.gameObject.name}");
                    break;
                }
            }

            // Se não encontrou PlayerMovement, procura Guard
            if (!foundLocalPlayer)
            {
                Guard[] guards = FindObjectsByType<Guard>(FindObjectsSortMode.None);
                foreach (Guard guard in guards)
                {
                    if (guard.IsOwner)
                    {
                        cameraFollow.Target = guard.transform;
                        cameraFollow.ForcePosition();
                        Debug.Log($"Câmera atribuída ao Guard {guard.gameObject.name}");
                        break;
                    }
                }
            }
        }
    }

    void OnDestroy()
    {
        CleanupCameras();
    }

    public void ResetCameras()
    {
        StopAllCoroutines();
        StartCoroutine(WaitAndSetup());
    }

    // Método para atualizar câmeras se necessário (ex: jogador morre, respawna)
    public void RefreshCameras()
    {
        StopAllCoroutines();
        StartCoroutine(WaitAndSetup());
    }
}