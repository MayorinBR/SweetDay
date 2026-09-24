using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Interactive lobby screen where up to 6 local players each claim a slot.
/// Real-time sync is handled by <see cref="LobbyStateManager"/>'s six
/// <c>NetworkVariable&lt;ulong&gt;</c> fields — every change is pushed to all
/// connected clients automatically.
/// </summary>
public class InteractiveLobbyPanel : MonoBehaviour
{
    // ====================================================================
    // Inspector – Slot Buttons
    // ====================================================================

    [Header("Runner Slot Buttons  (P1–P4, assign in order)")]
    [SerializeField] private Button[] runnerButtons = new Button[4];
    [SerializeField] private TextMeshProUGUI[] runnerTexts = new TextMeshProUGUI[4];

    [Header("Catcher Slot Buttons  (P5–P6, assign in order)")]
    [SerializeField] private Button[] catcherButtons = new Button[2];
    [SerializeField] private TextMeshProUGUI[] catcherTexts = new TextMeshProUGUI[2];

    // ====================================================================
    // Inspector – Level Selection
    // ====================================================================

    [Header("Level Selection")]
    [SerializeField] private TextMeshProUGUI levelNameText;
    [SerializeField] private Button prevLevelButton;
    [SerializeField] private Button nextLevelButton;

    // ====================================================================
    // Inspector – Action Buttons
    // ====================================================================

    [Header("Action Buttons")]
    [SerializeField] private Button startButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private Button quitButton;

    // ====================================================================
    // Inspector – Info Labels
    // ====================================================================

    [Header("Info Labels")]
    [SerializeField] private TextMeshProUGUI lobbyCodeText;
    [SerializeField] private TextMeshProUGUI playerCountText;

    // ====================================================================
    // Colors
    // ====================================================================

    /// <summary>Slot is empty and available.</summary>
    private static readonly Color ColorEmpty = Color.white;

    /// <summary>Slot is claimed by another machine or an unidentified local player.</summary>
    private static readonly Color ColorOther = new Color(0.55f, 0.55f, 0.55f);

    /// <summary>
    /// One distinct color per local player index (0–5).
    /// Used for both hover and claim states so each player is always identifiable.
    /// </summary>
    private static readonly Color[] PlayerColors =
    {
        new Color(0.30f, 0.85f, 0.30f),  // P1 – green
        new Color(0.30f, 0.55f, 1.00f),  // P2 – blue
        new Color(1.00f, 0.55f, 0.10f),  // P3 – orange
        new Color(0.85f, 0.30f, 0.85f),  // P4 – purple
        new Color(1.00f, 0.20f, 0.20f),  // P5 – red
        new Color(0.15f, 0.85f, 0.85f),  // P6 – cyan
    };

    private static Color GetPlayerColor(int localIndex)
        => PlayerColors[Mathf.Clamp(localIndex, 0, PlayerColors.Length - 1)];

    // ====================================================================
    // Private State
    // ====================================================================

    private LobbyStateManager _state;

    /// <summary>
    /// Per-localPlayerIndex: which slot index their gamepad cursor is currently on.
    /// localPlayerIndex 0 (keyboard) uses mouse clicks, so it has no hover state.
    /// </summary>
    private readonly Dictionary<int, int> _hoveredSlot = new Dictionary<int, int>();

    // ====================================================================
    // Unity Lifecycle
    // ====================================================================

    private void OnEnable()
    {
        _state = LobbyStateManager.Instance;

        if (_state != null)
            Subscribe();
        else
            StartCoroutine(WaitForStateManager());

        WireActionButtons();
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        _state?.RemoveChangeListeners(OnSlotChanged, OnLevelChanged);
    }

    private void Update()
    {
        PollGamepadNavigation();
    }

    // ====================================================================
    // Private – Subscription
    // ====================================================================

    private System.Collections.IEnumerator WaitForStateManager()
    {
        while (LobbyStateManager.Instance == null)
            yield return null;
        _state = LobbyStateManager.Instance;
        Subscribe();
    }

    private void Subscribe()
    {
        _state.AddChangeListeners(OnSlotChanged, OnLevelChanged);
        WireSlotButtons();
        RefreshAll();
    }

    // ====================================================================
    // Private – NetworkVariable Callbacks
    // ====================================================================

