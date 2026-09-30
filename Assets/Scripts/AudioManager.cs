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
    ///   AudioSource   - a "speaker" component that plays clips. This object has one.
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

        /// <summary>Master volume, from 0 (muted) to 1 (full).</summary>
        public float Volume { get; private set; } = DefaultVolume;

        public SoundSet SoundSet => soundSet;

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            // This source only ever plays one-shots on request, never a clip of its own at startup.
            audioSource.playOnAwake = false;

            // GetFloat returns the second argument when nothing has been saved yet.
            ApplyVolume(PlayerPrefs.GetFloat(SoundVolumeKey, DefaultVolume));
        }

        /// <summary>
        /// Plays <paramref name="sound"/> from the current sound set, once. Does nothing if the
        /// set has no clip for it. <paramref name="pitch"/> 1 is normal; 2 is an octave up (and
        /// twice as fast), 0.5 an octave down.
        /// </summary>
        public void Play(SoundId sound, float pitch = 1f)
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
            // one-shot from this source that's still ringing out. That's fine for short effects;
            // if it ever becomes audible, the fix is a few AudioSources to take turns with.
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip);
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
        }
    }
}
