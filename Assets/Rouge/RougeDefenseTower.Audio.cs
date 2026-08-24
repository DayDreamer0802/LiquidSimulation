using System.Collections.Generic;
using UnityEngine;

public sealed partial class RougeDefenseTower
{
    private const string TowerAudioResourceRoot = "Sfx/Towers/";
    private const string TowerBuildClip = TowerAudioResourceRoot + "tower_build";
    private const string TowerUpgradeClip = TowerAudioResourceRoot + "tower_upgrade";
    private const string TowerIceClip = TowerAudioResourceRoot + "tower_ice";
    private const string TowerMachineGunClip = TowerAudioResourceRoot + "tower_machine_gun";
    private const string TowerCannonClip = TowerAudioResourceRoot + "tower_cannon";
    private const string TowerFlameClip = TowerAudioResourceRoot + "tower_flame";
    private const string TowerLaserLoopClip = TowerAudioResourceRoot + "tower_laser_loop";
    private const string TowerPiercingChargeClip = TowerAudioResourceRoot + "tower_piercing_charge";
    private const string TowerPiercingLaserClip = TowerAudioResourceRoot + "tower_piercing_laser";
    private const string TowerOrbitSphereClip = TowerAudioResourceRoot + "tower_orbit_sphere";
    private const string TowerRocketBarrageClip = TowerAudioResourceRoot + "tower_rocket_barrage";
    private const string TowerChargeActivateClip = TowerAudioResourceRoot + "tower_charge_activate";
    private const string TowerReinforcementActivateClip =
        TowerAudioResourceRoot + "tower_reinforcement_activate";

    // Keep the real-voice budget below AudioManager's 32 voice limit, leaving room for
    // music, UI, and non-tower effects. Combat never steals an active voice; utility
    // feedback may replace only the utility cue that would finish first.
    private const int TowerCombatVoiceCount = 16;
    private const int TowerUtilityVoiceCount = 4;
    private const int TowerLaserLoopVoiceCount = 8;
    private const int TowerCombatPriority = 128;
    private const int TowerUtilityPriority = 80;
    private const int TowerLoopPriority = 176;
    private const float TowerLaserLoopTotalVolume = 0.12f;
    private const string TowerAudioRuntimeName = "Rouge Tower Audio Runtime";

    private static readonly string[] TowerAudioResourcePaths =
    {
        TowerBuildClip,
        TowerUpgradeClip,
        TowerIceClip,
        TowerMachineGunClip,
        TowerCannonClip,
        TowerFlameClip,
        TowerLaserLoopClip,
        TowerPiercingChargeClip,
        TowerPiercingLaserClip,
        TowerOrbitSphereClip,
        TowerRocketBarrageClip,
        TowerChargeActivateClip,
        TowerReinforcementActivateClip
    };

    private sealed class TowerOneShotVoice
    {
        public AudioSource Source;
        public RougeDefenseTower Owner;
        public double ReadyDspTime;
    }

    private sealed class TowerLoopVoice
    {
        public AudioSource Source;
        public RougeDefenseTower Owner;
    }

    private static readonly Dictionary<string, AudioClip> TowerAudioClipCache =
        new Dictionary<string, AudioClip>();
    private static readonly HashSet<string> MissingTowerAudioClips = new HashSet<string>();
    private static readonly Dictionary<string, double> GlobalTowerAudioCooldowns =
        new Dictionary<string, double>();

    private static GameObject towerAudioRuntimeRoot;
    private static TowerOneShotVoice[] towerCombatVoices;
    private static TowerOneShotVoice[] towerUtilityVoices;
    private static TowerLoopVoice[] towerLoopVoices;

    [System.NonSerialized] private double nextTowerAttackAudioTime;
    [System.NonSerialized] private uint towerAudioVariationCounter;

    internal static void PreloadTowerAudio()
    {
        for (int i = 0; i < TowerAudioResourcePaths.Length; i++)
            LoadTowerAudioClip(TowerAudioResourcePaths[i]);
    }

