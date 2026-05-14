using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

// 사운드 분류. AudioMixer 의 노출 파라미터 이름과 그룹 이름이 이와 동일해야 한다.
public enum SoundType
{
    BGM,
    SFX,
    Voice,
}

/// <summary>
/// 전역 오디오 매니저(싱글톤). 클립 사전 등록 → 이름으로 재생.
/// VR 공간 사운드를 위해 <see cref="PlaySoundAt"/> 제공.
/// 네트워크 동기화는 하지 않는다 — 모든 클라이언트에서 RPC 등으로 동일하게 호출.
/// </summary>
public class AudioManager : MonoBehaviour
{
    // ---- 싱글톤 ----
    private static AudioManager instance;
    public static AudioManager Instance
    {
        get
        {
            if (instance == null)
                instance = FindFirstObjectByType<AudioManager>();
            return instance;
        }
    }

    // ---- 직렬화 필드 ----
    [Header("Mixer")]
    [SerializeField] private AudioMixer audioMixer;

    [Header("사전 로드 클립")]
    [SerializeField] private AudioClip[] preloadClips;

    [Header("UI 슬라이더 (옵션 메뉴, 선택)")]
    public Slider masterSlider;
    public Slider bgmSlider;
    public Slider sfxSlider;
    public Slider voiceSlider;

    // ---- 내부 상태 ----
    // 클립 이름 → 클립
    private Dictionary<string, AudioClip> clipsDictionary;
    // 루프 사운드 추적 — 이름으로 멈출 수 있게
    private List<TemporarySoundPlayer> instantiatedSounds;

    // 현재 볼륨 (슬라이더 스케일 0~1)
    private float currentMaster = 1f;
    private float currentBGM = 1f;
    private float currentSFX = 1f;
    private float currentVoice = 1f;

