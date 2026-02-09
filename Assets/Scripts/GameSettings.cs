using UnityEngine;

/// <summary>
/// Classe estática para armazenar configurações globais que persistem entre cenas.
/// Como é static, os dados não são perdidos quando a cena muda.
/// </summary>
public static class GameSettings
{
    // O número de jogadores locais (1 a 4)
    private static int _localPlayerCount = 1;

    public static int LocalPlayerCount
    {
        get => _localPlayerCount;
        set => _localPlayerCount = Mathf.Clamp(value, 1, 4); // Garante limite de 1 a 4
    }

    // Você também pode armazenar o papel escolhido pelo Host aqui
    public static bool IsCatcher = false;

    // Helper para identificar se estamos em modo Split Screen
    public static bool IsSplitScreen => _localPlayerCount > 1;
}