    internal void PlayPlacementSound()
    {
        if (IsChargeTower)
        {
            PlayTowerOneShot(TowerChargeActivateClip, 0.30f, 0.98f, 1.02f,
                0.05d, 0.04d, false);
            return;
        }
        if (IsReinforcementTower)
        {
            PlayTowerOneShot(TowerReinforcementActivateClip, 0.28f, 0.97f, 1.03f,
                0.05d, 0.04d, false);
            return;
        }
        PlayTowerOneShot(TowerBuildClip, 0.20f, 0.96f, 1.04f,
            0.05d, 0.025d, false);
    }

    internal void PlayUpgradeSound()
    {
        PlayTowerOneShot(TowerUpgradeClip, 0.26f, 0.98f, 1.04f,
            0.05d, 0.035d, false);
    }

    internal void PlaySellSound()
    {
        PlayTowerOneShot(TowerBuildClip, 0.17f, 0.78f, 0.86f,
            0.05d, 0.025d, false);
    }

    internal void PlayPiercingChargeSound()
    {
        if (towerType != RougeTowerType.PiercingLaser) return;
        PlayTowerOneShot(TowerPiercingChargeClip, 0.18f, 0.98f, 1.02f,
            0.30d, 0.07d, true);
    }

    internal void PlayAttackSound()
    {
        string resourcePath;
        float volume;
        float pitchMin;
        float pitchMax;
        double towerCooldown;
        double globalCooldown;
        switch (towerType)
        {
            case RougeTowerType.Ice:
                resourcePath = TowerIceClip;
                volume = 0.20f;
                pitchMin = 0.96f;
                pitchMax = 1.04f;
                towerCooldown = 0.22d;
                globalCooldown = 0.035d;
                break;
            case RougeTowerType.MachineGun:
                resourcePath = TowerMachineGunClip;
                volume = 0.105f;
                pitchMin = 1.04f;
                pitchMax = 1.14f;
                towerCooldown = 0.09d;
                globalCooldown = 0.018d;
                break;
            case RougeTowerType.Cannon:
                resourcePath = TowerCannonClip;
                volume = 0.31f;
                pitchMin = 0.92f;
                pitchMax = 1.02f;
                towerCooldown = 0.18d;
                globalCooldown = 0.06d;
                break;
            case RougeTowerType.Flame:
                resourcePath = TowerFlameClip;
                volume = 0.17f;
                pitchMin = 0.96f;
                pitchMax = 1.02f;
                towerCooldown = 0.90d;
                globalCooldown = 0.16d;
                break;
            case RougeTowerType.PiercingLaser:
                resourcePath = TowerPiercingLaserClip;
                volume = 0.31f;
                pitchMin = 0.94f;
                pitchMax = 1.02f;
                towerCooldown = 0.45d;
                globalCooldown = 0.08d;
                break;
            case RougeTowerType.OrbitSphere:
                resourcePath = TowerOrbitSphereClip;
                volume = 0.21f;
                pitchMin = 0.96f;
                pitchMax = 1.04f;
                towerCooldown = 0.42d;
                globalCooldown = 0.07d;
                break;
            case RougeTowerType.RocketBarrage:
                resourcePath = TowerRocketBarrageClip;
                volume = 0.18f;
                pitchMin = 0.96f;
                pitchMax = 1.03f;
                towerCooldown = 0.65d;
                globalCooldown = 0.15d;
                break;
            default:
                return;
        }

        PlayTowerOneShot(resourcePath, volume, pitchMin, pitchMax,
            towerCooldown, globalCooldown, true);
    }

