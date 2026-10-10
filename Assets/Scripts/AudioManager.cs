using UnityEngine;

namespace Match3
{
    /// <summary>
    /// The one place sounds are played from. Other scripts call <see cref="Play"/> with a
    /// <see cref="SoundId"/> and never touch clips or AudioSources themselves, so which clip plays
    /// (the current <see cref="SoundSet"/>) can change without editing any of them.
    ///
    /// It also owns the master volume, which is saved with PlayerPrefs like the theme choices.
    ///
    /// How Unity audio fits together:
    ///   AudioClip     - the sound data (a .wav/.ogg asset). Stored in the SoundSet.
    ///   AudioSource   - a "speaker" component that plays clips. This object has two: one
    ///                   for the quick effects, and one just for the round-end horn.
    ///   AudioListener - the "microphone", on the Main Camera. Everything the game plays is
    ///                   heard through it, so its static volume/pause settings affect every sound.
    /// </summary>
    // RequireComponent makes Unity add an AudioSource automatically when this script is added to
    // a GameObject (and stops it being removed while this script is there).
    [RequireComponent(typeof(AudioSource))]
    public class AudioManager : MonoBehaviour
    {
        public const string SoundVolumeKey = "Settings.SoundVolume";
        public const float DefaultVolume = 0.8f;

        [Tooltip("The clips to play. Any empty slot in it simply plays nothing.")]
        [SerializeField] private SoundSet soundSet;

        private AudioSource audioSource;
        // Pitch is a setting of the whole AudioSource, so a pitched-up cascade sound on the
        // shared source would bend a long sound that's still playing there. The horn is long
        // (over a second) and plays just as the last cascade settles, so it gets a source of
        // its own that nothing else ever changes the pitch of.
        private AudioSource hornSource;

        /// <summary>Master volume, from 0 (muted) to 1 (full).</summary>
        public float Volume { get; private set; } = DefaultVolume;

        public SoundSet SoundSet => soundSet;

        /// <summary>
        /// Fired whenever <see cref="Volume"/> changes, so the Settings slider can follow a
        /// change it didn't make itself (the volume arriving from the player's account).
        /// </summary>
        public event System.Action VolumeChanged;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            // This source only ever plays one-shots on request, never a clip of its own at startup.
            audioSource.playOnAwake = false;

            // AddComponent creates the second source from code, so the scene doesn't need
            // editing. It sends its sound to the same place as the first one.
            hornSource = gameObject.AddComponent<AudioSource>();
            hornSource.playOnAwake = false;
            hornSource.outputAudioMixerGroup = audioSource.outputAudioMixerGroup;

            // GetFloat returns the second argument when nothing has been saved yet.
            ApplyVolume(PlayerPrefs.GetFloat(SoundVolumeKey, DefaultVolume));
        }

        /// <summary>
        /// Plays <paramref name="sound"/> from the current sound set, once. Does nothing if the
        /// set has no clip for it. <paramref name="pitch"/> 1 is normal; 2 is an octave up (and
        /// twice as fast), 0.5 an octave down. <paramref name="volumeScale"/> makes this one play
        /// quieter (0-1) without changing the master volume, e.g. for a sound that plays so often
        /// it would get tiring at full volume.
        /// </summary>
        public void Play(SoundId sound, float pitch = 1f, float volumeScale = 1f)
        {
            AudioClip clip = soundSet != null ? soundSet.GetClip(sound) : null;
            if (clip == null)
            {
                return;
            }

            // Play() plays the source's own `clip` and restarts it if it's already playing, so a
            // source can only play one clip at a time that way. PlayOneShot(clip) instead layers a
            // new copy on top of whatever is already playing, which is what we want for quick
            // sound effects that can overlap (two matches in a row, a tick during a match...).
            //
            // Pitch belongs to the source, not to each one-shot, so changing it also bends any
            // one-shot from this source that's still ringing out. That's fine for short effects.
            // It was audible on the long horn, which is why the horn has its own source.
            AudioSource source = sound == SoundId.RoundEndHorn ? hornSource : audioSource;
            source.pitch = pitch;
            source.PlayOneShot(clip, volumeScale);
        }

        /// <summary>
        /// Sets the master volume (clamped to 0-1) and applies it to every sound right away.
        /// It's remembered for next time; call <see cref="SaveVolume"/> to write it to disk.
        /// </summary>
        public void SetVolume(float volume)
        {
            ApplyVolume(volume);
            // SetFloat only updates Unity's in-memory copy, which is cheap enough to do on every
            // step of a slider drag. Writing it to disk waits for SaveVolume.
            PlayerPrefs.SetFloat(SoundVolumeKey, Volume);
        }

        /// <summary>Writes the volume to disk now, e.g. when the Settings menu closes.</summary>
        public void SaveVolume()
        {
            // Unity also saves PlayerPrefs when the game quits normally, but a browser tab being
            // closed isn't a normal quit, so the Web build would lose the change without this.
            PlayerPrefs.Save();
        }

        private void ApplyVolume(float volume)
        {
            Volume = Mathf.Clamp01(volume);
            // AudioListener.volume is a static (global) setting: it scales everything the
            // listener hears, so one line sets the volume of every sound in the game.
            AudioListener.volume = Volume;
            VolumeChanged?.Invoke();
        }
    }
}
