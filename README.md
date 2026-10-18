# BlastSim — simulateur d'explosions

Simulateur 3D des effets d'une explosion sur une ville, de la grenade à la Tsar Bomba.
Le rendu (Godot 4) est entièrement piloté par un moteur physique en C# fondé sur des
modèles publiés et validés contre des données historiques : ce que l'on voit à l'écran
(position de l'onde de choc, bâtiments qui s'effondrent, incendies, zones de dégâts)
découle directement du calcul.

> Projet à but éducatif : comprendre l'ampleur et la nature des effets des explosifs et
> des armes nucléaires. Les résultats sont des estimations (précision de l'ordre de
> 15 à 30 %), pas un outil de planification.

## Ce que fait le simulateur

- **Des villes à l'échelle réelle** : une ville générée de 10 × 10 km (environ 42 000
  bâtiments) ou une **vraie ville téléchargée depuis OpenStreetMap** (Paris, Manhattan,
  Londres, Hiroshima, Beyrouth) avec ses bâtiments, ses rues, son fleuve et ses parcs.
- **Le souffle** : surpression, impulsion et temps d'arrivée de l'onde en tout point,
  avec la hauteur d'explosion (réflexion de l'onde sur le sol) et l'altitude du terrain.
- **Les effets nucléaires** : flash, boule de feu, rayonnement thermique et brûlures,
  radiation initiale, nuage en champignon.
- **Les effets conventionnels** : 7 explosifs (TNT, C-4, RDX, ANFO…) et projection d'éclats.
- **Les dégâts bâtiment par bâtiment** : chaque bâtiment reçoit l'onde au moment où elle
  l'atteint ; son sort (intact, léger, modéré, grave, effondré) est tiré selon un modèle de
  vulnérabilité qui tient compte de la pression *et* de la durée de l'onde. Une grenade
  ne fait rien à un immeuble ; une onde nucléaire de même pression le rase.
- **L'estimation des victimes** : population tirée de la surface de plancher de chaque
  bâtiment, de jour ou de nuit ; morts et blessés selon le souffle, l'effondrement, les
  incendies, les brûlures et la radiation. Modèle calibré sur la mortalité observée à
  Hiroshima selon la distance.
- **Les retombées radioactives** (modèle WSEG-10) : panache poussé par le vent, dose
  cumulée qui s'étend heure après heure, victimes recalculées selon l'abri qu'offre
  chaque bâtiment. Nulles pour une explosion aérienne, majeures au sol.
- **Le cratère** des explosions au sol, creusé dans le terrain ; les bâtiments qui s'y
  trouvaient disparaissent.
- **La fiche de chaque bâtiment** : un clic sur un bâtiment montre ce qu'il a reçu
  (surpression, impulsion, chaleur, radiation, retombées), ses probabilités de dégâts,
  l'état tiré et le sort de ses occupants.
- **10 scénarios de référence** : grenade M67, bombe Mk 82, MOAB, explosion du port de
  Beyrouth (2020), Hiroshima, Nagasaki, bombe à neutrons, ogive de 1,2 Mt, Tsar Bomba,
  Castle Bravo.

## Prérequis

| Outil | Version | Lien |
|---|---|---|
| Godot, **édition .NET** | 4.7 ou plus récent | https://godotengine.org/download |
| SDK .NET | 8.0 | https://dotnet.microsoft.com/download/dotnet/8.0 |

L'édition standard de Godot ne gère pas le C# : il faut l'édition « .NET » (ou « mono »).

## Installation et lancement

1. Cloner le dépôt :
   ```
   git clone https://github.com/skou94/Simulateur.git
   ```
2. Ouvrir Godot, cliquer sur **Importer** et choisir `BlastSim/godot/project.godot`.
3. Appuyer sur **F5** (ou le bouton ▶ en haut à droite).
   Au premier lancement, Godot compile le projet C# : quelques secondes d'attente.

La scène paraît vide dans l'éditeur : c'est normal, la ville, la caméra et l'interface
sont créées par le code au lancement.

## Utilisation

