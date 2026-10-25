using BlastPhysics.Damage;

namespace BlastPhysics.Fire;

/// <summary>Bâtiment pour la propagation du feu : centre, emprise et type de construction.</summary>
public readonly record struct FireSite(double X, double Z, double Length, double Width, StructureType Structure);

/// <summary>
/// Propagation des incendies de bâtiment en bâtiment, pilotée par les événements.
/// <para>
/// Un bâtiment en feu enflamme chaque voisin j après un délai tiré selon une loi
/// exponentielle de taux λ = λ0 · inflammabilité(j) · e^(−écart/L) · e^(k·U·cos θ) :
/// l'écart est la distance entre façades, L ≈ 5 m la portée du rayonnement et des brandons,
/// U la vitesse du vent et θ l'angle entre la direction de propagation et le vent. Si le
/// délai dépasse la durée de combustion de la source, le voisin n'est pas atteint par elle.
/// </para>
/// <para>
/// Ordres de grandeur visés : ~100-200 m/h en ville dense de bois sans vent, plusieurs fois
/// plus sous le vent (modèle de Hamada, retours d'expérience des incendies urbains japonais) ;
/// combustion d'1.5 à 2.5 h. L'aléa est déterministe (graine) pour des résultats reproductibles.
/// </para>
/// <para>
/// Le vent peut changer en cours d'incendie (<see cref="SetWind"/>) : les délais
/// exponentiels étant sans mémoire, on retire à partir de cet instant, avec le nouveau
/// vent, les délais de tous les voisins encore intacts des foyers actifs ; c'est
/// statistiquement exact. Les allumages directs (éclair) à venir sont conservés.
/// </para>
/// </summary>
public sealed class FireSpread
{
    /// <summary>Taux d'allumage d'un voisin accolé et très inflammable, par heure.</summary>
    public const double BaseRatePerHour = 4.0;
    /// <summary>Portée caractéristique du rayonnement et des brandons, m.</summary>
    public const double ReachM = 5.0;
    /// <summary>Écart au-delà duquel le feu ne saute plus d'un bâtiment à l'autre, m.</summary>
    public const double MaxGapM = 25.0;
    /// <summary>Effet du vent : facteur e^(k·U) sous le vent, U en m/s.</summary>
    public const double WindFactorPerMs = 0.15;

    private readonly FireSite[] _sites;
    // Voisins : indice, écart entre façades, direction unitaire (x, z) de la source vers eux.
    private readonly List<(int J, double Gap, double Dx, double Dz)>[] _neighbors;
    private readonly int _seed;
    private double _wind, _windX, _windZ;
    private int _epoch;
    private readonly double[] _scheduled, _primary, _ignited, _burnout;
    private readonly PriorityQueue<int, double> _ignitions = new();
    private readonly PriorityQueue<int, double> _burnouts = new();

