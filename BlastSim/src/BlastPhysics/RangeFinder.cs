namespace BlastPhysics;

/// <summary>Recherche de rayon d'effet sur des profils radiaux pas forcément monotones.</summary>
public static class RangeFinder
{
    /// <summary>
    /// Plus grande distance r ∈ [minRange, maxRange] telle que f(r) ≥ seuil.
    /// Balaye depuis l'extérieur sur une grille logarithmique, puis affine par
    /// dichotomie. Retourne 0 si le seuil n'est atteint nulle part.
    /// </summary>
    public static double OuterRange(Func<double, double> f, double threshold,
                                    double maxRangeM, double minRangeM = 0.1, int samples = 400)
    {
        if (maxRangeM <= minRangeM) return 0.0;
        double lMin = Math.Log(minRangeM), lMax = Math.Log(maxRangeM);
        double prevR = maxRangeM;
        if (f(prevR) >= threshold) return maxRangeM;
        for (int i = samples - 1; i >= 0; i--)
        {
            double r = Math.Exp(lMin + (lMax - lMin) * i / samples);
            if (f(r) >= threshold)
                return Bisect(f, threshold, inside: r, outside: prevR);
            prevR = r;
        }
        return 0.0;
    }

    private static double Bisect(Func<double, double> f, double threshold, double inside, double outside)
    {
        for (int k = 0; k < 60 && outside - inside > 1e-3 * inside; k++)
        {
            double mid = 0.5 * (inside + outside);
            if (f(mid) >= threshold) inside = mid; else outside = mid;
        }
        return 0.5 * (inside + outside);
    }
}
