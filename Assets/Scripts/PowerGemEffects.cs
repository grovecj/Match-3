using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Match3
{
    /// <summary>
    /// Draws everything power gems do, so each kind is recognisable at a glance:
    /// <list type="bullet">
    /// <item>A new gem: a flash in its cell as it appears.</item>
    /// <item>A Flame: a fast burst of embers over the 3x3 it clears.</item>
    /// <item>A Star: a burst on the gem and beams that race along its row and column (<see cref="StarBeam"/>).</item>
    /// <item>A Hypercube: a crackling arc to each gem of the colour (<see cref="HypercubeZap"/>), with a spark on each as it's hit.</item>
    /// </list>
    ///
    /// It only listens to the <see cref="Board"/>'s events (<see cref="Board.PowerGemCreated"/>
    /// and <see cref="Board.PowerGemBlastStarted"/>); the Board knows nothing about particles.
    /// Every blast arrives with the delay at which each of its cells is hit, which is when the
    /// Board pops that cell, so waiting the same delay here keeps the pictures and the pops in step.
    ///
    /// The particle effects are pooled (see <see cref="ObjectPool{T}"/>): each prefab gets a few
    /// copies that are switched off when they finish and switched back on for the next blast,
    /// instead of being created and destroyed every time. A Hypercube's bolts are pooled the
    /// same way; they have no prefab, so their pool builds them in code.
    /// </summary>
    public class PowerGemEffects : MonoBehaviour
    {
        [SerializeField] private Board board;

        [Header("Particle prefabs (Match3 > Build Power Gem Effects)")]
        [Tooltip("Played in a new power gem's cell as it appears.")]
        [SerializeField] private ParticleEffect gemCreatedPrefab;
        [Tooltip("A Flame going off. Built to cover a 3x3 at a cell size of 1.")]
        [SerializeField] private ParticleEffect flameBlastPrefab;
        [Tooltip("Played on a Star as its beams set off.")]
        [SerializeField] private ParticleEffect starBurstPrefab;
        [Tooltip("Played on a Hypercube as it goes off, and on each gem as a bolt hits it.")]
        [SerializeField] private ParticleEffect zapSparkPrefab;

        /// <summary>How much white is mixed into a gem's colour for its beam or bolts (0 = none, 1 = all white).</summary>
        public const float BeamWhiteness = 0.6f;

        // Beams and bolts are drawn this far in front of the tokens: above a picked-up token and
        // its overlay, so nothing on the board covers them. (The particle prefabs set their own
        // order, further forward still.)
        private const int BeamOrderOffset = 10;

        // At most this many copies of one effect are kept when they finish. A huge chain can
        // need more at once; the extras are created as usual and destroyed when done.
        private const int MaxPooledPerEffect = 24;

        // One pool per prefab. A Dictionary looks a pool up by its prefab.
        private readonly Dictionary<ParticleEffect, ObjectPool<ParticleEffect>> pools =
            new Dictionary<ParticleEffect, ObjectPool<ParticleEffect>>();

        // The Hypercube's bolts. Made the first time one is needed (see ZapPool).
        private ObjectPool<HypercubeZap> zapPool;

        // Each bolt gets its own seed, so no two take the same crooked path.
        private int nextZapSeed;

        private void OnEnable()
        {
            if (board == null)
            {
                Debug.LogError("PowerGemEffects: needs a Board assigned.", this);
                return;
            }
            board.PowerGemCreated += OnPowerGemCreated;
            board.PowerGemBlastStarted += OnBlastStarted;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.PowerGemCreated -= OnPowerGemCreated;
                board.PowerGemBlastStarted -= OnBlastStarted;
            }
        }

        private void OnPowerGemCreated(PowerKind kind, Vector2Int cell)
        {
            // The new gem's token is in its cell now. A Hypercube has no colour (Type is null),
            // which GemColorFor turns into white.
            Token token = board.GetToken(cell.x, cell.y);
            Color color = board.GemColorFor(token != null ? token.Type : null);
            // The matched gems slide into the cell first; the flash goes off as the gem appears.
            StartCoroutine(PlayAfter(board.CollapseDuration, gemCreatedPrefab, board.GridToWorld(cell.x, cell.y), color));
        }

        private void OnBlastStarted(PowerGemBlast blast)
        {
            Color color = board.GemColorFor(blast.Colour);
            Vector3 origin = board.GridToWorld(blast.Cell.x, blast.Cell.y);
            switch (blast.Kind)
            {
                case PowerKind.Flame:
                    PlayEffect(flameBlastPrefab, origin, color);
                    break;
                case PowerKind.Star:
                    PlayEffect(starBurstPrefab, origin, color);
                    FireBeam(blast.Cell, Color.Lerp(color, Color.white, BeamWhiteness));
                    break;
                case PowerKind.Hypercube:
                    PlayEffect(zapSparkPrefab, origin, color);
                    FireBolts(blast, origin, Color.Lerp(color, Color.white, BeamWhiteness), color);
                    break;
            }
        }

        /// <summary>
        /// Fires a Star's beam from <paramref name="cell"/> to the four edges of the board. The
        /// beam travels at the Board's beam speed, so it reaches each gem as that gem pops.
        /// </summary>
        private void FireBeam(Vector2Int cell, Color color)
        {
            // No prefab: the beam is simple enough to build in code. Parented here to keep the
            // Hierarchy tidy; it destroys itself when it's done.
            var beamObject = new GameObject("Star Beam");
            beamObject.transform.SetParent(transform, false);
            beamObject.transform.position = board.GridToWorld(cell.x, cell.y);
            StarBeam beam = beamObject.AddComponent<StarBeam>();

            SpriteRenderer tokenRenderer = board.TokenRenderer;
            int order = (tokenRenderer != null ? tokenRenderer.sortingOrder : 0) + BeamOrderOffset;
            // Each arm starts in the middle of the gem's cell, so it has half a cell to cross
            // before it even leaves that cell, then one whole cell for each cell beyond it.
            beam.Play(color, board.CellSize, board.BeamSpeed,
                reachRight: board.Width - cell.x - 0.5f,
                reachUp: board.Height - cell.y - 0.5f,
                reachLeft: cell.x + 0.5f,
                reachDown: cell.y + 0.5f,
                drawLike: tokenRenderer,
                sortingOrder: order);
        }

        /// <summary>
        /// One bolt from the Hypercube to each gem it destroys, each lit at that gem's delay,
        /// and a spark on the gem at the same moment.
        /// </summary>
        private void FireBolts(PowerGemBlast blast, Vector3 origin, Color boltColor, Color sparkColor)
        {
            SpriteRenderer tokenRenderer = board.TokenRenderer;
            int order = (tokenRenderer != null ? tokenRenderer.sortingOrder : 0) + BeamOrderOffset;
            for (int i = 0; i < blast.Blasted.Count; i++)
            {
                Vector2Int target = blast.Blasted[i];
                Vector3 targetPosition = board.GridToWorld(target.x, target.y);
                float delay = blast.Delays[i];

                HypercubeZap zap = ZapPool().Get();
                zap.Play(origin, targetPosition, boltColor, delay, board.CellSize, tokenRenderer, order, nextZapSeed++);

                StartCoroutine(PlayAfter(delay, zapSparkPrefab, targetPosition, sparkColor));
            }
        }

        /// <summary>Waits <paramref name="delay"/> seconds (game time, so pausing waits too), then plays the effect.</summary>
        private IEnumerator PlayAfter(float delay, ParticleEffect prefab, Vector3 position, Color color)
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }
            PlayEffect(prefab, position, color);
        }

        /// <summary>
        /// Plays a copy of <paramref name="prefab"/> at <paramref name="position"/>, scaled to the
        /// board's cells and tinted <paramref name="color"/>. Does nothing if the prefab hasn't
        /// been built and assigned yet.
        /// </summary>
        private void PlayEffect(ParticleEffect prefab, Vector3 position, Color color)
        {
            if (prefab == null)
            {
                return;
            }
            ObjectPool<ParticleEffect> pool = PoolFor(prefab);
            // Get hands back a finished copy if there is one, or makes a new one if not.
            ParticleEffect effect = pool.Get();
            effect.transform.position = position;
            // The prefabs are built for a 1-unit cell; scaling one scales the whole effect.
            effect.transform.localScale = Vector3.one * board.CellSize;
            effect.Play(color);
        }

        /// <summary>The pool of copies of <paramref name="prefab"/>, made the first time it's needed.</summary>
        private ObjectPool<ParticleEffect> PoolFor(ParticleEffect prefab)
        {
            if (pools.TryGetValue(prefab, out ObjectPool<ParticleEffect> pool))
            {
                return pool;
            }

            // An ObjectPool is told how to make, hand out, take back and throw away its objects,
            // as four little functions (lambdas). It does the bookkeeping.
            pool = new ObjectPool<ParticleEffect>(
                createFunc: () =>
                {
                    ParticleEffect effect = Instantiate(prefab, transform);
                    // When it finishes, it goes back into this pool. `pool` is the variable
                    // above: the lambda "captures" it, so it's the right pool for this prefab.
                    effect.Finished += finished => pool.Release(finished);
                    return effect;
                },
                actionOnGet: effect => effect.gameObject.SetActive(true),
                // A switched-off GameObject costs almost nothing: no Update, nothing drawn.
                actionOnRelease: effect => effect.gameObject.SetActive(false),
                actionOnDestroy: effect => Destroy(effect.gameObject),
                // collectionCheck would catch releasing the same object twice, at a cost on
                // every release. Each effect only releases itself, once per play, so skip it.
                collectionCheck: false,
                defaultCapacity: 8,
                maxSize: MaxPooledPerEffect);
            pools.Add(prefab, pool);
            return pool;
        }

        /// <summary>The pool of Hypercube bolts, made the first time it's needed.</summary>
        private ObjectPool<HypercubeZap> ZapPool()
        {
            if (zapPool != null)
            {
                return zapPool;
            }

            // The same four functions as PoolFor. The only difference is "make": there's no
            // prefab to copy, so a new bolt is an empty GameObject with a HypercubeZap on it
            // (which adds its own segments the first time it plays, and keeps them).
            zapPool = new ObjectPool<HypercubeZap>(
                createFunc: () =>
                {
                    var zapObject = new GameObject("Hypercube Zap");
                    zapObject.transform.SetParent(transform, false);
                    HypercubeZap zap = zapObject.AddComponent<HypercubeZap>();
                    zap.Finished += finished => zapPool.Release(finished);
                    return zap;
                },
                actionOnGet: zap => zap.gameObject.SetActive(true),
                actionOnRelease: zap => zap.gameObject.SetActive(false),
                actionOnDestroy: zap => Destroy(zap.gameObject),
                collectionCheck: false,
                defaultCapacity: 16,
                maxSize: MaxPooledPerEffect);
            return zapPool;
        }

        // ---- Test hooks -------------------------------------------------------------------

        /// <summary>How many finished copies of <paramref name="prefab"/> are waiting in its pool.</summary>
        internal int PooledCountForTests(ParticleEffect prefab)
        {
            return pools.TryGetValue(prefab, out ObjectPool<ParticleEffect> pool) ? pool.CountInactive : 0;
        }

        /// <summary>How many finished Hypercube bolts are waiting in their pool.</summary>
        internal int PooledZapCountForTests => zapPool != null ? zapPool.CountInactive : 0;

        /// <summary>Sets what the scene would assign. Call it while the GameObject is still inactive.</summary>
        internal void ConfigureForTests(Board board, ParticleEffect gemCreated, ParticleEffect flameBlast,
            ParticleEffect starBurst, ParticleEffect zapSpark)
        {
            this.board = board;
            gemCreatedPrefab = gemCreated;
            flameBlastPrefab = flameBlast;
            starBurstPrefab = starBurst;
            zapSparkPrefab = zapSpark;
        }
    }
}