    private void OnSlotChanged(ulong previous, ulong current) => RefreshAll();
    private void OnLevelChanged(int previous, int current)
    {
        if (levelNameText != null && _state != null)
            levelNameText.text = _state.SelectedLevel;
    }

    // ====================================================================
    // Private – Button Wiring
    // ====================================================================

    private void WireSlotButtons()
    {
        var noNav = new Navigation { mode = Navigation.Mode.None };

        for (int i = 0; i < 4; i++)
        {
            if (runnerButtons[i] == null) continue;
            int capture = i;
            runnerButtons[i].navigation = noNav;
            runnerButtons[i].onClick.RemoveAllListeners();
            runnerButtons[i].onClick.AddListener(() => OnSlotClickedByMouse(capture));
        }

        for (int i = 0; i < 2; i++)
        {
            if (catcherButtons[i] == null) continue;
            int capture = 4 + i;
            catcherButtons[i].navigation = noNav;
            catcherButtons[i].onClick.RemoveAllListeners();
            catcherButtons[i].onClick.AddListener(() => OnSlotClickedByMouse(capture));
        }

        if (prevLevelButton != null)
        {
            prevLevelButton.onClick.RemoveAllListeners();
            prevLevelButton.onClick.AddListener(() => ChangeLevel(-1));
        }
        if (nextLevelButton != null)
        {
            nextLevelButton.onClick.RemoveAllListeners();
            nextLevelButton.onClick.AddListener(() => ChangeLevel(+1));
        }
    }

    private void WireActionButtons()
    {
        if (startButton != null) { startButton.onClick.RemoveAllListeners(); startButton.onClick.AddListener(OnStartClicked); }
        if (disconnectButton != null) { disconnectButton.onClick.RemoveAllListeners(); disconnectButton.onClick.AddListener(OnDisconnectClicked); }
        if (quitButton != null) { quitButton.onClick.RemoveAllListeners(); quitButton.onClick.AddListener(OnQuitClicked); }
    }

    // ====================================================================
    // Private – Gamepad Per-Controller Navigation
    // ====================================================================

    /// <summary>
    /// Reads gamepad input every frame for each registered local player.
    /// localPlayerIndex 0 (keyboard) is handled by Unity's EventSystem
    /// (clicking buttons with the mouse), so we skip it here.
    /// </summary>
    private void PollGamepadNavigation()
    {
        if (LocalPlayerManager.Instance == null || _state == null) return;

        bool needsRefresh = false;

        foreach (var (localIndex, device) in LocalPlayerManager.Instance.GetAll())
        {
            // Skip keyboard player — they use mouse clicks on the buttons.
            if (device is not Gamepad gp) continue;

            // Ensure this controller has a hover position.
            if (!_hoveredSlot.ContainsKey(localIndex))
            {
                // Start on the first unclaimed slot, or slot 0.
                _hoveredSlot[localIndex] = FindFirstUnclaimed();
            }

            // If this controller already owns a slot, lock the hover cursor
            // to that slot. Navigation is disabled until the slot is released
            // by pressing South again on the same slot (toggle).
            ulong localClientId = NetworkManager.Singleton.LocalClientId;
            int claimedSlot = _state.GetSlotIndexForOwner(localClientId, localIndex);
            bool hasClaimed = claimedSlot >= 0;

            if (hasClaimed)
            {
                if (_hoveredSlot[localIndex] != claimedSlot)
                {
                    _hoveredSlot[localIndex] = claimedSlot;
                    needsRefresh = true;
                }
            }

            int hover = _hoveredSlot[localIndex];
            int newHover = hover;

            // ── Navigate (blocked while a slot is claimed) ───────────────
            if (!hasClaimed)
            {
                bool left = gp.dpad.left.wasPressedThisFrame
                          || StickLeft(gp.leftStick.ReadValue());
                bool right = gp.dpad.right.wasPressedThisFrame
                          || StickRight(gp.leftStick.ReadValue());
                bool up = gp.dpad.up.wasPressedThisFrame
                          || StickUp(gp.leftStick.ReadValue());
                bool down = gp.dpad.down.wasPressedThisFrame
                          || StickDown(gp.leftStick.ReadValue());

                if (right) newHover = Wrap(hover + 1);
                else if (left) newHover = Wrap(hover - 1);
                else if (down) newHover = Wrap(hover + 2);
                else if (up) newHover = Wrap(hover - 2);

                if (newHover != hover)
                {
                    _hoveredSlot[localIndex] = newHover;
                    needsRefresh = true;
                }
            }

            // ── Confirm / Release (South / A / Cross) ────────────────────
            // If the player has no slot: claim the hovered slot.
            // If the player already owns the hovered slot: release it (toggle).
            if (gp.buttonSouth.wasPressedThisFrame)
                ClaimSlot(_hoveredSlot[localIndex], localIndex);
        }

        if (needsRefresh) RefreshSlots();
    }

