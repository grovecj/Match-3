using UnityEngine;

namespace Match3
{
    /// <summary>
    /// A short one-shot particle effect that can be used over and over: a power gem's blast,
    /// the flash of a new gem, the spark where a Hypercube's bolt lands. Lives on the prefabs
    /// built by Match3 > Build Power Gem Effects.
    ///
    /// Unlike the <see cref="Explosion"/>, which destroys itself when it's done, this one says
    /// so through <see cref="Finished"/> and stays alive, so whoever played it can put it back
    /// in a pool and play it again later (see <see cref="PowerGemEffects"/>). A board-clearing
    /// chain can set off dozens of these in a second; reusing a handful of objects is much
    /// cheaper than creating and destroying dozens, especially in the Web build.
    /// </summary>
    // A ParticleSystem's Stop Action says what happens once it has finished playing and its
    // last particle has died. "Destroy" removes the GameObject (that's what the Explosion
    // uses). "Callback", used here, instead calls OnParticleSystemStopped on the scripts of
    // that GameObject, and leaves the rest to us.
    [RequireComponent(typeof(ParticleSystem))]
    public class ParticleEffect : MonoBehaviour
    {
        [Tooltip("An optional second system (a soft glow behind the particles, say) that plays along with this one. Its colour is set to white mixed with the effect's colour.")]
        [SerializeField] private ParticleSystem glow;

        private ParticleSystem particles;

        /// <summary>Fired once each time the effect has finished playing.</summary>
        public event System.Action<ParticleEffect> Finished;

        private void Awake()
        {
            particles = GetComponent<ParticleSystem>();
        }

        /// <summary>
        /// Tints the effect <paramref name="color"/> and plays it from the start. Every particle
        /// gets a random colour between that and a paler version of it, so it sparkles.
        /// </summary>
        public void Play(Color color)
        {
            // The modules are structs that write straight through to the system (see Explosion).
            ParticleSystem.MainModule main = particles.main;
            main.startColor = new ParticleSystem.MinMaxGradient(color, Color.Lerp(color, Color.white, 0.5f));
            if (glow != null)
            {
                ParticleSystem.MainModule glowMain = glow.main;
                Color glowColor = Color.Lerp(color, Color.white, 0.6f);
                // Keep the alpha the prefab gave the glow: it's meant to be see-through.
                glowColor.a = glowMain.startColor.color.a;
                glowMain.startColor = glowColor;
            }

            // Clear first: a pooled effect may be reused the moment it finished, and must
            // start empty rather than carry over anything from its last play.
            particles.Clear(withChildren: true);
            particles.Play(withChildren: true);
        }

        // Unity calls this (because the prefab's Stop Action is Callback) once the system has
        // stopped and its last particle has died.
        private void OnParticleSystemStopped()
        {
            Finished?.Invoke(this);
        }
    }
}