    // AudioMixer 노출 파라미터 키 — SoundType.ToString() 과 일치시킴
    private const string MasterParam = "Master";
    private const string PrefKeyMaster = "vol_master";
    private const string PrefKeyBGM = "vol_bgm";
    private const string PrefKeySFX = "vol_sfx";
    private const string PrefKeyVoice = "vol_voice";

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);

        // 딕셔너리/리스트 초기화는 Awake 에서 (다른 스크립트 Start 보다 먼저)
        clipsDictionary = new Dictionary<string, AudioClip>();
        if (preloadClips != null)
        {
            foreach (AudioClip clip in preloadClips)
            {
                if (clip == null) continue;
                clipsDictionary[clip.name] = clip;
            }
        }
        instantiatedSounds = new List<TemporarySoundPlayer>();

        // 저장된 볼륨 복원
        currentMaster = PlayerPrefs.GetFloat(PrefKeyMaster, 1f);
        currentBGM = PlayerPrefs.GetFloat(PrefKeyBGM, 1f);
        currentSFX = PlayerPrefs.GetFloat(PrefKeySFX, 1f);
        currentVoice = PlayerPrefs.GetFloat(PrefKeyVoice, 1f);
        ApplyVolume(MasterParam, currentMaster);
        ApplyVolume(SoundType.BGM.ToString(), currentBGM);
        ApplyVolume(SoundType.SFX.ToString(), currentSFX);
        ApplyVolume(SoundType.Voice.ToString(), currentVoice);
    }

    private void Start()
    {
        BindSlider(masterSlider, currentMaster, sliderMasterValueChanged);
        BindSlider(bgmSlider, currentBGM, sliderBGMValueChanged);
        BindSlider(sfxSlider, currentSFX, sliderSFXValueChanged);
        BindSlider(voiceSlider, currentVoice, sliderVoiceValueChanged);
    }

    private static void BindSlider(Slider slider, float initial, UnityEngine.Events.UnityAction<float> handler)
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(initial);
        slider.onValueChanged.AddListener(handler);
    }

    // ===== 클립 등록 / 검색 =====

    /// <summary>런타임에 클립 추가(예: Addressables 로딩 후).</summary>
    public void RegisterClip(AudioClip clip)
    {
        if (clip == null) return;
        clipsDictionary[clip.name] = clip;
    }

    private AudioClip GetClip(string clipName)
    {
        if (clipsDictionary.TryGetValue(clipName, out AudioClip clip))
            return clip;
        Debug.LogError($"[AudioManager] '{clipName}' 클립이 존재하지 않습니다.");
        return null;
    }

    // ===== 재생 =====

    /// <summary>2D 재생(논포지셔널 SFX, BGM, UI).</summary>
    public TemporarySoundPlayer PlaySound(string clipName, float delay = 0f, bool isLoop = false, SoundType type = SoundType.SFX)
    {
        AudioClip clip = GetClip(clipName);
        if (clip == null) return null;

        GameObject obj = new GameObject($"TempSound_{clipName}");
        TemporarySoundPlayer player = obj.AddComponent<TemporarySoundPlayer>();
        player.InitSound(clip);

        AudioMixerGroup group = null;
        if (audioMixer != null)
        {
            AudioMixerGroup[] groups = audioMixer.FindMatchingGroups(type.ToString());
            if (groups != null && groups.Length > 0) group = groups[0];
        }
        player.Play(group, delay, isLoop);

        if (isLoop) instantiatedSounds.Add(player);
        return player;
    }

    /// <summary>월드 좌표 3D 사운드(VR 공간 음원).</summary>
    public TemporarySoundPlayer PlaySoundAt(
        string clipName,
        Vector3 worldPosition,
        float delay = 0f,
        bool isLoop = false,
        SoundType type = SoundType.SFX,
        float minDistance = 1f,
        float maxDistance = 15f)
    {
        TemporarySoundPlayer player = PlaySound(clipName, delay, isLoop, type);
        if (player == null) return null;
        player.transform.position = worldPosition;
        player.SetSpatial(minDistance, maxDistance);
        return player;
    }

    /// <summary>이름으로 루프 사운드 정지.</summary>
    public void StopLoopSound(string clipName)
    {
        for (int i = instantiatedSounds.Count - 1; i >= 0; i--)
        {
            TemporarySoundPlayer audioPlayer = instantiatedSounds[i];
            if (audioPlayer == null)
            {
                instantiatedSounds.RemoveAt(i);
                continue;
            }
            if (audioPlayer.ClipName == clipName)
            {
                instantiatedSounds.RemoveAt(i);
                Destroy(audioPlayer.gameObject);
                return;
            }
        }
        Debug.LogWarning($"[AudioManager] 루프 사운드 '{clipName}'을 찾을 수 없습니다.");
    }

    /// <summary>핸들로 루프 사운드 정지(이름 충돌 회피).</summary>
    public void StopLoopSound(TemporarySoundPlayer player)
    {
        if (player == null) return;
        instantiatedSounds.Remove(player);
        Destroy(player.gameObject);
    }

    // ===== 볼륨 =====

    // 선형 0~1 → dB(-80~0). 0 근처는 -80dB 로 클램프해서 log10(0) 회피.
    private static float LinearToDb(float linear)
    {
        return Mathf.Log10(Mathf.Max(linear, 0.0001f)) * 20f;
    }

    private void ApplyVolume(string exposedParam, float linear)
    {
        if (audioMixer == null) return;
        audioMixer.SetFloat(exposedParam, LinearToDb(linear));
    }

    /// <summary>SoundType 그룹 볼륨을 dB 단위로 직접 지정(고급).</summary>
    public void SetVolume(SoundType type, float dB)
    {
        if (audioMixer == null) return;
        audioMixer.SetFloat(type.ToString(), dB);
    }

    public void sliderMasterValueChanged(float value)
    {
        currentMaster = value;
        ApplyVolume(MasterParam, value);
        PlayerPrefs.SetFloat(PrefKeyMaster, value);
    }

    public void sliderBGMValueChanged(float value)
    {
        currentBGM = value;
        ApplyVolume(SoundType.BGM.ToString(), value);
        PlayerPrefs.SetFloat(PrefKeyBGM, value);
    }

    public void sliderSFXValueChanged(float value)
    {
        currentSFX = value;
        ApplyVolume(SoundType.SFX.ToString(), value);
        PlayerPrefs.SetFloat(PrefKeySFX, value);
    }

    public void sliderVoiceValueChanged(float value)
    {
        currentVoice = value;
        ApplyVolume(SoundType.Voice.ToString(), value);
        PlayerPrefs.SetFloat(PrefKeyVoice, value);
    }

    // 현재 슬라이더 스케일(0~1) 값
    public float GetMasterSliderValue() => currentMaster;
    public float GetBGMSliderValue() => currentBGM;
    public float GetSFXSliderValue() => currentSFX;
    public float GetVoiceSliderValue() => currentVoice;
}
