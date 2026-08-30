using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public partial class RougeGameManager
{
    private string TowerDefenseFailureDialogue =>
        string.IsNullOrWhiteSpace(_towerDefenseFailureDialogue)
            ? TowerDefenseAutoplayCharacterName +
              "：主塔失去响应，链接正在断开。"
            : _towerDefenseFailureDialogue;
    private const string TowerDefenseFailureGlitchCharacters =
        "█▓▒░#@%&!?/\\|<>01断链失真";

    private sealed class TowerDefenseFailureShard
    {
        public Transform Transform;
        public Vector3 Velocity;
        public Vector3 Rotation;
    }

    private bool _towerDefenseFailureSequenceActive;
    private bool _towerDefenseFailureResultReady;
    private string _towerDefenseFailureDialogue = string.Empty;
    private int _towerDefenseGoldSpentTotal;
    private bool _towerDefenseScoreBossEngaged;
    private float _towerDefenseScoreBossMaximumHealth;
    private float _towerDefenseScoreBossLowestHealth;
    private string _towerDefenseFailureScoreText = string.Empty;
    private Coroutine _towerDefenseFailureRoutine;
    private Canvas _towerDefenseFailureCanvas;
    private Image _towerDefenseFailurePortrait;
    private Material _towerDefenseFailurePortraitMaterial;
    private Text _towerDefenseFailureDialogueText;
    private Text _towerDefenseFailureTitleText;
    private Text _towerDefenseFailureBreakdownText;
    private CanvasGroup _towerDefenseFailureResultGroup;
    private Vector2 _towerDefenseFailureDialogueOrigin;
    private readonly List<TowerDefenseFailureShard> _towerDefenseFailureShards =
        new List<TowerDefenseFailureShard>();
    private readonly List<LineRenderer> _towerDefenseFailureRings =
        new List<LineRenderer>();
    private Material _towerDefenseFailureShardMaterial;

    private void InitializeTowerDefenseFailurePresentation()
    {
        _towerDefenseFailureSequenceActive = false;
        _towerDefenseFailureResultReady = false;
        _towerDefenseGoldSpentTotal = 0;
        _towerDefenseScoreBossEngaged = false;
        _towerDefenseScoreBossMaximumHealth = 0f;
        _towerDefenseScoreBossLowestHealth = 0f;
        _towerDefenseFailureScoreText = string.Empty;
        _towerDefenseFailureDialogue = string.Empty;
    }

    private void DisposeTowerDefenseFailurePresentation()
    {
        if (_towerDefenseFailureRoutine != null)
            StopCoroutine(_towerDefenseFailureRoutine);
        _towerDefenseFailureRoutine = null;
        if (_towerDefenseFailureCanvas != null)
            Destroy(_towerDefenseFailureCanvas.gameObject);
        _towerDefenseFailureCanvas = null;
        if (_towerDefenseFailurePortraitMaterial != null)
            Destroy(_towerDefenseFailurePortraitMaterial);
        _towerDefenseFailurePortraitMaterial = null;
        if (_towerDefenseFailureShardMaterial != null)
            Destroy(_towerDefenseFailureShardMaterial);
        _towerDefenseFailureShardMaterial = null;
        ClearTowerDefenseFailureWorldVisuals();
        _towerDefenseFailureSequenceActive = false;
        _towerDefenseFailureDialogue = string.Empty;
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.
            GetComponent<RougeCameraFollow>();
        if (follow != null) follow.SetCinematicShake(0f);
    }

    private void RecordTowerDefenseGoldSpent(int amount)
    {
        _towerDefenseGoldSpentTotal = AddGoldWithoutOverflow(
            _towerDefenseGoldSpentTotal, Mathf.Max(0, amount));
    }

    private void RegisterTowerDefenseBossForScore(float maximumHealth)
    {
        _towerDefenseScoreBossEngaged = true;
        _towerDefenseScoreBossMaximumHealth = Mathf.Max(
            _towerDefenseScoreBossMaximumHealth, maximumHealth);
        _towerDefenseScoreBossLowestHealth = Mathf.Max(0f, maximumHealth);
    }

    private void UpdateTowerDefenseBossScoreHealth(float currentHealth)
    {
        if (!_towerDefenseScoreBossEngaged) return;
        _towerDefenseScoreBossLowestHealth = Mathf.Min(
            _towerDefenseScoreBossLowestHealth, Mathf.Max(0f, currentHealth));
    }

    private void BeginTowerDefenseFailureSequence()
    {
        if (_towerDefenseFailureSequenceActive) return;
        ResetTowerDefenseAutoplayPortraitInteraction();
        if (_towerDefenseAutoplayPortraitButton != null)
            _towerDefenseAutoplayPortraitButton.interactable = false;
        if (_towerDefenseAutoplayPortrait != null)
            _towerDefenseAutoplayPortrait.raycastTarget = false;
        _towerDefenseFailureDialogue = PickTowerDefenseAutoplayDefeatLine();
        CaptureTowerDefenseFailureScore();
        BuildTowerDefenseFailureUi();
        PrepareTowerDefenseFailureView();
        _towerDefenseFailureSequenceActive = true;
        _towerDefenseFailureResultReady = false;
        _towerDefenseFailureRoutine = StartCoroutine(
            PlayTowerDefenseFailureSequence());
    }

    private void PrepareTowerDefenseFailureView()
    {
        if (IsTiltShiftObservationActive)
            ForceTiltShiftObservationExit(CameraViewMode.Default);
        else if (_cameraViewMode != CameraViewMode.Default)
            ForceCameraViewMode(CameraViewMode.Default);

        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.
            GetComponent<RougeCameraFollow>();
        if (follow != null)
        {
            RougeCameraFollow.ViewState failureView =
                ResolveCameraViewPreset(CameraViewMode.TiltShift, follow);
            follow.CancelScriptedView();
            follow.ApplyViewState(failureView);
            follow.SetCinematicShake(0.04f);
        }
        RougeTiltShiftCamera tiltShift = ResolveTiltShiftCamera();
        if (tiltShift != null)
        {
            tiltShift.ClearWorldFocusPoint();
            tiltShift.SetEffectEnabled(false);
        }

        Canvas[] canvases = FindObjectsByType<Canvas>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < canvases.Length; i++)
        {
            Canvas canvas = canvases[i];
            if (canvas != null && canvas != _towerDefenseFailureCanvas)
                canvas.gameObject.SetActive(false);
        }
        HideF2MainTowerHealth();
    }

    private void BuildTowerDefenseFailureUi()
    {
        if (_towerDefenseFailureCanvas != null)
            Destroy(_towerDefenseFailureCanvas.gameObject);
        GameObject canvasObject = new GameObject("Failure Disconnect Canvas");
        canvasObject.transform.SetParent(transform, false);
        _towerDefenseFailureCanvas = canvasObject.AddComponent<Canvas>();
        _towerDefenseFailureCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _towerDefenseFailureCanvas.sortingOrder = 120;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        RougeTowerDefenseUiLayout.ConfigureCanvasScaler(scaler);

        _towerDefenseFailurePortrait = CreateUiImage(
            "Disconnecting " + TowerDefenseAutoplayCharacterName,
            canvasObject.transform, Color.white);
        _towerDefenseFailurePortrait.sprite = TowerDefenseAutoplayCommander
            .ResolvePortraitSprite(GetTowerDefenseAutoplayPortraitEmotion(),
                RougeAutoplayCommanderPortraitVariant.Defeat);
        _towerDefenseFailurePortrait.preserveAspect = true;
        _towerDefenseFailurePortrait.raycastTarget = false;
        RectTransform portraitRect = _towerDefenseFailurePortrait.rectTransform;
        portraitRect.anchorMin = Vector2.zero;
        portraitRect.anchorMax = Vector2.zero;
        portraitRect.pivot = Vector2.zero;
        portraitRect.anchoredPosition = new Vector2(12f, 8f);
        portraitRect.sizeDelta = new Vector2(310f, 476f);

        Shader disconnectShader = Shader.Find("Rouge/UI Disconnect");
        if (disconnectShader != null)
        {
            _towerDefenseFailurePortraitMaterial = new Material(disconnectShader)
            {
                hideFlags = HideFlags.DontSave
            };
            _towerDefenseFailurePortraitMaterial.SetFloat("_Glitch", 0f);
            _towerDefenseFailurePortraitMaterial.SetFloat("_Dissolve", 0f);
            _towerDefenseFailurePortrait.material =
                _towerDefenseFailurePortraitMaterial;
        }

        _towerDefenseFailureDialogueText = CreateUiText("Last Transmission",
            canvasObject.transform, 34, TextAnchor.LowerRight);
        _towerDefenseFailureDialogueText.supportRichText = false;
        _towerDefenseFailureDialogueText.fontStyle = FontStyle.Bold;
        _towerDefenseFailureDialogueText.color =
            new Color(0.82f, 0.97f, 1f, 1f);
        RectTransform dialogueRect = _towerDefenseFailureDialogueText.rectTransform;
        dialogueRect.anchorMin = new Vector2(1f, 0f);
        dialogueRect.anchorMax = new Vector2(1f, 0f);
        dialogueRect.pivot = new Vector2(1f, 0f);
        dialogueRect.anchoredPosition = new Vector2(-64f, 84f);
        dialogueRect.sizeDelta = new Vector2(820f, 190f);
        _towerDefenseFailureDialogueOrigin = dialogueRect.anchoredPosition;
        Shadow dialogueShadow = _towerDefenseFailureDialogueText.gameObject.
            AddComponent<Shadow>();
        dialogueShadow.effectColor = new Color(0f, 0f, 0f, 0.92f);
        dialogueShadow.effectDistance = new Vector2(3f, -3f);
        _towerDefenseFailureDialogueText.text = TowerDefenseFailureDialogue;

        Image resultShade = CreateUiImage("Failure Result Shade",
            canvasObject.transform, new Color(0.005f, 0.008f, 0.018f, 0.88f));
        StretchRect(resultShade.rectTransform, 0f, 0f, 0f, 0f);
        _towerDefenseFailureResultGroup = resultShade.gameObject.
            AddComponent<CanvasGroup>();
        _towerDefenseFailureResultGroup.alpha = 0f;
        _towerDefenseFailureResultGroup.blocksRaycasts = false;

        _towerDefenseFailureTitleText = CreateUiText("Mission Failed",
            resultShade.transform, 72, TextAnchor.MiddleCenter);
        _towerDefenseFailureTitleText.text = "任务失败";
        _towerDefenseFailureTitleText.fontStyle = FontStyle.Bold;
        _towerDefenseFailureTitleText.color = new Color(1f, 0.16f, 0.1f, 1f);
        RectTransform titleRect = _towerDefenseFailureTitleText.rectTransform;
        titleRect.anchorMin = new Vector2(0.5f, 0.5f);
        titleRect.anchorMax = new Vector2(0.5f, 0.5f);
        titleRect.pivot = new Vector2(0.5f, 0.5f);
        titleRect.anchoredPosition = new Vector2(0f, 220f);
        titleRect.sizeDelta = new Vector2(900f, 110f);

        _towerDefenseFailureBreakdownText = CreateUiText("Failure Score",
            resultShade.transform, 25, TextAnchor.UpperCenter);
        _towerDefenseFailureBreakdownText.supportRichText = true;
        _towerDefenseFailureBreakdownText.text = _towerDefenseFailureScoreText;
        _towerDefenseFailureBreakdownText.color =
            new Color(0.86f, 0.94f, 1f, 1f);
        RectTransform scoreRect = _towerDefenseFailureBreakdownText.rectTransform;
        scoreRect.anchorMin = new Vector2(0.5f, 0.5f);
        scoreRect.anchorMax = new Vector2(0.5f, 0.5f);
        scoreRect.pivot = new Vector2(0.5f, 0.5f);
        scoreRect.anchoredPosition = new Vector2(0f, -42f);
        scoreRect.sizeDelta = new Vector2(940f, 390f);

        ApplyActiveCommanderUiTheme(canvasObject);
    }

    private IEnumerator PlayTowerDefenseFailureSequence()
    {
        Vector3 epicenter = mainTower != null
            ? mainTower.transform.position
            : Vector3.zero;
        RougeCameraFollow follow = RougeCameraFollow.ResolveCamera()?.
            GetComponent<RougeCameraFollow>();
        float elapsed = 0f;
        bool detonationStarted = false;
        // Leave enough clean screen time to read the commander's last line before
        // the signal corruption and the blast begin to overtake it.
        const float detonationTime = 2f;
        const float disintegrationDuration = 2.15f;
        const float presentationDuration = 4.65f;

        while (elapsed < presentationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float tension = Mathf.Clamp01(elapsed / detonationTime);
            float disconnection = Mathf.Clamp01((elapsed - 1.35f) / 2.8f);
            UpdateTowerDefenseFailureDialogue(disconnection, elapsed);

            if (!detonationStarted && elapsed >= detonationTime)
            {
                detonationStarted = true;
                BeginTowerDefenseBaseExplosion(epicenter);
                RougeTowerDefenseMapLoader loader = RougeTowerDefenseMapLoader.Active;
                if (loader != null)
                    loader.StartCoroutine(loader.PlayFailureDisintegration(
                        epicenter, disintegrationDuration));
            }

            float portraitDissolve = detonationStarted
                ? Mathf.Clamp01((elapsed - detonationTime) /
                                disintegrationDuration)
                : 0f;
            if (_towerDefenseFailurePortraitMaterial != null)
            {
                _towerDefenseFailurePortraitMaterial.SetFloat("_Glitch",
                    Mathf.Clamp01(0.08f + disconnection * 1.15f));
                _towerDefenseFailurePortraitMaterial.SetFloat("_Dissolve",
                    portraitDissolve);
            }
            else if (_towerDefenseFailurePortrait != null)
            {
                Color color = _towerDefenseFailurePortrait.color;
                color.a = 1f - portraitDissolve;
                _towerDefenseFailurePortrait.color = color;
            }

            UpdateTowerDefenseFailureWorldVisuals(Time.unscaledDeltaTime,
                elapsed - detonationTime);
            if (follow != null)
            {
                float shake = !detonationStarted
                    ? Mathf.Lerp(0.04f, 0.22f, tension * tension)
                    : Mathf.Lerp(0.92f, 0.06f,
                        Mathf.Clamp01((elapsed - detonationTime) / 2.35f));
                follow.SetCinematicShake(shake);
            }
            yield return null;
        }

        if (_towerDefenseFailurePortrait != null)
            _towerDefenseFailurePortrait.gameObject.SetActive(false);
        if (_towerDefenseFailureDialogueText != null)
            _towerDefenseFailureDialogueText.gameObject.SetActive(false);
        HideTowerDefenseFailureSceneObjects();
        if (follow != null) follow.SetCinematicShake(0f);

        float resultFade = 0f;
        while (resultFade < 1f)
        {
            resultFade += Time.unscaledDeltaTime / 0.6f;
            if (_towerDefenseFailureResultGroup != null)
                _towerDefenseFailureResultGroup.alpha =
                    Mathf.Clamp01(resultFade);
            yield return null;
        }
        _towerDefenseFailureResultReady = true;
        _towerDefenseFailureSequenceActive = false;
        _towerDefenseFailureRoutine = null;
    }

    private void UpdateTowerDefenseFailureDialogue(float corruption,
        float elapsed)
    {
        if (_towerDefenseFailureDialogueText == null) return;
        int seed = Mathf.FloorToInt(elapsed * 24f);
        _towerDefenseFailureDialogueText.text = BuildCorruptedFailureDialogue(
            TowerDefenseFailureDialogue, corruption, seed);
        _towerDefenseFailureDialogueText.color = Color.Lerp(
            RemapCommanderInterfaceColor(
                new Color(0.82f, 0.97f, 1f, 1f)),
            new Color(1f, 0.18f, 0.07f, Mathf.Clamp01(1.35f - corruption)),
            corruption);
        RectTransform rect = _towerDefenseFailureDialogueText.rectTransform;
        float jitter = corruption * corruption * 9f;
        rect.anchoredPosition = _towerDefenseFailureDialogueOrigin +
            new Vector2(Mathf.Sin(elapsed * 73f) * jitter,
                Mathf.Cos(elapsed * 91f) * jitter * 0.55f);
    }

    private static string BuildCorruptedFailureDialogue(string source,
        float amount, int seed)
    {
        amount = Mathf.Clamp01(amount);
        var builder = new StringBuilder(source.Length + 12);
        unchecked
        {
            uint state = (uint)(seed * 747796405 + 2891336453);
            for (int i = 0; i < source.Length; i++)
            {
                char current = source[i];
                if (char.IsWhiteSpace(current) || current == '\n')
                {
                    builder.Append(current);
                    continue;
                }
                state = state * 1664525u + 1013904223u;
                float roll = (state & 0xFFFFu) / 65535f;
                if (roll < amount * 0.78f)
                {
                    int index = (int)((state >> 16) %
                        TowerDefenseFailureGlitchCharacters.Length);
                    builder.Append(TowerDefenseFailureGlitchCharacters[index]);
                }
                else builder.Append(current);
            }
        }
        return builder.ToString();
    }

    private void BeginTowerDefenseBaseExplosion(Vector3 epicenter)
    {
        if (mainTower != null)
        {
            Renderer[] renderers = mainTower.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].enabled = false;
        }
        CreateTowerDefenseFailureRings(epicenter);
        CreateTowerDefenseFailureShards(epicenter);
    }

    private void CreateTowerDefenseFailureRings(Vector3 epicenter)
    {
        for (int i = 0; i < 3; i++)
        {
            LineRenderer ring = TowerDefenseVisuals.CreateCircleRenderer(
                $"Failure Shockwave {i + 1}", transform);
            ring.widthMultiplier = 0.4f + i * 0.22f;
            ring.sharedMaterial = GetTacticalIndicatorMaterial();
            ring.sortingOrder = 32040 + i;
            TowerDefenseVisuals.UpdateCircle(ring,
                epicenter + Vector3.up * (0.1f + i * 0.12f), 0.1f,
                Color.clear, true);
            _towerDefenseFailureRings.Add(ring);
        }
    }

    private void CreateTowerDefenseFailureShards(Vector3 epicenter)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ??
                        Shader.Find("Unlit/Color") ??
                        Shader.Find("Sprites/Default");
        if (shader != null)
        {
            _towerDefenseFailureShardMaterial = new Material(shader)
            {
                hideFlags = HideFlags.DontSave
            };
            ApplyBaseColor(_towerDefenseFailureShardMaterial,
                new Color(0.05f, 0.82f, 1f, 1f));
        }

        for (int i = 0; i < 20; i++)
        {
            float angle = i * Mathf.PI * 2f / 20f +
                          Mathf.Sin(i * 4.17f) * 0.19f;
            Vector3 direction = new Vector3(Mathf.Cos(angle),
                0.25f + (i % 5) * 0.12f, Mathf.Sin(angle)).normalized;
            GameObject shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            shard.name = "Main Tower Failure Shard";
            shard.transform.position = epicenter + Vector3.up *
                                       (0.6f + (i % 4) * 0.35f);
            shard.transform.localScale = new Vector3(
                0.2f + (i % 3) * 0.12f,
                0.28f + (i % 4) * 0.12f,
                0.18f + (i % 2) * 0.16f);
            Collider collider = shard.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = shard.GetComponent<Renderer>();
            if (renderer != null && _towerDefenseFailureShardMaterial != null)
                renderer.sharedMaterial = _towerDefenseFailureShardMaterial;
            _towerDefenseFailureShards.Add(new TowerDefenseFailureShard
            {
                Transform = shard.transform,
                Velocity = direction * (9f + (i % 6) * 1.8f),
                Rotation = new Vector3(130f + i * 7f,
                    210f - i * 3f, 90f + i * 11f)
            });
        }
    }

    private void UpdateTowerDefenseFailureWorldVisuals(float dt,
        float detonationElapsed)
    {
        if (detonationElapsed < 0f) return;
        for (int i = 0; i < _towerDefenseFailureShards.Count; i++)
        {
            TowerDefenseFailureShard shard = _towerDefenseFailureShards[i];
            if (shard.Transform == null) continue;
            shard.Velocity += Vector3.down * (15f * dt);
            shard.Transform.position += shard.Velocity * dt;
            shard.Transform.Rotate(shard.Rotation * dt, Space.Self);
            float scale = Mathf.Clamp01(1f -
                Mathf.Max(0f, detonationElapsed - 0.8f) / 1.2f);
            shard.Transform.localScale *= Mathf.Lerp(1f, 0.94f,
                Mathf.Clamp01(dt * 12f));
            if (scale <= 0.01f) shard.Transform.gameObject.SetActive(false);
        }

        if (mainTower == null) return;
        Vector3 epicenter = mainTower.transform.position;
        for (int i = 0; i < _towerDefenseFailureRings.Count; i++)
        {
            LineRenderer ring = _towerDefenseFailureRings[i];
            if (ring == null) continue;
            float local = Mathf.Clamp01((detonationElapsed - i * 0.12f) /
                                        (0.8f + i * 0.14f));
            Color color = Color.Lerp(new Color(0.1f, 0.88f, 1f, 1f),
                new Color(1f, 0.12f, 0.03f, 0f), local);
            TowerDefenseVisuals.UpdateCircle(ring,
                epicenter + Vector3.up * (0.1f + i * 0.12f),
                Mathf.Lerp(0.2f, 20f + i * 18f, local), color,
                local < 1f);
        }
    }

    private void HideTowerDefenseFailureSceneObjects()
    {
        for (int i = 0; i < _defenseTowers.Count; i++)
        {
            RougeDefenseTower tower = _defenseTowers[i];
            if (tower == null) continue;
            Renderer[] renderers = tower.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
                if (renderers[r] != null) renderers[r].enabled = false;
        }
        ClearTowerDefenseFailureWorldVisuals();
    }

    private void ClearTowerDefenseFailureWorldVisuals()
    {
        for (int i = 0; i < _towerDefenseFailureShards.Count; i++)
        {
            Transform shard = _towerDefenseFailureShards[i].Transform;
            if (shard != null) Destroy(shard.gameObject);
        }
        _towerDefenseFailureShards.Clear();
        for (int i = 0; i < _towerDefenseFailureRings.Count; i++)
            if (_towerDefenseFailureRings[i] != null)
                Destroy(_towerDefenseFailureRings[i].gameObject);
        _towerDefenseFailureRings.Clear();
    }

    private void CaptureTowerDefenseFailureScore()
    {
        double totalDamage = 0d;
        if (_towerDamageTotalsFixed.IsCreated)
        {
            for (int i = 0; i < _towerDamageTotalsFixed.Length; i++)
                totalDamage += System.Math.Max(0L,
                    _towerDamageTotalsFixed[i]) / 1000d;
        }
        float bossDamage = _towerDefenseScoreBossEngaged
            ? Mathf.Max(0f, _towerDefenseScoreBossMaximumHealth -
                         _towerDefenseScoreBossLowestHealth)
            : 0f;
        float healthRatio = mainTower != null ? mainTower.HealthNormalized : 0f;
        RougeTowerDefenseMap.ScoringRules scoreRules =
            GetActiveTowerDefenseScoringRules();
        long killPoints = scoreRules.GetKillPoints(totalKills);
        long damagePoints = scoreRules.GetDamagePoints(totalDamage);
        long healthPoints = scoreRules.GetMainTowerHealthPoints(healthRatio);
        long goldPoints = scoreRules.GetRemainingGoldPoints(_towerDefenseGold);
        long score = killPoints + damagePoints + healthPoints + goldPoints;

        string bossLine = _towerDefenseScoreBossEngaged
            ? $"首领伤害  {bossDamage:0} / {_towerDefenseScoreBossMaximumHealth:0}（已计入总伤害）"
            : "首领交战  尚未接触";
        _towerDefenseFailureScoreText =
            $"<b>作战评分  {score:N0}</b>\n\n" +
            $"击杀  {totalKills:N0}  +{killPoints:N0}\n" +
            $"总伤害  {totalDamage:N0}  +{damagePoints:N0}\n" +
            $"主塔完整度  {healthRatio * 100f:0.0}%  +{healthPoints:N0}\n" +
            $"剩余金币  {_towerDefenseGold:N0}  +{goldPoints:N0}\n" +
            $"作战用时  {FormatGameTime(_survivalTime)}\n" +
            $"{bossLine}\n\n" +
            "<color=#9AC9D8>按 R 重新开始</color>";
    }
}