    /// <param name="windTowardDeg">Direction vers laquelle souffle le vent (0 = nord, 90 = est).</param>
    public FireSpread(IReadOnlyList<FireSite> sites, double windSpeedMs, double windTowardDeg, int seed = 1)
    {
        _sites = sites.ToArray();
        _seed = seed;
        SetWindDirection(windSpeedMs, windTowardDeg);
        int n = _sites.Length;
        _scheduled = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
        _primary = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
        _ignited = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
        _burnout = Enumerable.Repeat(double.PositiveInfinity, n).ToArray();
        _neighbors = new List<(int, double, double, double)>[n];

        const double cell = 50.0;
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < n; i++)
        {
            var key = ((int)Math.Floor(_sites[i].X / cell), (int)Math.Floor(_sites[i].Z / cell));
            if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
            list.Add(i);
        }
        for (int i = 0; i < n; i++)
        {
            var s = _sites[i];
            var nb = new List<(int, double, double, double)>();
            int cx = (int)Math.Floor(s.X / cell), cz = (int)Math.Floor(s.Z / cell);
            for (int dz = -2; dz <= 2; dz++)
            for (int dx = -2; dx <= 2; dx++)
            {
                if (!grid.TryGetValue((cx + dx, cz + dz), out var list)) continue;
                foreach (int j in list)
                {
                    if (j == i) continue;
                    var o = _sites[j];
                    double ex = o.X - s.X, ez = o.Z - s.Z, d = Math.Sqrt(ex * ex + ez * ez);
                    double gap = Math.Max(0.0, d - HalfSize(s) - HalfSize(o));
                    if (gap > MaxGapM) continue;
                    nb.Add(d > 1e-6 ? (j, gap, ex / d, ez / d) : (j, gap, 0.0, 0.0));
                }
            }
            _neighbors[i] = nb;
        }
    }

    public int Count => _sites.Length;
    public double IgnitionTime(int i) => _ignited[i];
    public double BurnoutTime(int i) => _burnout[i];
    public bool IsBurning(int i, double timeS) => _ignited[i] <= timeS && timeS < _burnout[i];

    /// <summary>Durée de combustion d'un bâtiment (s).</summary>
    public static double BurnDurationS(StructureType type) => 3600.0 * type switch
    {
        StructureType.WoodFrame => 1.5,
        StructureType.UnreinforcedMasonry => 2.5,
        StructureType.ReinforcedConcrete => 2.0,
        _ => 1.5,
    };

    /// <summary>Inflammabilité relative : facilité à être allumé par un voisin.</summary>
    public static double Flammability(StructureType type) => type switch
    {
        StructureType.WoodFrame => 1.0,
        StructureType.UnreinforcedMasonry => 0.3,
        StructureType.SteelFrame => 0.1,
        _ => 0.08,
    };

    /// <summary>Taux d'allumage (par heure) d'un voisin, selon l'écart et le vent.</summary>
    public double RatePerHour(StructureType target, double gapM, double cosToWind)
        => BaseRatePerHour * Flammability(target) * Math.Exp(-gapM / ReachM)
           * Math.Exp(WindFactorPerMs * _wind * cosToWind);

    /// <summary>Allume un bâtiment au temps donné (allumage initial par l'éclair, par exemple).</summary>
    public void Ignite(int i, double timeS)
    {
        _primary[i] = Math.Min(_primary[i], timeS);
        if (timeS >= _scheduled[i]) return;
        _scheduled[i] = timeS;
        _ignitions.Enqueue(i, timeS);
    }

    /// <summary>
    /// Change le vent à l'instant donné : les allumages à venir sont retirés avec le nouveau
    /// vent depuis les foyers actifs (les délais exponentiels n'ont pas de mémoire).
    /// </summary>
    public void SetWind(double windSpeedMs, double windTowardDeg, double nowS)
    {
        SetWindDirection(windSpeedMs, windTowardDeg);
        _epoch++;
        // Repart des seuls allumages directs encore à venir…
        _ignitions.Clear();
        for (int j = 0; j < _sites.Length; j++)
        {
            if (!double.IsPositiveInfinity(_ignited[j])) continue;
            _scheduled[j] = _primary[j] > nowS ? _primary[j] : double.PositiveInfinity;
            if (!double.IsPositiveInfinity(_scheduled[j])) _ignitions.Enqueue(j, _scheduled[j]);
        }
        // …puis chaque foyer actif rallume ses voisins intacts à partir de maintenant.
        for (int i = 0; i < _sites.Length; i++)
            if (_ignited[i] <= nowS && nowS < _burnout[i])
                ScheduleNeighbors(i, nowS, _burnout[i]);
    }

    /// <summary>
    /// Traite tous les événements jusqu'au temps t : renvoie les bâtiments qui viennent de
    /// prendre feu et ceux qui viennent de s'éteindre.
    /// </summary>
    public void Advance(double timeS, List<int> ignited, List<int> burnedOut)
    {
        while (_ignitions.TryPeek(out int i, out double ti) && ti <= timeS)
        {
            _ignitions.Dequeue();
            if (!double.IsPositiveInfinity(_ignited[i]) || ti > _scheduled[i]) continue; // déjà traité
            _ignited[i] = ti;
            double end = ti + BurnDurationS(_sites[i].Structure);
            _burnout[i] = end;
            _burnouts.Enqueue(i, end);
            ignited.Add(i);
            ScheduleNeighbors(i, ti, end);
        }
        while (_burnouts.TryPeek(out int b, out double tb) && tb <= timeS)
        {
            _burnouts.Dequeue();
            burnedOut.Add(b);
        }
    }

    private void SetWindDirection(double windSpeedMs, double windTowardDeg)
    {
        _wind = Math.Max(windSpeedMs, 0.0);
        double a = windTowardDeg * Math.PI / 180.0;
        _windX = Math.Sin(a);   // est = +X
        _windZ = -Math.Cos(a);  // nord = −Z
    }

    // Tire le délai d'allumage de chaque voisin intact à partir de « from », tant que la
    // source brûle encore.
    private void ScheduleNeighbors(int i, double from, double end)
    {
        foreach (var (j, gap, dx, dz) in _neighbors[i])
        {
            if (!double.IsPositiveInfinity(_ignited[j])) continue;
            double rate = RatePerHour(_sites[j].Structure, gap, dx * _windX + dz * _windZ) / 3600.0;
            double tj = from - Math.Log(Math.Max(Hash(i, j), 1e-12)) / rate;
            if (tj < end && tj < _scheduled[j])
            {
                _scheduled[j] = tj;
                _ignitions.Enqueue(j, tj);
            }
        }
    }

    private static double HalfSize(FireSite s) => 0.5 * Math.Sqrt(Math.Max(s.Length * s.Width, 1.0));

    private double Hash(int i, int j)
    {
        // L'époque change à chaque changement de vent : nouveaux tirages indépendants.
        uint h = (uint)i * 2654435761u ^ (uint)j * 2246822519u ^ (uint)_seed * 3266489917u ^ (uint)_epoch * 668265263u;
        h ^= h >> 15; h *= 2246822519u; h ^= h >> 13; h *= 3266489917u; h ^= h >> 16;
        return (h + 0.5) / 4294967296.0;
    }
}