    internal void SetContinuousAttackSound(bool active)
    {
        if (towerType != RougeTowerType.Laser) active = false;
        if (!active)
        {
            ReleaseTowerLoopVoice(this);
            return;
        }

        if (!isActiveAndEnabled)
        {
            ReleaseTowerLoopVoice(this);
            return;
        }

        AudioClip clip = LoadTowerAudioClip(TowerLaserLoopClip);
        if (clip == null)
        {
            ReleaseTowerLoopVoice(this);
            return;
        }
        EnsureTowerAudioRuntime();

        TowerLoopVoice voice = FindTowerLoopVoice(this);
        if (voice == null)
        {
            voice = FindFreeTowerLoopVoice();
            if (voice == null) return;
            voice.Owner = this;
        }

        AudioSource source = voice.Source;
        source.panStereo = GetTowerAudioPan();
        if (source.isPlaying && source.clip == clip) return;

        source.Stop();
        source.clip = clip;
        source.loop = true;
        source.volume = TowerLaserLoopTotalVolume;
        source.pitch = 1f;
        source.Play();
        NormalizeTowerLoopVolumes();
    }

    /// <summary>Stops combat one-shots and the continuous beam owned by this tower.</summary>
    internal void StopAttackSounds()
    {
        ReleaseOwnedCombatVoices(this);
        ReleaseTowerLoopVoice(this);
    }

    /// <summary>Stops every tower combat cue while preserving build and upgrade feedback.</summary>
    internal static void StopAllTowerCombatSounds()
    {
        StopOneShotPool(towerCombatVoices);
        StopLoopPool();
    }

    /// <summary>Releases all runtime voices and decoded tower audio data.</summary>
    internal static void ShutdownTowerAudio()
    {
        StopOneShotPool(towerCombatVoices);
        StopOneShotPool(towerUtilityVoices);
        StopLoopPool();

        if (towerAudioRuntimeRoot != null)
        {
            // Hiding immediately prevents a same-frame restart from adopting an object
            // already scheduled for destruction.
            towerAudioRuntimeRoot.SetActive(false);
            if (Application.isPlaying) Object.Destroy(towerAudioRuntimeRoot);
            else Object.DestroyImmediate(towerAudioRuntimeRoot);
        }
        towerAudioRuntimeRoot = null;
        towerCombatVoices = null;
        towerUtilityVoices = null;
        towerLoopVoices = null;

        foreach (AudioClip clip in TowerAudioClipCache.Values)
        {
            if (clip != null && clip.loadState != AudioDataLoadState.Unloaded)
                clip.UnloadAudioData();
        }
        TowerAudioClipCache.Clear();
        MissingTowerAudioClips.Clear();
        GlobalTowerAudioCooldowns.Clear();
    }

    private void PlayTowerOneShot(string resourcePath, float volume, float pitchMin,
        float pitchMax, double towerCooldown, double globalCooldown, bool combatCue)
    {
        AudioClip clip = LoadTowerAudioClip(resourcePath);
        if (clip == null || !isActiveAndEnabled) return;

        double now = AudioSettings.dspTime;
        if (combatCue && now < nextTowerAttackAudioTime) return;
        if (GlobalTowerAudioCooldowns.TryGetValue(resourcePath, out double globalReadyTime) &&
            now < globalReadyTime)
            return;

        // Reserve both cooldown windows before looking for a slot. In particular, a full
        // combat pool must not make every same-type tower rescan all voices every attack.
        if (combatCue) nextTowerAttackAudioTime = now + Mathf.Max(0f, (float)towerCooldown);
        GlobalTowerAudioCooldowns[resourcePath] = now + Mathf.Max(0f, (float)globalCooldown);

        EnsureTowerAudioRuntime();
        TowerOneShotVoice voice = combatCue
            ? AcquireCombatVoice(now)
            : AcquireUtilityVoice(now);
        if (voice == null) return;

        float pitch = GetTowerAudioPitch(pitchMin, pitchMax);
        PlayOneShotVoice(voice, clip, Mathf.Clamp01(volume), pitch,
            GetTowerAudioPan(), combatCue ? this : null, now);
    }

    private static TowerOneShotVoice AcquireCombatVoice(double now)
    {
        return FindAvailableOneShotVoice(towerCombatVoices, now);
    }