1. Choisir une **ville** : générée, ou réelle. Une ville réelle est téléchargée la
   première fois (de 30 s à 2 min, connexion internet nécessaire), puis lue depuis le cache.
2. Choisir un **scénario**, ou régler soi-même le type d'engin, la puissance et la
   hauteur d'explosion ; régler le vent et le moment (jour ou nuit).
3. Cliquer sur le sol pour choisir le **point d'impact** (marqueur rouge).
4. Cliquer sur **DÉTONER**.

Le panneau affiche alors les rayons d'effet (boule de feu, 140 / 35 / 7 kPa, brûlures du
3ᵉ degré, radiation, retombées), et le coin supérieur gauche suit en direct le front de
choc, le bilan des dégâts et le nombre de victimes.

Un clic sur un **bâtiment** ouvre sa fiche, avant ou pendant l'explosion (Échap pour la
fermer) : on y voit pourquoi il s'est effondré, ou pas, et combien de ses occupants sont
morts ou blessés.

Pour voir les retombées, choisir une explosion **au sol** (hauteur 0, par exemple Castle
Bravo), dézoomer largement et accélérer le temps (×1 000 et plus) : elles arrivent en
quelques heures. Les couleurs au sol indiquent la dose cumulée : jaune 1–10 rad, orange
10–100, rouge 100–1 000, violet au-delà (dose mortelle pour la moitié des gens exposés
sans abri : ~450 rad).

### Commandes

| Action | Commande |
|---|---|
| Tourner autour de la scène | glisser avec le clic gauche |
| Déplacer la vue | glisser avec le clic droit ou le clic molette |
| Zoomer | molette |
| Choisir le point d'impact | clic gauche simple sur le sol (hors explosion en cours) |
| Fiche d'un bâtiment | clic gauche simple sur le bâtiment ; Échap pour fermer |
| Pause / reprise | Espace |

### Réglages du panneau

| Réglage | Rôle |
|---|---|
| Ville | ville générée ou ville réelle OpenStreetMap |
| Scénario | préréglage historique ou « Personnalisé » |
| Type d'engin | fission, thermonucléaire, rayonnement renforcé ou TNT |
| Puissance | de 10 t à 100 Mt (nucléaire), de 100 g à 5 000 t de TNT (conventionnel) |
| Hauteur d'explosion | de 0 à 8 km (nucléaire), de 0 à 300 m (conventionnel) |
| Vitesse du temps | de ×0,01 (ralenti) à ×4 000 ; utile pour les fortes puissances et pour les retombées, qui se jouent sur des heures |
| Vent | direction vers laquelle il souffle et vitesse : orientent et allongent le panache de retombées |
| Moment de l'explosion | jour (gens au travail, 25 % dehors) ou nuit (gens chez eux, 4 % dehors) |
| Afficher les zones d'effet | cercles au sol des seuils de dégâts |
| Réinitialiser / Nouvelle ville | remet la ville en état / génère une autre ville |

### Lancement en ligne de commande

Les options se placent après `--` :

```
Godot_v4.7.2-stable_mono_win64.exe --path BlastSim/godot -- --demo --scenario=5 --time-scale=3
```

| Option | Effet |
|---|---|
| `--demo` | déclenche le scénario sélectionné une seconde après le démarrage |
| `--scenario=N` | 1 grenade, 2 Mk 82, 3 MOAB, 4 Beyrouth, 5 Hiroshima, 6 Nagasaki, 7 bombe à neutrons, 8 ogive 1,2 Mt, 9 Tsar Bomba, 10 Castle Bravo |
| `--custom=type,puissance,hauteur` | engin personnalisé ; type 0 fission, 1 thermonucléaire, 2 neutrons, 3 TNT ; puissance en kt (ou kg de TNT), hauteur en m |
| `--city=N` | 1 Paris, 2 Manhattan, 3 Londres, 4 Hiroshima, 5 Beyrouth |
| `--wind=direction,vitesse` | vent (degrés vers lesquels il souffle, m/s) |
| `--night` | explosion de nuit |
| `--select=x,z` | ouvre la fiche du bâtiment situé au point (x, z), en mètres |
| `--time-scale=x` | vitesse du temps |
| `--camera=distance,lacet,tangage` | position de la caméra (mètres, radians) |
| `--benchmark=s` | affiche chaque seconde les images par seconde et les temps CPU/GPU, puis quitte après s secondes |

