using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative manager for the interactive lobby state.
/// </summary>
public class LobbyStateManager : NetworkBehaviour
{
    // ====================================================================
    // Singleton
    // ====================================================================

    /// <summary>Gets the singleton instance of <see cref="LobbyStateManager"/>.</summary>
    public static LobbyStateManager Instance { get; private set; }

    // ====================================================================
    // Constants
    // ====================================================================

    /// <summary>Sentinel value meaning "no player has claimed this slot".</summary>
    public const ulong Unclaimed = ulong.MaxValue;

    /// <summary>Number of Runner player slots (P1–P4).</summary>
    public const int RunnerSlotCount = 4;

    /// <summary>Number of Catcher player slots (P5–P6).</summary>
    public const int CatcherSlotCount = 2;

    /// <summary>Total lobby slots.</summary>
    public const int TotalSlotCount = RunnerSlotCount + CatcherSlotCount;

    /// <summary>Available game scenes shown in the level carousel.</summary>
    public static readonly string[] AvailableLevels = { "TestScene_Flat", "TestScene_Hill" };

    // ====================================================================
    // Slot Owner Encoding
    // ====================================================================

    // Encodes (clientId, localPlayerIndex) into a single ulong.
    // Layout:  [63..8] = clientId   [7..0] = localPlayerIndex (0-255)
    // ulong.MaxValue is reserved as the "Unclaimed" sentinel.

    /// <summary>
    /// Packs <paramref name="clientId"/> and <paramref name="localIndex"/> into
    /// a single <c>ulong</c> for storage in a <see cref="NetworkVariable{T}"/>.
    /// </summary>
    public static ulong EncodeOwner(ulong clientId, int localIndex)
        => (clientId << 8) | ((ulong)(uint)localIndex & 0xFFu);  // cast to uint avoids CS0675

    /// <summary>
    /// Unpacks a value previously created by <see cref="EncodeOwner"/>.
    /// </summary>
    public static (ulong clientId, int localIndex) DecodeOwner(ulong encoded)
        => (encoded >> 8, (int)(encoded & 0xFFu));

    // ====================================================================
    // Network State – Slot Owners (one per slot, avoids NetworkList CS8377)
    // ====================================================================