    private static TowerOneShotVoice AcquireUtilityVoice(double now)
    {
        TowerOneShotVoice available = FindAvailableOneShotVoice(towerUtilityVoices, now);
        if (available != null) return available;

        TowerOneShotVoice earliest = null;
        double earliestReadyTime = double.MaxValue;
        for (int i = 0; i < towerUtilityVoices.Length; i++)
        {
            TowerOneShotVoice candidate = towerUtilityVoices[i];
            if (candidate == null || candidate.Source == null) continue;
            if (candidate.ReadyDspTime >= earliestReadyTime) continue;
            earliestReadyTime = candidate.ReadyDspTime;
            earliest = candidate;
        }
        if (earliest != null) ResetOneShotVoice(earliest);
        return earliest;
    }

    private static TowerOneShotVoice FindAvailableOneShotVoice(TowerOneShotVoice[] pool,
        double now)
    {
        if (pool == null) return null;
        for (int i = 0; i < pool.Length; i++)
        {
            TowerOneShotVoice voice = pool[i];
            if (voice == null || voice.Source == null) continue;
            if (voice.Source.isPlaying && now < voice.ReadyDspTime) continue;
            ResetOneShotVoice(voice);
            return voice;
        }
        return null;
    }

    private static void PlayOneShotVoice(TowerOneShotVoice voice, AudioClip clip,
        float volume, float pitch, float pan, RougeDefenseTower owner, double now)
    {
        AudioSource source = voice.Source;
        source.Stop();
        source.clip = clip;
        source.loop = false;
        source.volume = volume;
        source.pitch = pitch;
        source.panStereo = pan;
        source.Play();
        voice.Owner = owner;
        voice.ReadyDspTime = now + clip.length / Mathf.Max(0.01f, Mathf.Abs(pitch));
    }

    private static void ResetOneShotVoice(TowerOneShotVoice voice)
    {
        if (voice == null) return;
        if (voice.Source != null)
        {
            voice.Source.Stop();
            voice.Source.clip = null;
        }
        voice.Owner = null;
        voice.ReadyDspTime = 0d;
    }

    private static void StopOneShotPool(TowerOneShotVoice[] pool)
    {
        if (pool == null) return;
        for (int i = 0; i < pool.Length; i++) ResetOneShotVoice(pool[i]);
    }

    private static void ReleaseOwnedCombatVoices(RougeDefenseTower owner)
    {
        if (owner == null || towerCombatVoices == null) return;
        for (int i = 0; i < towerCombatVoices.Length; i++)
        {
            TowerOneShotVoice voice = towerCombatVoices[i];
            if (voice != null && voice.Owner == owner) ResetOneShotVoice(voice);
        }
    }

    private static TowerLoopVoice FindTowerLoopVoice(RougeDefenseTower owner)
    {
        if (towerLoopVoices == null) return null;
        for (int i = 0; i < towerLoopVoices.Length; i++)
        {
            TowerLoopVoice voice = towerLoopVoices[i];
            if (voice != null && voice.Owner == owner) return voice;
        }
        return null;
    }

    private static TowerLoopVoice FindFreeTowerLoopVoice()
    {
        if (towerLoopVoices == null) return null;
        for (int i = 0; i < towerLoopVoices.Length; i++)
        {
            TowerLoopVoice voice = towerLoopVoices[i];
            if (voice == null || voice.Source == null || voice.Owner != null) continue;
            return voice;
        }
        return null;
    }

    private static void ReleaseTowerLoopVoice(RougeDefenseTower owner)
    {
        if (owner == null || towerLoopVoices == null) return;
        for (int i = 0; i < towerLoopVoices.Length; i++)
        {
            TowerLoopVoice voice = towerLoopVoices[i];
            if (voice == null || voice.Owner != owner) continue;
            ResetLoopVoice(voice);
            NormalizeTowerLoopVolumes();
            return;
        }
    }