    // ====================================================================
    // Private – Slot Interaction
    // ====================================================================

    /// <summary>
    /// Called when the mouse clicks a slot button.
    /// Identifies the keyboard/mouse player's local index.
    /// </summary>
    private void OnSlotClickedByMouse(int slotIndex)
    {
        if (_state == null || NetworkManager.Singleton == null) return;
        int localIndex = GetKeyboardPlayerIndex();
        ClaimSlot(slotIndex, localIndex);
    }

    /// <summary>
    /// Sends a <see cref="LobbyStateManager.ClaimSlotServerRpc"/> for
    /// (<paramref name="slotIndex"/>, <paramref name="localPlayerIndex"/>).
    /// Toggle logic (claim / release) is handled server-side.
    /// </summary>
    private void ClaimSlot(int slotIndex, int localPlayerIndex)
    {
        if (_state == null) return;
        _state.ClaimSlotServerRpc(slotIndex, localPlayerIndex);
    }

    /// <summary>
    /// Public entry point so external systems (e.g. <see cref="UIManager"/>)
    /// can trigger a slot claim for a specific local player.
    /// </summary>
    public void ClaimSlotForLocalPlayer(int slotIndex, int localPlayerIndex)
        => ClaimSlot(slotIndex, localPlayerIndex);

    private void ChangeLevel(int direction)
    {
        if (_state == null) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;
        _state.ChangeLevelServerRpc(direction);
    }

    // ====================================================================
    // Private – Full Refresh
    // ====================================================================

    private void RefreshAll()
    {
        RefreshSlots();
        RefreshInfoLabels();
        RefreshActionButtons();
        if (levelNameText != null && _state != null)
            levelNameText.text = _state.SelectedLevel;
    }

    /// <summary>
    /// Rebuilds the visual state of all 6 slot buttons.
    ///
    /// Priority of colors:
    /// <list type="number">
    ///   <item>Claimed by me        → <see cref="ColorMine"/> (green)</item>
    ///   <item>Claimed by another   → <see cref="ColorOther"/> (grey, non-interactable)</item>
    ///   <item>Hovered by my gamepad → <see cref="ColorHover"/> (yellow)</item>
    ///   <item>Empty                → <see cref="ColorEmpty"/> (white)</item>
    /// </list>
    /// </summary>
    private void RefreshSlots()
    {
        if (_state == null) return;

        ulong myClientId = NetworkManager.Singleton?.LocalClientId ?? ulong.MaxValue;

        // Build set of slots hovered by any of this machine's gamepads.
        var hoveredByLocal = new Dictionary<int, int>(); // slotIndex → localIndex
        foreach (var kv in _hoveredSlot)
            hoveredByLocal[kv.Value] = kv.Key;

        for (int i = 0; i < LobbyStateManager.TotalSlotCount; i++)
        {
            ulong raw = _state.GetSlotOwner(i);
            bool claimed = raw != LobbyStateManager.Unclaimed;

            bool isRunner = i < LobbyStateManager.RunnerSlotCount;
            int btnIdx = isRunner ? i : i - LobbyStateManager.RunnerSlotCount;

            Button btn = isRunner ? SafeGet(runnerButtons, btnIdx)
                                           : SafeGet(catcherButtons, btnIdx);
            TextMeshProUGUI txt = isRunner ? SafeGet(runnerTexts, btnIdx)
                                           : SafeGet(catcherTexts, btnIdx);

            if (btn == null) continue;

            if (claimed)
            {
                var (ownerClientId, _) = LobbyStateManager.DecodeOwner(raw);
                bool isMine = ownerClientId == myClientId;

                var slot = new LobbySlotState(i, true, ownerClientId);
                if (txt != null) txt.text = slot.DisplayTag;
                var (_, claimedLocalIdx) = LobbyStateManager.DecodeOwner(raw);
                SetColor(btn, isMine ? GetPlayerColor(claimedLocalIdx) : ColorOther);
                btn.interactable = isMine; // Can release own slot; cannot take others'.
            }
            else if (hoveredByLocal.ContainsKey(i))
            {
                int hoveringLocalIndex = hoveredByLocal[i];
                if (txt != null) txt.text = $"P{hoveringLocalIndex + 1}";
                SetColor(btn, GetPlayerColor(hoveringLocalIndex));
                btn.interactable = true;
            }
            else
            {
                if (txt != null) txt.text = string.Empty;
                SetColor(btn, ColorEmpty);
                btn.interactable = true;
            }
        }
    }