    /// <summary>Owner of slot 0 (Runner P1). <see cref="Unclaimed"/> when empty.</summary>
    public NetworkVariable<ulong> Slot0 = new NetworkVariable<ulong>(Unclaimed, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>Owner of slot 1 (Runner P2).</summary>
    public NetworkVariable<ulong> Slot1 = new NetworkVariable<ulong>(Unclaimed, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>Owner of slot 2 (Runner P3).</summary>
    public NetworkVariable<ulong> Slot2 = new NetworkVariable<ulong>(Unclaimed, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>Owner of slot 3 (Runner P4).</summary>
    public NetworkVariable<ulong> Slot3 = new NetworkVariable<ulong>(Unclaimed, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>Owner of slot 4 (Catcher P5).</summary>
    public NetworkVariable<ulong> Slot4 = new NetworkVariable<ulong>(Unclaimed, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    /// <summary>Owner of slot 5 (Catcher P6).</summary>
    public NetworkVariable<ulong> Slot5 = new NetworkVariable<ulong>(Unclaimed, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ====================================================================
    // Network State – Level
    // ====================================================================

    /// <summary>Index into <see cref="AvailableLevels"/> for the selected map.</summary>
    public NetworkVariable<int> SelectedLevelIndex = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // ====================================================================
    // Public Properties
    // ====================================================================

    /// <summary>Scene name of the currently selected level.</summary>
    public string SelectedLevel
        => AvailableLevels[Mathf.Clamp(SelectedLevelIndex.Value, 0, AvailableLevels.Length - 1)];

    // ====================================================================
    // Unity / NetworkBehaviour Lifecycle
    // ====================================================================

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    /// <inheritdoc/>

    /// <summary>
    /// Re-applies the slot assignments persisted in <see cref="GameSettings.SlotAssignments"/>
    /// to the <see cref="NetworkVariable{T}"/> slots so returning players keep their roles.
    /// </summary>


    public override void OnDestroy()
    {
        base.OnDestroy();
        if (Instance == this) Instance = null;
    }

    // ====================================================================
    // Public – Slot Queries
    // ====================================================================

    public ulong GetSlotOwner(int slotIndex) => GetSlotVar(slotIndex)?.Value ?? Unclaimed;

    public LobbySlotState GetSlotState(int slotIndex)
    {
        ulong raw = GetSlotOwner(slotIndex);
        if (raw == Unclaimed) return new LobbySlotState(slotIndex, false, 0);
        var (clientId, _) = DecodeOwner(raw);
        return new LobbySlotState(slotIndex, true, clientId);
    }

    public int ClaimedSlotCount()
    {
        int count = 0;
        for (int i = 0; i < TotalSlotCount; i++)
            if (GetSlotOwner(i) != Unclaimed) count++;
        return count;
    }

    public int GetSlotIndexForOwner(ulong clientId, int localPlayerIndex)
    {
        ulong encoded = EncodeOwner(clientId, localPlayerIndex);
        for (int i = 0; i < TotalSlotCount; i++)
            if (GetSlotOwner(i) == encoded) return i;
        return -1;
    }

    /// <summary>
    /// Registers <paramref name="slotCallback"/> on all 6 slot variables and
    /// <paramref name="levelCallback"/> on <see cref="SelectedLevelIndex"/>.
    /// Called by <see cref="InteractiveLobbyPanel"/> in <c>OnEnable</c>.
    /// </summary>
    public void AddChangeListeners(
        NetworkVariable<ulong>.OnValueChangedDelegate slotCallback,
        NetworkVariable<int>.OnValueChangedDelegate levelCallback)
    {
        for (int i = 0; i < TotalSlotCount; i++)
            GetSlotVar(i).OnValueChanged += slotCallback;
        SelectedLevelIndex.OnValueChanged += levelCallback;
    }

    /// <summary>Removes listeners previously added by <see cref="AddChangeListeners"/>.</summary>
    public void RemoveChangeListeners(
        NetworkVariable<ulong>.OnValueChangedDelegate slotCallback,
        NetworkVariable<int>.OnValueChangedDelegate levelCallback)
    {
        for (int i = 0; i < TotalSlotCount; i++)
            GetSlotVar(i).OnValueChanged -= slotCallback;
        SelectedLevelIndex.OnValueChanged -= levelCallback;
    }

    // ====================================================================
    // Public – Pre-Game Persistence
    // ====================================================================

    public void PersistSlotAssignments()
    {
        GameSettings.SlotAssignments.Clear();
        for (int i = 0; i < TotalSlotCount; i++)
        {
            ulong raw = GetSlotOwner(i);
            if (raw == Unclaimed) continue;
            var (clientId, localIndex) = DecodeOwner(raw);
            GameSettings.SlotAssignments.Add(new GameSettings.SlotAssignment
            {
                ClientId = clientId,
                LocalPlayerIndex = localIndex,
                SlotIndex = i,
            });
        }
        GameSettings.SelectedScene = SelectedLevel;
        Debug.Log($"[LobbyStateManager] Persisted {GameSettings.SlotAssignments.Count} assignments → '{SelectedLevel}'.");
    }

    // ====================================================================
    // Public – Disconnect Helper
    // ====================================================================

    public void ReleaseClientSlot(ulong clientId)
    {
        if (!IsServer) return;
        for (int i = 0; i < TotalSlotCount; i++)
        {
            ulong raw = GetSlotOwner(i);
            if (raw == Unclaimed) continue;
            var (owner, _) = DecodeOwner(raw);
            if (owner == clientId) GetSlotVar(i).Value = Unclaimed;
        }
    }

    // ====================================================================
    // Server RPCs
    // ====================================================================

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ClaimSlotServerRpc(int slotIndex, int localPlayerIndex, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        ulong myEncoded = EncodeOwner(clientId, localPlayerIndex);

        if (slotIndex < 0 || slotIndex >= TotalSlotCount) return;

        var targetVar = GetSlotVar(slotIndex);
        ulong current = targetVar.Value;

        if (current != Unclaimed && current != myEncoded) return;

        for (int i = 0; i < TotalSlotCount; i++)
        {
            if (i == slotIndex) continue;
            var v = GetSlotVar(i);
            if (v?.Value == myEncoded) { v.Value = Unclaimed; break; }
        }

        targetVar.Value = (current == myEncoded) ? Unclaimed : myEncoded;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReleaseSlotServerRpc(int localPlayerIndex, RpcParams rpcParams = default)
    {
        ulong encoded = EncodeOwner(rpcParams.Receive.SenderClientId, localPlayerIndex);
        for (int i = 0; i < TotalSlotCount; i++)
        {
            var v = GetSlotVar(i);
            if (v?.Value == encoded) { v.Value = Unclaimed; return; }
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ChangeLevelServerRpc(int direction, RpcParams rpcParams = default)
    {
        if (rpcParams.Receive.SenderClientId != NetworkManager.ServerClientId) return;
        SelectedLevelIndex.Value = Mathf.Clamp(
            SelectedLevelIndex.Value + direction, 0, AvailableLevels.Length - 1);
    }

    // ====================================================================
    // Private – Slot Variable Accessor
    // ====================================================================

    private NetworkVariable<ulong> GetSlotVar(int i) => i switch
    {
        0 => Slot0,
        1 => Slot1,
        2 => Slot2,
        3 => Slot3,
        4 => Slot4,
        5 => Slot5,
        _ => null
    };
}