    private static void CleanupInvalidTowerLoopOwners()
    {
        if (towerLoopVoices == null) return;
        bool changed = false;
        for (int i = 0; i < towerLoopVoices.Length; i++)
        {
            TowerLoopVoice voice = towerLoopVoices[i];
            if (voice == null || ReferenceEquals(voice.Owner, null) || voice.Owner != null)
                continue;
            ResetLoopVoice(voice); // Unity-destroyed owner: managed reference survives but == null.
            changed = true;
        }
        if (changed) NormalizeTowerLoopVolumes();
    }

    private static void ResetLoopVoice(TowerLoopVoice voice)
    {
        if (voice == null) return;
        if (voice.Source != null)
        {
            voice.Source.Stop();
            voice.Source.clip = null;
        }
        voice.Owner = null;
    }

    private static void StopLoopPool()
    {
        if (towerLoopVoices == null) return;
        for (int i = 0; i < towerLoopVoices.Length; i++) ResetLoopVoice(towerLoopVoices[i]);
    }

    private static void NormalizeTowerLoopVolumes()
    {
        if (towerLoopVoices == null) return;
        int activeCount = 0;
        for (int i = 0; i < towerLoopVoices.Length; i++)
        {
            TowerLoopVoice voice = towerLoopVoices[i];
            if (voice != null && voice.Source != null && voice.Owner != null) activeCount++;
        }
        if (activeCount <= 0) return;

        float volume = TowerLaserLoopTotalVolume / activeCount;
        for (int i = 0; i < towerLoopVoices.Length; i++)
        {
            TowerLoopVoice voice = towerLoopVoices[i];
            if (voice != null && voice.Source != null && voice.Owner != null)
                voice.Source.volume = volume;
        }
    }

    private static void EnsureTowerAudioRuntime()
    {
        if (towerAudioRuntimeRoot != null &&
            HasValidPool(towerCombatVoices, TowerCombatVoiceCount) &&
            HasValidPool(towerUtilityVoices, TowerUtilityVoiceCount) &&
            HasValidPool(towerLoopVoices, TowerLaserLoopVoiceCount))
        {
            CleanupInvalidTowerLoopOwners();
            return;
        }

        AudioSource[] existingSources = null;
        if (towerAudioRuntimeRoot == null)
        {
            towerAudioRuntimeRoot = GameObject.Find(TowerAudioRuntimeName);
            if (towerAudioRuntimeRoot != null)
                existingSources = towerAudioRuntimeRoot.GetComponents<AudioSource>();
            else
                towerAudioRuntimeRoot = new GameObject(TowerAudioRuntimeName);
        }
        else
        {
            existingSources = towerAudioRuntimeRoot.GetComponents<AudioSource>();
        }

        towerAudioRuntimeRoot.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
        towerAudioRuntimeRoot.SetActive(true);
        RebuildTowerAudioPools(existingSources);
    }

