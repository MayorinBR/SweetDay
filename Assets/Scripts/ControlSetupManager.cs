using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Users;

public class ControlSetupManager : MonoBehaviour
{
    public static ControlSetupManager Instance;

    private Dictionary<int, InputDevice> playerDevices = new Dictionary<int, InputDevice>();
    private List<PlayerInput> pendingPlayers = new List<PlayerInput>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void AssignControlScheme(GameObject player, int playerIndex)
    {
        PlayerInput pInput = player.GetComponent<PlayerInput>();
        if (pInput == null) return;

        // Pegamos todos os gamepads disponíveis
        var allGamepads = Gamepad.all.ToList();
        int gamepadCount = allGamepads.Count;
        int totalLocalPlayers = GameSettings.LocalPlayerCount;

        InputDevice deviceToPair = null;
        string scheme = "";

        // LÓGICA DE DISTRIBUIÇÃO:
        // Se o índice do jogador for menor que a diferença entre players e gamepads,
        // significa que não tem gamepad pra ele, então ele fica no teclado.
        // Ex: 4 players, 3 gamepads -> Player 0 (índice 0) < (4-3=1) -> Teclado.

        int playersWithoutGamepad = totalLocalPlayers - gamepadCount;

        if (playerIndex < playersWithoutGamepad)
        {
            // Atribui Teclado/Mouse
            deviceToPair = Keyboard.current;
            scheme = "KeyboardMouse";
            Debug.Log($"Player {playerIndex} atribuído ao TECLADO");
        }
        else
        {
            // Atribui um Gamepad
            // O índice do gamepad será o índice do player ajustado
            int gamepadIndex = playerIndex - (playersWithoutGamepad > 0 ? playersWithoutGamepad : 0);

            if (gamepadIndex < allGamepads.Count)
            {
                deviceToPair = allGamepads[gamepadIndex];
                scheme = "Gamepad";
                Debug.Log($"Player {playerIndex} atribuído ao GAMEPAD {gamepadIndex}");
            }
        }

        if (deviceToPair != null)
        {
            PairDeviceToPlayer(pInput, deviceToPair, scheme);
        }
    }

    private void PairDeviceToPlayer(PlayerInput pInput, InputDevice device, string scheme)
    {
        // Limpa pareamentos antigos para evitar conflitos
        pInput.user.UnpairDevices();

        // Emparelha o novo dispositivo
        InputUser.PerformPairingWithDevice(device, pInput.user);

        // Ativa o esquema de controle correto (deve bater com os nomes no seu Input Action Asset)
        pInput.SwitchCurrentControlScheme(scheme, device);
    }

    private System.Collections.IEnumerator DelayedAssignment(PlayerInput pInput, int playerIndex)
    {
        // Tenta por alguns frames
        int maxAttempts = 10;
        for (int i = 0; i < maxAttempts; i++)
        {
            if (pInput.user.valid)
            {
                AssignControlSchemeInternal(pInput, playerIndex);
                pendingPlayers.Remove(pInput);
                yield break;
            }
            yield return null;
        }

        Debug.LogError($"Falha ao atribuir controles para Player {playerIndex} após {maxAttempts} tentativas");

        // Fallback: força uma configuração básica
        ForceBasicAssignment(pInput, playerIndex);
    }

