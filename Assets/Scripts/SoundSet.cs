using UnityEngine;

namespace Match3
{
    /// <summary>Every sound the game can ask the <see cref="AudioManager"/> to play.</summary>
    public enum SoundId
    {
        Match3,
        Match4,
        Match5Plus,
        CountdownTick,
        RoundEndHorn,
        Swap,
        InvalidSwap,
        AchievementUnlocked,
        PowerGemCreated,
        FlameDetonate,
        StarDetonate,
        HypercubeActivate,
    }

    /// <summary>
    /// One complete set of gameplay sounds: which <see cref="AudioClip"/> to play for each
    /// <see cref="SoundId"/>. Swapping the set changes how the game sounds without touching any
    /// code that plays sounds. Any slot can be left empty; an empty slot just plays nothing, so a
    /// set can be filled in bit by bit.
    ///
    /// Create one with Create > Match3 > Sound Set.
    /// </summary>
    // An AudioClip is the sound data itself (like a Sprite is image data). It doesn't play on its
    // own: an AudioSource plays it, and the AudioListener (on the camera) "hears" it.
    [CreateAssetMenu(fileName = "SoundSet", menuName = "Match3/Sound Set")]
    public class SoundSet : ScriptableObject
    {
        [Tooltip("Stable identifier for code and save data, e.g. \"chimes\". Should not change once set.")]
        [SerializeField] private string id;
        [Tooltip("Human-readable name for UI.")]
        [SerializeField] private string displayName;

        [Header("Matches")]
        [SerializeField] private AudioClip match3;
        [SerializeField] private AudioClip match4;
        [Tooltip("A match of 5 or more tokens.")]
        [SerializeField] private AudioClip match5Plus;

        [Header("Round")]
        [Tooltip("Played once a second near the end of the round.")]
        [SerializeField] private AudioClip countdownTick;
        [SerializeField] private AudioClip roundEndHorn;

        [Header("Swaps")]
        [SerializeField] private AudioClip swap;
        [Tooltip("A swap that makes no match and swaps back.")]
        [SerializeField] private AudioClip invalidSwap;

        [Header("Achievements")]
        [Tooltip("Played with the \"Achievement unlocked\" toast.")]
        [SerializeField] private AudioClip achievementUnlocked;

        [Header("Power gems")]
        [Tooltip("A match leaves a Flame, Star or Hypercube behind.")]
        [SerializeField] private AudioClip powerGemCreated;
        [SerializeField] private AudioClip flameDetonate;
        [SerializeField] private AudioClip starDetonate;
        [SerializeField] private AudioClip hypercubeActivate;

        public string Id => id;
        public string DisplayName => displayName;

        /// <summary>The clip for <paramref name="sound"/>, or null if that slot is empty.</summary>
        public AudioClip GetClip(SoundId sound)
        {
            // A switch expression: picks one value per case, with `_` as the "anything else" case.
            return sound switch
            {
                SoundId.Match3 => match3,
                SoundId.Match4 => match4,
                SoundId.Match5Plus => match5Plus,
                SoundId.CountdownTick => countdownTick,
                SoundId.RoundEndHorn => roundEndHorn,
                SoundId.Swap => swap,
                SoundId.InvalidSwap => invalidSwap,
                SoundId.AchievementUnlocked => achievementUnlocked,
                SoundId.PowerGemCreated => powerGemCreated,
                SoundId.FlameDetonate => flameDetonate,
                SoundId.StarDetonate => starDetonate,
                SoundId.HypercubeActivate => hypercubeActivate,
                _ => null,
            };
        }
    }
}