    private static bool HasValidPool(TowerOneShotVoice[] pool, int expectedCount)
    {
        if (pool == null || pool.Length != expectedCount) return false;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null || pool[i].Source == null) return false;
        }
        return true;
    }

    private static bool HasValidPool(TowerLoopVoice[] pool, int expectedCount)
    {
        if (pool == null || pool.Length != expectedCount) return false;
        for (int i = 0; i < pool.Length; i++)
        {
            if (pool[i] == null || pool[i].Source == null) return false;
        }
        return true;
    }

    private static void RebuildTowerAudioPools(AudioSource[] existingSources)
    {
        int existingIndex = 0;
        towerCombatVoices = CreateOneShotPool(TowerCombatVoiceCount,
            TowerCombatPriority, existingSources, ref existingIndex);
        towerUtilityVoices = CreateOneShotPool(TowerUtilityVoiceCount,
            TowerUtilityPriority, existingSources, ref existingIndex);
        towerLoopVoices = CreateLoopPool(TowerLaserLoopVoiceCount,
            TowerLoopPriority, existingSources, ref existingIndex);

        if (existingSources == null) return;
        for (int i = existingIndex; i < existingSources.Length; i++)
        {
            AudioSource extra = existingSources[i];
            if (extra == null) continue;
            extra.Stop();
            if (Application.isPlaying) Object.Destroy(extra);
            else Object.DestroyImmediate(extra);
        }
    }

    private static TowerOneShotVoice[] CreateOneShotPool(int count, int priority,
        AudioSource[] existingSources, ref int existingIndex)
    {
        TowerOneShotVoice[] pool = new TowerOneShotVoice[count];
        for (int i = 0; i < count; i++)
        {
            AudioSource source = TakeOrCreateAudioSource(existingSources, ref existingIndex);
            ConfigureTowerAudioSource(source, priority);
            pool[i] = new TowerOneShotVoice { Source = source };
        }
        return pool;
    }

    private static TowerLoopVoice[] CreateLoopPool(int count, int priority,
        AudioSource[] existingSources, ref int existingIndex)
    {
        TowerLoopVoice[] pool = new TowerLoopVoice[count];
        for (int i = 0; i < count; i++)
        {
            AudioSource source = TakeOrCreateAudioSource(existingSources, ref existingIndex);
            ConfigureTowerAudioSource(source, priority);
            pool[i] = new TowerLoopVoice { Source = source };
        }
        return pool;
    }

    private static AudioSource TakeOrCreateAudioSource(AudioSource[] existingSources,
        ref int existingIndex)
    {
        if (existingSources != null)
        {
            while (existingIndex < existingSources.Length)
            {
                AudioSource existing = existingSources[existingIndex++];
                if (existing != null) return existing;
            }
        }
        return towerAudioRuntimeRoot.AddComponent<AudioSource>();
    }

    private static void ConfigureTowerAudioSource(AudioSource source, int priority)
    {
        source.Stop();
        source.clip = null;
        source.enabled = true;
        source.playOnAwake = false;
        source.loop = false;
        source.volume = 1f;
        source.pitch = 1f;
        source.panStereo = 0f;
        source.priority = Mathf.Clamp(priority, 0, 256);
        source.spatialBlend = 0f;
        source.dopplerLevel = 0f;
        source.reverbZoneMix = 0f;
    }

    private float GetTowerAudioPan()
    {
        Camera camera = Camera.main;
        if (camera == null) return 0f;
        Vector3 viewport = camera.WorldToViewportPoint(transform.position);
        return viewport.z > 0f
            ? Mathf.Clamp((viewport.x - 0.5f) * 1.3f, -0.65f, 0.65f)
            : 0f;
    }

    private float GetTowerAudioPitch(float minimum, float maximum)
    {
        towerAudioVariationCounter++;
        uint hash = unchecked((uint)GetInstanceID() * 747796405u +
                              towerAudioVariationCounter * 2891336453u + 0x9E3779B9u);
        hash ^= hash >> 16;
        hash *= 2246822519u;
        hash ^= hash >> 13;
        float t = (hash & 0xFFFFu) / 65535f;
        return Mathf.Lerp(minimum, maximum, t);
    }

    private static AudioClip LoadTowerAudioClip(string resourcePath)
    {
        if (TowerAudioClipCache.TryGetValue(resourcePath, out AudioClip cached)) return cached;
        if (MissingTowerAudioClips.Contains(resourcePath)) return null;

        AudioClip clip = Resources.Load<AudioClip>(resourcePath);
        if (clip == null)
        {
            MissingTowerAudioClips.Add(resourcePath);
            Debug.LogWarning("Missing tower audio clip at Resources/" + resourcePath);
            return null;
        }
        if (Application.isPlaying && clip.loadState == AudioDataLoadState.Unloaded)
            clip.LoadAudioData();
        TowerAudioClipCache[resourcePath] = clip;
        return clip;
    }
}