## Organisation du projet

```
BlastSim/
├── src/BlastPhysics/          moteur physique (bibliothèque C#, sans dépendance graphique)
├── src/CityData/              villes réelles OpenStreetMap (téléchargement, conversion, carte au sol)
├── tests/BlastPhysics.Tests/  tests de cohérence physique et de validation historique
├── godot/                     simulateur 3D (scripts C# et shaders)
└── README.md                  documentation technique des modèles physiques
```

Le moteur physique est indépendant du rendu : on peut l'utiliser seul dans n'importe
quel programme .NET.

```csharp
var hiroshima = Scenarios.Hiroshima();
var effets = hiroshima.At(groundRangeM: 1600);   // effets à 1,6 km du point zéro
Console.WriteLine(effets.Blast.OverpressurePa);  // ≈ 32 600 Pa
```

## Fiabilité de la physique

Les modèles viennent de sources de référence : modèle de souffle DNA (BLAST.EXE), loi de
Kinney-Graham pour le TNT, *The Effects of Nuclear Weapons* de Glasstone & Dolan pour le
thermique et la radiation, critères DoD pour les éclats, WSEG-10 pour les retombées,
HAZUS pour les victimes par état de dégât, Glasstone & Dolan et Ambrosini pour les
cratères. Les 68 tests automatiques vérifient notamment :

| Cas | Calculé | Référence |
|---|---|---|
| Hiroshima, rayon à 35 kPa (destruction de la plupart des bâtiments) | 1,53 km | ~1,6–1,8 km |
| Hiroshima, radiation initiale à 1 km | 4,5 Gy | ~4–5 Gy (dosimétrie DS02) |
| Hiroshima, mortalité à 1,5 km / 2 km / 3 km | 53 % / 17 % / ~0 % | ~50–60 % / 20–30 % / quelques % |
| Ogive de 1,2 Mt, rayon à 35 kPa | 6,5 km | ~6,4 km |
| Trinity, rayon de la boule de feu à 25 ms | ~138 m | 130 m (analyse de G. I. Taylor) |
| Grenade M67, rayon de danger des éclats | 16 m | 15 m |
| Cactus (18 kt sur corail), diamètre du cratère | ~120 m | ~110 m |
| Castle Bravo, dose sur 48 h à 180 km sous le vent | ~1 200 rad (dans l'axe) | Rongelap : ~190 rad (hors de l'axe) |

Pour lancer les tests :

```
cd BlastSim
dotnet test
```

**Limites connues** : sol plat (pas d'effet d'écran des bâtiments) ; bâtiments OSM
approchés par des rectangles de même surface ; seuils de dégâts aux bâtiments indicatifs ;
bilan des victimes précis à un facteur ~1,5 ; retombées « cas moyen » (WSEG-10), sans
relief ni pluie, précises à un facteur ~3 ; cratères calculés en sol sec (×2 environ dans
un sol gorgé d'eau, comme le port de Beyrouth). Le détail des modèles, des écarts volontaires
et des limites se trouve dans [BlastSim/README.md](BlastSim/README.md).

## Performances

Mesuré sur une Radeon RX 7700 XT en 1600 × 900 : de 175 à 550 images par seconde selon
la scène, y compris quand toute la ville s'effondre en même temps.

## Crédits

Le portage du modèle de souffle DNA, des courbes de radiation et du modèle de retombées
WSEG-10 s'appuie sur la bibliothèque [glasstone](https://github.com/GOFAI/glasstone)
(licence MIT) ; voir [BlastSim/THIRD_PARTY_NOTICES.md](BlastSim/THIRD_PARTY_NOTICES.md).

Les villes réelles utilisent les données © les contributeurs d'OpenStreetMap, sous
licence ODbL (https://www.openstreetmap.org/copyright), téléchargées via l'API Overpass.