    private void AssignControlSchemeInternal(PlayerInput pInput, int playerIndex)
    {
        try
        {
            // Desassocia dispositivos anteriores com segurança
            if (pInput.user.valid)
            {
                pInput.user.UnpairDevices();
            }

            InputDevice assignedDevice = GetAvailableDeviceForPlayer(playerIndex);

            if (assignedDevice != null)
            {
                // Pareia o dispositivo
                InputUser.PerformPairingWithDevice(assignedDevice, pInput.user);

                // Configura o esquema de controle
                if (assignedDevice is Gamepad)
                {
                    pInput.SwitchCurrentControlScheme("Gamepad", assignedDevice);
                    Debug.Log($"✓ Player {playerIndex + 1} -> Gamepad: {assignedDevice.name}");
                }
                else if (assignedDevice is Keyboard)
                {
                    // Para teclado, inclui mouse se disponível
                    Mouse mouse = Mouse.current;
                    if (mouse != null)
                    {
                        InputUser.PerformPairingWithDevice(mouse, pInput.user);
                        pInput.SwitchCurrentControlScheme("Keyboard", Keyboard.current, mouse);
                        Debug.Log($"✓ Player {playerIndex + 1} -> Teclado + Mouse");
                    }
                    else
                    {
                        pInput.SwitchCurrentControlScheme("Keyboard", Keyboard.current);
                        Debug.Log($"✓ Player {playerIndex + 1} -> Teclado");
                    }
                }

                // Armazena o dispositivo
                playerDevices[playerIndex] = assignedDevice;
            }
            else
            {
                // NÃO É MAIS UM ERRO - apenas um aviso
                Debug.LogWarning($"⚠ Player {playerIndex + 1}: Nenhum dispositivo disponível. Pode precisar de configuração manual.");

                // Configuração mínima para permitir o jogo continuar
                ConfigureMinimalInput(pInput, playerIndex);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Erro ao configurar controles para Player {playerIndex}: {e.Message}");
            ConfigureMinimalInput(pInput, playerIndex);
        }
    }

    private void ConfigureMinimalInput(PlayerInput pInput, int playerIndex)
    {
        try
        {
            // Tenta configurar algo básico para não quebrar o jogo
            if (Keyboard.current != null)
            {
                // Se já não estiver usando o teclado, tenta configurar
                if (!playerDevices.ContainsValue(Keyboard.current))
                {
                    pInput.SwitchCurrentControlScheme("Keyboard", Keyboard.current, Mouse.current);
                    Debug.Log($"⚠ Player {playerIndex + 1} -> Usando teclado compartilhado (modo fallback)");
                }
                else
                {
                    // Se o teclado já está em uso, o jogador pode não ter controle
                    // Mas o jogo ainda pode continuar
                    Debug.Log($"⚠ Player {playerIndex + 1} -> Sem controle específico. Pode ser controlado por código.");
                }
            }
            // O jogo continua mesmo sem controle - isso é permitido agora
        }
        catch
        {
            // Ignora erros no fallback - o importante é que o jogo continue
        }
    }

    private InputDevice GetAvailableDeviceForPlayer(int playerIndex)
    {
        // Lista todos os dispositivos disponíveis
        var availableGamepads = new List<Gamepad>(Gamepad.all);
        bool keyboardAvailable = Keyboard.current != null && !playerDevices.ContainsValue(Keyboard.current);

        // Remove dispositivos já atribuídos
        foreach (var device in playerDevices.Values)
        {
            if (device is Gamepad gamepad && availableGamepads.Contains(gamepad))
            {
                availableGamepads.Remove(gamepad);
            }
        }

        // Lógica de atribuição baseada em preferências
        switch (playerIndex)
        {
            case 0: // Player 1
                // Player 1 prefere teclado
                if (keyboardAvailable) return Keyboard.current;
                if (availableGamepads.Count > 0) return availableGamepads[0];
                break;

            case 1: // Player 2
                // Player 2 prefere gamepad
                if (availableGamepads.Count > 0) return availableGamepads[0];
                // Se não houver gamepad, pode usar teclado se disponível
                if (keyboardAvailable && !playerDevices.ContainsKey(0))
                    return Keyboard.current;
                break;

            case 2: // Player 3
                // Apenas gamepads para players 3+
                if (availableGamepads.Count > 0) return availableGamepads[0];
                break;

            case 3: // Player 4
                // Apenas gamepads para players 4
                if (availableGamepads.Count > 0) return availableGamepads[0];
                break;
        }

        return null; // Nenhum dispositivo disponível
    }

    private void ForceBasicAssignment(PlayerInput pInput, int playerIndex)
    {
        // Configuração de emergência quando tudo falha
        Debug.LogWarning($"Configuração de emergência para Player {playerIndex + 1}");

        var gamepads = Gamepad.all;
        if (playerIndex < gamepads.Count)
        {
            pInput.SwitchCurrentControlScheme("Gamepad", gamepads[playerIndex]);
        }
        else if (Keyboard.current != null)
        {
            pInput.SwitchCurrentControlScheme("Keyboard", Keyboard.current, Mouse.current);
        }
        // Se não houver nenhum dispositivo, deixa sem configuração
        // O jogo ainda pode funcionar com controle alternativo
    }

    public void DebugCurrentDevices()
    {
        Debug.Log("=== DISPOSITIVOS ATUAIS ===");
        if (playerDevices.Count == 0)
        {
            Debug.Log("Nenhum dispositivo atribuído ainda.");
        }
        else
        {
            foreach (var kvp in playerDevices)
            {
                string deviceType = kvp.Value is Gamepad ? "Gamepad" :
                                   kvp.Value is Keyboard ? "Keyboard" : "Unknown";
                Debug.Log($"Player {kvp.Key + 1}: {deviceType} - {kvp.Value?.name ?? "Nenhum"}");
            }
        }

        Debug.Log($"Gamepads conectados: {Gamepad.all.Count}");
        for (int i = 0; i < Gamepad.all.Count; i++)
        {
            Debug.Log($"  Gamepad {i}: {Gamepad.all[i].name}");
        }

        Debug.Log($"Teclado disponível: {(Keyboard.current != null ? "Sim" : "Não")}");
    }

    void OnDestroy()
    {
        // Limpa todos os dispositivos
        foreach (var kvp in playerDevices)
        {
            if (kvp.Value != null)
            {
                ReleaseDevice(kvp.Value);
            }
        }
        playerDevices.Clear();
        pendingPlayers.Clear();
    }

    private void ReleaseDevice(InputDevice device)
    {
        foreach (var user in InputUser.all)
        {
            if (user.valid && user.pairedDevices.Contains(device))
            {
                user.UnpairDevice(device);
                break;
            }
        }
    }

    public void ReleasePlayerDevice(int playerIndex)
    {
        if (playerDevices.ContainsKey(playerIndex))
        {
            var device = playerDevices[playerIndex];
            playerDevices.Remove(playerIndex);
            ReleaseDevice(device);
        }
    }

    // Novo método: verifica se jogador tem controle configurado
    public bool HasControlAssigned(int playerIndex)
    {
        return playerDevices.ContainsKey(playerIndex) && playerDevices[playerIndex] != null;
    }

    // Novo método: obtém o dispositivo de um jogador
    public InputDevice GetPlayerDevice(int playerIndex)
    {
        return playerDevices.ContainsKey(playerIndex) ? playerDevices[playerIndex] : null;
    }
}