    private void RefreshInfoLabels()
    {
        if (lobbyCodeText != null)
        {
            string code = NetworkConnectionManager.Instance?.LobbyCode ?? string.Empty;
            lobbyCodeText.text = string.IsNullOrEmpty(code) ? "Local Session" : $"Code: {code}";
        }

        if (playerCountText != null && _state != null)
            playerCountText.text =
                $"Players: {_state.ClaimedSlotCount()} / {LobbyStateManager.TotalSlotCount}";
    }

    private void RefreshActionButtons()
    {
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost;
        if (startButton != null) startButton.gameObject.SetActive(isHost);
        if (prevLevelButton != null) prevLevelButton.interactable = isHost;
        if (nextLevelButton != null) nextLevelButton.interactable = isHost;
    }

    // ====================================================================
    // Private – Action Callbacks
    // ====================================================================

    private void OnStartClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsHost) return;
        if (_state == null) return;

        if (_state.ClaimedSlotCount() == 0)
        {
            Debug.LogWarning("[InteractiveLobbyPanel] No players in slots — cannot start.");
            return;
        }

        _state.PersistSlotAssignments();

        var status = NetworkManager.Singleton.SceneManager.LoadScene(
            _state.SelectedLevel, LoadSceneMode.Single);

        if (status != SceneEventProgressStatus.Started)
            Debug.LogError($"[InteractiveLobbyPanel] Scene load failed: {status}");
    }

    private void OnDisconnectClicked()
    {
        if (NetworkConnectionManager.Instance != null)
            NetworkConnectionManager.Instance.Disconnect(goToMainMenu: true);
        else
            SceneManager.LoadScene(NetworkConnectionManager.MenuSceneName);
    }

    private void OnQuitClicked()
    {
        Application.Quit();
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }

    // ====================================================================
    // Private – Helpers
    // ====================================================================

    private static void SetColor(Button btn, Color c)
    {
        var col = btn.colors;
        col.normalColor = c;
        col.selectedColor = c;
        btn.colors = col;
    }

    private static T SafeGet<T>(T[] arr, int i) where T : class
        => arr != null && i >= 0 && i < arr.Length ? arr[i] : null;

    /// <summary>
    /// Returns the localPlayerIndex of the keyboard/mouse registered player.
    /// Falls back to 0 if no keyboard is registered.
    /// </summary>
    private static int GetKeyboardPlayerIndex()
    {
        if (LocalPlayerManager.Instance == null) return 0;
        foreach (var (localIndex, device) in LocalPlayerManager.Instance.GetAll())
            if (device is Keyboard) return localIndex;
        return 0;
    }

    private int FindFirstUnclaimed()
    {
        if (_state == null) return 0;
        for (int i = 0; i < LobbyStateManager.TotalSlotCount; i++)
            if (_state.GetSlotOwner(i) == LobbyStateManager.Unclaimed) return i;
        return 0;
    }

    private static int Wrap(int slot)
        => ((slot % LobbyStateManager.TotalSlotCount) + LobbyStateManager.TotalSlotCount)
           % LobbyStateManager.TotalSlotCount;

    // ── Stick helpers (threshold = 0.5) ──────────────────────────────────
    private static bool StickLeft(Vector2 v) => v.x < -0.5f;
    private static bool StickRight(Vector2 v) => v.x > 0.5f;
    private static bool StickUp(Vector2 v) => v.y > 0.5f;
    private static bool StickDown(Vector2 v) => v.y < -0.5f;
}