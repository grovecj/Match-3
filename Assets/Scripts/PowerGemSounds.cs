using System.Collections.Generic;
using UnityEngine;

namespace Match3
{
    /// <summary>
    /// Plays the power gem sounds: one when a match makes a power gem, and one per kind when a
    /// gem goes off (a Flame's blast, a Star's beam, a Hypercube's zap). Each wave of a chain
    /// reaction plays a little higher than the one before, the way <see cref="MatchSounds"/>
    /// raises each cascade, so a long chain audibly builds.
    ///
    /// Like MatchSounds it only listens to the <see cref="Board"/>'s events, and asks the
    /// <see cref="AudioManager"/> to play by <see cref="SoundId"/>.
    /// </summary>
    public class PowerGemSounds : MonoBehaviour
    {
        [SerializeField] private Board board;
        [SerializeField] private AudioManager audioManager;

        // Several gems of the same kind can go off in one wave (two Flames caught by one
        // Star, say). Their events all arrive in the same frame, and the same sound played two
        // or three times on top of itself is just louder and muddier. So each kind plays once
        // per frame: this remembers the last frame each kind was played in.
        private readonly Dictionary<PowerKind, int> lastFramePlayed =
            new Dictionary<PowerKind, int>();

        private void OnEnable()
        {
            if (board == null || audioManager == null)
            {
                Debug.LogError("PowerGemSounds: needs both a Board and an AudioManager assigned.", this);
                return;
            }
            board.PowerGemCreated += OnPowerGemCreated;
            board.PowerGemDetonated += OnPowerGemDetonated;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.PowerGemCreated -= OnPowerGemCreated;
                board.PowerGemDetonated -= OnPowerGemDetonated;
            }
        }

        private void OnPowerGemCreated(PowerKind kind, Vector2Int cell)
        {
            audioManager.Play(SoundId.PowerGemCreated);
        }

        private void OnPowerGemDetonated(PowerKind kind, Vector2Int cell, int waveIndex)
        {
            // TryGetValue is false (and leaves `frame` at 0) for a kind not played yet.
            if (lastFramePlayed.TryGetValue(kind, out int frame) && frame == Time.frameCount)
            {
                return;
            }
            lastFramePlayed[kind] = Time.frameCount;
            audioManager.Play(SoundForDetonation(kind), PitchForWave(waveIndex));
        }

        // Both mappings are `public static` so the Edit Mode tests can check them directly.

        /// <summary>The sound a gem of <paramref name="kind"/> makes when it goes off.</summary>
        public static SoundId SoundForDetonation(PowerKind kind)
        {
            return kind switch
            {
                PowerKind.Star => SoundId.StarDetonate,
                PowerKind.Hypercube => SoundId.HypercubeActivate,
                // A Flame, and anything new that hasn't got a sound of its own yet.
                _ => SoundId.FlameDetonate,
            };
        }

        /// <summary>
        /// The pitch for wave <paramref name="waveIndex"/> of a chain reaction: normal for the
        /// first, then a semitone higher per wave, capped like cascades are. The same curve as
        /// <see cref="MatchSounds.PitchForCascade"/>, so a chain climbs the way cascades do.
        /// </summary>
        public static float PitchForWave(int waveIndex)
        {
            return MatchSounds.PitchForCascade(waveIndex);
        }
    }
}
