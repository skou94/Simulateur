namespace BlastPhysics.Blast;

/// <summary>
/// Interaction de l'onde avec un sol idéal pour une explosion à hauteur quelconque :
/// réflexion régulière près du point zéro, tige de Mach plus loin, et zone de
/// transition entre les deux. Modèle de Speicher &amp; Brode / Carpenter repris dans
/// BLAST.EXE (DNA), portage d'après « glasstone » (MIT).
/// Fiabilité annoncée : ±15 % sur les distances, jusqu'à ±30 % sur la pression
/// selon la hauteur d'explosion.
/// Toutes les longueurs sont réduites à 1 kt (m/kt^(1/3)), au niveau de la mer.
/// </summary>
public static class GroundReflection
{
    public static readonly double Cbrt2 = Math.Cbrt(2.0);

    // Évite log(0) et les divisions par zéro exactement sous le point d'explosion.
    private const double MinScaledGroundRange = 1e-3;

    /// <summary>Surpression de crête au sol, Pa (niveau de la mer, 1 kt).</summary>
    public static double PeakOverpressure(FreeAirCurve curve, double sgr, double shob)
    {
        sgr = Math.Max(sgr, MinScaledGroundRange);
        double sigma = RegularToMachSwitch(curve, sgr, shob);
        if (sigma >= 1.0) return RegularReflection(curve, sgr, shob);
        if (sigma <= 0.0) return MachReflection(curve, sgr, shob);
        return sigma * RegularReflection(curve, sgr, shob) + (1.0 - sigma) * MachReflection(curve, sgr, shob);
    }

    /// <summary>Pression dynamique de crête au sol, Pa (composante horizontale en zone régulière).</summary>
    public static double PeakDynamicPressure(FreeAirCurve curve, double sgr, double shob)
    {
        sgr = Math.Max(sgr, MinScaledGroundRange);
        double p = PeakOverpressure(curve, sgr, shob);
        double sigma = RegularToMachSwitch(curve, sgr, shob);
        double alpha = Math.Atan(shob / sgr);
        double n = ShockRelations.DensityRatio(p);
        return 0.5 * p * (n - 1.0) * (1.0 - sigma * Sq(Math.Sin(alpha)));
    }

    /// <summary>Temps d'arrivée au sol, s/kt^(1/3) (correction de la tige de Mach comprise).</summary>
    public static double ArrivalTime(FreeAirCurve curve, double sgr, double shob)
    {
        double xm = MachStemFormationRange(shob);
        double v = sgr <= xm ? 1.0 : 1.26 - 0.26 * (xm / sgr);
        double slant = Math.Sqrt(sgr * sgr + shob * shob);
        return curve.ArrivalTime(slant / v) * v;
    }

    /// <summary>Distance réduite au sol où se forme la tige de Mach.</summary>
    public static double MachStemFormationRange(double shob)
        => Math.Pow(shob, 2.5) / 5822.0 + 2.09 * Math.Pow(shob, 0.75);

    // Poids de la réflexion régulière : 1 près du point zéro, 0 en zone de Mach.
    // Correction par rapport à BLAST.EXE : la tige de Mach n'existe qu'au-delà de sa
    // distance de formation x_m. Sans cette borne, la formule de Mach (qui dépend de la
    // distance au sol) garde un poids non nul sous le point d'explosion et diverge.
    // Raccord progressif entre x_m et 1.5·x_m.
    private static double RegularToMachSwitch(FreeAirCurve curve, double sgr, double shob)
    {
        double sigma = DnaSwitch(curve, sgr, shob);
        double xm = MachStemFormationRange(shob);
        if (xm <= 0) return sigma;
        double machAllowed = Math.Clamp((sgr - xm) / (0.5 * xm), 0.0, 1.0);
        machAllowed = machAllowed * machAllowed * (3.0 - 2.0 * machAllowed); // smoothstep
        return 1.0 - (1.0 - sigma) * machAllowed;
    }

    private static double DnaSwitch(FreeAirCurve curve, double sgr, double shob)
    {
        double pFree = curve.Overpressure(Math.Sqrt(sgr * sgr + shob * shob));
        double t = 340.0 / Math.Pow(pFree, 0.55);
        double u = 1.0 / (7782.0 * Math.Pow(pFree, 0.7) + 0.9);
        double mergeAngle = Math.Atan(1.0 / (t + u));
        double w = 1.0 / (7473.0 * Math.Sqrt(pFree) + 6.6);
        double v = 1.0 / (647.0 * Math.Pow(pFree, 0.8) + w);
        double width = Math.Atan(1.0 / (t + v));
        double alpha = Math.Atan(shob / sgr);
        double s = Math.Clamp((alpha - mergeAngle) / width, -1.0, 1.0);
        return 0.5 * (Math.Sin(0.5 * Math.PI * s) + 1.0);
    }

    // Tige de Mach : onde incidente d'une charge doublée (hémisphère), corrigée selon l'angle.
    private static double MachReflection(FreeAirCurve curve, double sgr, double shob)
    {
        double alpha = Math.Atan(shob / sgr);
        double a = Math.Min(3.7 - 0.94 * Math.Log(sgr), 0.7);
        double b = 0.77 * Math.Log(sgr) - 3.8 - 18.0 / sgr;
        double c = Math.Max(a, b);
        return curve.Overpressure(sgr / Cbrt2) / (1.0 - c * Math.Sin(alpha));
    }

    // Réflexion régulière : de 2× (incidence rasante) à Pr/Ps normal (sous le point zéro).
    private static double RegularReflection(FreeAirCurve curve, double sgr, double shob)
    {
        double pFree = curve.Overpressure(Math.Sqrt(sgr * sgr + shob * shob));
        double alpha = Math.Atan(shob / sgr);
        double rn = ShockRelations.NormalReflectionFactor(pFree);
        double f = pFree / 75842.0;
        double f6 = Math.Pow(f, 6);
        double d = f6 * (1.2 + 0.07 * Math.Sqrt(f)) / (f6 + 1.0);
        return pFree * ((rn - 2.0) * Math.Pow(Math.Sin(alpha), d) + 2.0);
    }

    private static double Sq(double v) => v * v;
}
