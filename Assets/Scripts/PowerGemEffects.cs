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
    /// <item>A combo: a Flame burst scaled up to the 5x5, or a Star burst with three beams per axis.</item>
    /// <item>A Hypercube + Flame or Star: a new-gem flash on every gem the cube turns into a power gem, then the usual bolts.</item>
    /// <item>Two Hypercubes: a spark on every cell, spreading out from the moved cube in rings.</item>
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

        /// <summary>
        /// How much bigger a <see cref="PowerCombo.BigFlame"/>'s burst is than a Flame's: the
        /// prefab covers a 3x3, and the combo clears a 5x5.
        /// </summary>
        public const float BigFlameScale = 5f / 3f;

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
            board.PowerGemConverted += OnPowerGemConverted;
            board.PowerGemBlastStarted += OnBlastStarted;
        }

        private void OnDisable()
        {
            if (board != null)
            {
                board.PowerGemCreated -= OnPowerGemCreated;
                board.PowerGemConverted -= OnPowerGemConverted;
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

        private void OnPowerGemConverted(PowerKind kind, Vector2Int cell)
        {
            // The same flash as a new gem, but straight away: nothing slides in first. The token
            // already shows its new power, so the flash is in its (unchanged) colour.
            Token token = board.GetToken(cell.x, cell.y);
            Color color = board.GemColorFor(token != null ? token.Type : null);
            PlayEffect(gemCreatedPrefab, board.GridToWorld(cell.x, cell.y), color);
        }

        private void OnBlastStarted(PowerGemBlast blast)
        {
            Color color = board.GemColorFor(blast.Colour);
            Vector3 origin = board.GridToWorld(blast.Cell.x, blast.Cell.y);

            // A combo is drawn with the same prefabs as a single gem, just more of them or
            // bigger: the moved gem draws the whole combined blast...
            switch (blast.Combo)
            {
                case PowerCombo.BigFlame:
                    PlayEffect(flameBlastPrefab, origin, color, BigFlameScale);
                    return;
                case PowerCombo.StarCross:
                    PlayEffect(starBurstPrefab, origin, color);
                    FireCrossBeams(blast.Cell, Color.Lerp(color, Color.white, BeamWhiteness));
                    return;
                case PowerCombo.ClearBoard:
                    // No bolts: dozens of them at once would be a tangle (and a lot to draw on a
                    // phone). A spark on each gem as its ring reaches it reads as one wave.
                    PlayEffect(zapSparkPrefab, origin, color);
                    for (int i = 0; i < blast.Blasted.Count; i++)
                    {
                        Vector2Int cell = blast.Blasted[i];
                        StartCoroutine(PlayAfter(blast.Delays[i], zapSparkPrefab, board.GridToWorld(cell.x, cell.y), color));
                    }
                    return;
                // A Conversion's cube is drawn like any Hypercube (below): the conversion itself
                // was shown by OnPowerGemConverted, and its gems' blasts come in the next wave.
            }

            // ...and its partner only flares in its own cell: its blast is part of that one.
            if (blast.IsComboPartner)
            {
                ParticleEffect flare = blast.Kind == PowerKind.Star ? starBurstPrefab
                    : blast.Kind == PowerKind.Hypercube ? zapSparkPrefab
                    : flameBlastPrefab;
                PlayEffect(flare, origin, color);
                return;
            }

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
        private void FireBeam(Vector2Int cell, Color color, bool alongRow = true, bool alongColumn = true)
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
            // before it even leaves that cell, then one whole cell for each cell beyond it. An
            // arm that isn't wanted is given no reach at all, so it never grows.
            beam.Play(color, board.CellSize, board.BeamSpeed,
                reachRight: alongRow ? board.Width - cell.x - 0.5f : 0f,
                reachUp: alongColumn ? board.Height - cell.y - 0.5f : 0f,
                reachLeft: alongRow ? cell.x + 0.5f : 0f,
                reachDown: alongColumn ? cell.y + 0.5f : 0f,
                drawLike: tokenRenderer,
                sortingOrder: order);
        }

        /// <summary>
        /// A <see cref="PowerCombo.StarCross"/>'s beams: three along rows and three along
        /// columns, through the 3x3 around <paramref name="centre"/>. Each row's beam starts in
        /// the centre's column and each column's beam in the centre's row, which is where
        /// <see cref="Board.BlastDelay"/> measures from. A row or column off the board is skipped.
        /// </summary>
        private void FireCrossBeams(Vector2Int centre, Color color)
        {
            for (int offset = -1; offset <= 1; offset++)
            {
                int row = centre.y + offset;
                if (row >= 0 && row < board.Height)
                {
                    FireBeam(new Vector2Int(centre.x, row), color, alongRow: true, alongColumn: false);
                }

                int column = centre.x + offset;
                if (column >= 0 && column < board.Width)
                {
                    FireBeam(new Vector2Int(column, centre.y), color, alongRow: false, alongColumn: true);
                }
            }
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
        /// <param name="scale">
        /// How much bigger than one cell's worth to draw it: 1 for the effect as built, more for
        /// a combo's bigger blast. Set again every time, so a pooled copy never keeps an old size.
        /// </param>
        private void PlayEffect(ParticleEffect prefab, Vector3 position, Color color, float scale = 1f)
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
            effect.transform.localScale = Vector3.one * (board.CellSize * scale);
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
