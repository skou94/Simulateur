# BlastSim — moteur physique

Bibliothèque C# (.NET 8) qui calcule les effets d'une explosion conventionnelle ou
nucléaire en tout point du sol. Elle ne dépend d'aucun moteur graphique : le rendu
Godot 4 viendra s'y brancher.

```
BlastSim/
├── src/BlastPhysics/          bibliothèque
│   ├── Atmosphere.cs          atmosphère standard ISA, échelle de Sachs
│   ├── Blast/                 souffle : courbes en air libre, réflexion au sol, Rankine-Hugoniot
│   ├── Nuclear/               boule de feu, rayonnement thermique, radiation initiale
│   ├── Conventional/          explosifs (équivalent TNT), fragmentation
│   ├── Damage/                vulnérabilité des bâtiments
│   ├── Casualties/            estimation des victimes (population, morts, blessés)
│   ├── Detonation.cs          point d'entrée : effets en un point, rayons d'effet
│   └── Scenarios.cs           Hiroshima, Nagasaki, Beyrouth, MOAB, Tsar Bomba, Castle Bravo…
├── src/CityData/              villes réelles OpenStreetMap : Overpass, rectangles orientés, carte au sol
└── tests/BlastPhysics.Tests/  cohérence physique et validation historique
```

## Application Godot

Le dossier `godot/` contient le simulateur 3D (Godot 4.7, édition .NET). Ouvrir
`godot/project.godot` dans Godot puis lancer (F5), ou en ligne de commande :

```
Godot_v4.7.2-stable_mono_win64.exe --path godot
Godot_v4.7.2-stable_mono_win64.exe --path godot -- --demo --scenario=5 --time-scale=3
```

Options après `--` : `--demo` (détone après 1 s), `--scenario=N` (rang dans
`Scenarios.All`, 1 = grenade), `--time-scale=x`, `--camera=distance,lacet,tangage`.

Tous les effets affichés viennent de la bibliothèque : le front de poussière suit le
temps d'arrivée calculé, chaque bâtiment reçoit sa surpression et son impulsion au
moment où l'onde l'atteint, son état est tiré selon les probabilités du modèle de
vulnérabilité, et les incendies se déclarent selon la fluence thermique reçue.

## Bibliothèque : utilisation

```csharp
var hiroshima = Scenarios.Hiroshima();
EffectsAtPoint e = hiroshima.At(groundRangeM: 1600);
// e.Blast.OverpressurePa, e.Blast.ArrivalTimeS, e.ThermalFluenceCalPerCm2, e.InitialRadiationRad…

var damage = BuildingDamage.Assess(StructureType.UnreinforcedMasonry,
                                   e.Blast.OverpressurePa, e.Blast.ImpulsePaS);
// damage.PCollapse, damage.ExpectedDamageRatio
```

```
dotnet test        # lance la suite de tests
```

## Modèles et sources

| Effet | Modèle | Source | Précision indicative |
|---|---|---|---|
| Souffle nucléaire en air libre | Courbe standard DNA 1 kt | BLAST.EXE / DNA EM-1 | ±4 % (pire 11 %) |
| Souffle TNT en air libre | Kinney & Graham | *Explosive Shocks in Air*, 1985 | ±10-20 % |
| Hauteur d'explosion | Réflexion régulière / tige de Mach | Speicher & Brode, Carpenter (DNA) | ±15 % en distance, ±30 % en pression |
| Altitude | Échelle de Sachs sur l'atmosphère ISA | ISO 2533 | — |
| Front de choc, pression dynamique | Rankine-Hugoniot, gamma effectif | Speicher & Brode | — |
| Boule de feu | R ≈ 55·W^0.4 m ; croissance de Taylor-Sedov | Glasstone & Dolan §2.03 ; Taylor 1950 | ±15 % |
| Rayonnement thermique | Q = f·E·τ/4πD², transmittance de Koschmieder | Glasstone & Dolan §7.93 | ±25 % |
| Seuils de brûlure | ~W^0.07, 1er/2e/3e degré | Glasstone & Dolan tableau 7.42 (approché) | ±20 % |
| Radiation initiale | Courbes 1 kt neutrons + gammas, augmentation hydrodynamique | Glasstone & Dolan §8.60-8.83 | facteur ~2 |
| Éclats | Gurney + freinage, critères 79 J et 1/55.7 m² | DoD 6055.09 | ±30 % |
| Dégâts aux bâtiments | Hyperboles pression-impulsion + fragilité lognormale | Baker et al. 1983 ; HAZUS | **seuils indicatifs** |
| Cratère | R ≈ R1·W^(1/3.4) (nucléaire), D ≈ 0.8·W^(1/3) (chimique), réduit avec la hauteur | Glasstone & Dolan §6.70 ; Ambrosini et al. 2002 | facteur ~2 |
| Victimes | Risques indépendants (état du bâtiment, incendie, souffle, brûlures, radiation, éclats), DL50 450 rad | HAZUS-MH ; Glasstone ch. 12 ; calibré sur Oughterson & Warren 1956 | facteur ~1.5 |
| Retombées | WSEG-10, décroissance t^-1.2, protection des bâtiments | Hanifen 1980 ; FEMA (facteurs de protection) | facteur ~3 |

Le portage du modèle DNA et des courbes de radiation s'appuie sur la bibliothèque
[glasstone](https://github.com/GOFAI/glasstone) (MIT, voir `THIRD_PARTY_NOTICES.md`).

## Écarts volontaires par rapport aux sources

- **WSEG-10** : trois erreurs d'unités du portage Python corrigées (hauteur du nuage avec
  le rendement en Mt, vent en mph dans L0 et α1, coordonnée sous le vent dans α2) ; temps
  d'arrivée réécrit sous forme dimensionnellement cohérente.
- **Hiroshima** : radiation initiale recalée sur la dosimétrie DS02 (~4.5 Gy à 1 km) par un
  facteur 0.45 propre à l'engin (`NuclearDevice.RadiationScale`) ; les courbes génériques de
  Glasstone donnent ~10 Gy (cas bas) à ~24 Gy (cas haut).

- **Rapport de densité au choc** : forme exacte (1 + μξ)/(μ + ξ) au lieu de l'ajustement
  BLAST.EXE, qui ne tend pas vers 1 pour un choc faible.
- **Tige de Mach** : interdite en deçà de sa distance de formation. Sans cette borne, la
  formule DNA diverge sous le point d'explosion (milliards de kPa).

## Limites connues

- Sol plat et idéal ; pas d'effet d'écran ni de canalisation par les bâtiments.
- Impulsion nucléaire approchée par une onde de Friedlander (b = 1, ±30 %).
- Thermobarique traité comme du TNT équivalent (onde plus longue non modélisée).
- Retombées sans relief ni précipitations (« cas moyen » WSEG-10) ; cratère en sol sec par défaut.
- Victimes : pas de prise en charge médicale ni d'évacuation ; la radiation est comptée
  comme mortelle sans soins (DL50 450 rad).
- Les impulsions seuils I0 des dégâts aux bâtiments sont à calibrer sur des données d'essais.
