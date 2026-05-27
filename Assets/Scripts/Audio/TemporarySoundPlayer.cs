using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// <see cref="AudioManager"/> 가 클립당 1회용으로 생성하는 AudioSource 래퍼.
/// 비루프는 재생 후 자동 파괴, 루프는 매니저가 추적 후 명시적 파괴한다.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class TemporarySoundPlayer : MonoBehaviour
{
    private AudioSource source;

    /// <summary>매니저가 이름으로 정지할 때 비교 키로 사용.</summary>
    public string ClipName { get; private set; }

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f; // 기본 2D
    }

    public void InitSound(AudioClip clip)
    {
        if (source == null) source = GetComponent<AudioSource>();
        source.clip = clip;
        ClipName = clip != null ? clip.name : string.Empty;
    }

    public void Play(AudioMixerGroup group, float delay = 0f, bool isLoop = false)
    {
        if (source.clip == null)
        {
            Debug.LogWarning("[TemporarySoundPlayer] 클립이 비어있어 재생할 수 없습니다.");
            Destroy(gameObject);
            return;
        }

        if (group != null) source.outputAudioMixerGroup = group;
        source.loop = isLoop;

        if (delay > 0f) source.PlayDelayed(delay);
        else source.Play();

        // 비루프는 길이 + 여유 후 자동 정리
        if (!isLoop)
        {
            float lifetime = source.clip.length + delay + 0.1f;
            Destroy(gameObject, lifetime);
        }
    }

    /// <summary>3D 위치 기반 사운드로 전환(VR 공간 음원).</summary>
    public void SetSpatial(float minDistance, float maxDistance)
    {
        source.spatialBlend = 1f;
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.dopplerLevel = 0f; // VR 어색함 방지용 기본값
    }

    public void SetVolume(float volume)
    {
        if (source != null) source.volume = volume;
    }

    /// <summary>현재 재생 중인지(루프 사운드 상태 체크용).</summary>
    public bool IsPlaying => source != null && source.isPlaying;
}
