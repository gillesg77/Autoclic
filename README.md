# AutoClic

Enregistreur et lecteur de macros clavier / souris pour Windows.
WinUI 3 (non empaqueté) sur .NET 8.

## Prérequis

Le SDK .NET 8 suffit :

```bash
winget install Microsoft.DotNet.SDK.8
```

Visual Studio n'est pas nécessaire — voir « Piège de configuration » plus bas. Le
Windows App SDK arrive par NuGet et `WindowsAppSDKSelfContained` l'embarque dans la
sortie, donc rien à installer sur les postes cibles non plus.

## Compiler et lancer

```bash
dotnet build AutoClic.sln
```

```bash
dotnet run --project src/AutoClic.App
```

```bash
dotnet test AutoClic.sln
```

Publication d'un dossier autonome à copier sur un poste :

```bash
dotnet publish src/AutoClic.App -c Release -r win-x64 -o publish
```

## Structure

| Projet | Rôle |
| --- | --- |
| `src/AutoClic.Core` | Interop Win32, capture, rejeu, repérage visuel, sérialisation. Aucune dépendance UI. |
| `src/AutoClic.App` | Fenêtre WinUI 3. |
| `tests/AutoClic.Core.Tests` | Tests xUnit du modèle et de la sérialisation. |

Le découpage n'est pas cosmétique : `AutoClic.Core` reste testable et réutilisable
(service Windows, CLI, tâche planifiée) sans traîner WinUI derrière.

### Points techniques

- **Capture** — hooks `WH_KEYBOARD_LL` / `WH_MOUSE_LL`. Ils imposent deux contraintes :
  installation depuis un thread avec boucle de messages (le thread UI), et rappels
  rapides sous peine de désinstallation par Windows.
- **Rejeu** — `SendInput` en coordonnées absolues rapportées au bureau virtuel,
  seul repère correct en multi-écrans.
- **Boucle** — les événements injectés portent le drapeau `LLKHF_INJECTED` / `LLMHF_INJECTED`
  et sont ignorés à la capture ; rejouer pendant un enregistrement ne s'auto-alimente pas.
- **Chronologie** — chaque événement porte son délai depuis le précédent. `Task.Delay`
  seul dérive d'environ 15 ms par attente, d'où l'attente active en fin de délai
  (`PrecisionDelay`).

## Utilisation

| Action | Raccourci |
| --- | --- |
| Arrêter l'enregistrement | `F9` |
| Interrompre le rejeu | `Échap` |
| Annuler la sélection d'un repère | `Échap` |

La touche d'arrêt est filtrée : elle n'atteint pas l'application au premier plan et
n'entre pas dans la macro.

Les macros sont du JSON lisible et modifiable à la main.

## Se repérer sur une fenêtre

Choisir une **fenêtre cible** avant d'enregistrer : les positions sont alors stockées
en coordonnées clientes, et la macro survit à un déplacement de la fenêtre. Au rejeu,
l'application est retrouvée par nom de processus (éliminatoire), classe Win32 puis
titre — dans cet ordre de confiance, le titre étant le moins fiable des trois.

En cas de redimensionnement, trois règles :

| Règle | Comportement | Quand l'utiliser |
| --- | --- | --- |
| Bord le plus proche *(défaut)* | Chaque axe conserve sa distance au bord dont il était le plus près | Interfaces classiques, dont Battle.net |
| Coin haut-gauche | Distance au coin conservée telle quelle | Tout est ancré en haut à gauche |
| Mise à l'échelle | Position proportionnelle à la taille | Contenu qui s'étire réellement |

La règle par défaut n'est pas un compromis mou : les interfaces **ancrent** leurs
commandes à un bord au lieu de les étirer. Un bouton à 50 px du bord droit y reste
quelle que soit la largeur — une règle proportionnelle le manquerait.

### Nettoyer

Un enregistrement contient surtout du bruit : les déplacements du moment où l'on
cherchait l'élément à l'écran. **Nettoyer…** les retire et remplace le trajet par une
ligne droite vers la cible. La boîte affiche l'effet chiffré — événements et durée
avant / après — avant d'appliquer.

| Option | Défaut | Effet |
| --- | --- | --- |
| Supprimer les déplacements entre les clics | oui | Retire le trajet de recherche |
| Rejoindre la cible en ligne droite | oui | Décoché, le curseur saute d'un clic à l'autre — plus rapide, mais un élément qui n'apparaît qu'au survol de son parent ne se révèlera pas |
| Redresser aussi les glissers | non | Voir ci-dessous |
| Plafonner les attentes | 0 (désactivé) | Écrase le temps passé à chercher, mais aussi les attentes légitimes pendant que l'application répond |

Les délais des événements supprimés sont reversés au suivant : à moins de plafonner,
la macro nettoyée se déroule exactement au même rythme.

**Les glissers sont préservés par défaut.** Pour un glisser-déposer ordinaire, seuls
le point de prise et le point de lâcher comptent, et cocher l'option ne change rien.
Le trajet redevient significatif dans trois cas : une arborescence qui déplie un
dossier au survol pendant le glisser, une liste qui défile quand on approche du bord,
et tout tracé libre — lasso, dessin, signature — où le chemin est la donnée. Quand
l'option est cochée, quelques positions intermédiaires sont tout de même conservées :
sans elles, ni le seuil de déclenchement du glisser ni la cible de dépôt ne voient
passer le curseur.

Le nettoyage remplace la macro en mémoire, sans retour en arrière. La macro sur disque
n'est touchée qu'au prochain enregistrement.

### Simuler avant de rejouer

**Simuler** déroule la macro sans rien envoyer au système : un curseur vert fluo suit
le trajet calculé, chaque clic laisse un anneau coloré, et un cadre rappelle la zone
cliente sur laquelle la macro raisonne. C'est le moyen de vérifier les positions
avant de laisser la macro agir.

Ce n'est pas un rendu approché : la simulation et le rejeu réel passent par le même
`MacroPlayer`, seule la destination des entrées change (`IInputSink`). Mêmes délais,
même ancrage, même correction issue du repère — une simulation qui referait ces
calculs de son côté finirait par diverger et ne prouverait plus rien.

L'affichage passe par une fenêtre superposée en alpha par pixel, marquée
`WS_EX_TRANSPARENT` et `WS_EX_NOACTIVATE` : clics et survol la traversent, elle ne
prend jamais le focus.

### Le repère visuel

L'ancrage géométrique a une limite que la géométrie ne peut pas détecter : sous une
certaine taille, beaucoup d'interfaces ne déplacent pas leurs commandes, elles les
**masquent**. La position calculée reste plausible tout en désignant autre chose, et
le clic part quand même.

D'où le repère : « Définir un repère… » puis tracer un rectangle autour d'un élément
stable de la fenêtre. Un calque de sélection matérialise la zone pendant le tracé —
réticule pour viser, voile percé sur la sélection, dimensions en pixels, poignées aux
angles, et bordure rouge tant que la zone est trop petite. Le glisser est intercepté
et n'atteint jamais l'application visée : entourer un bouton ne l'actionne pas.

Ce calque est refermé — et sa disparition attendue — avant toute capture : sans cela,
un repli sur la copie d'écran le photographierait lui-même.

Une boîte montre alors **les pixels capturés, agrandis au plus proche voisin** : ceux
qui serviront à la comparaison, sans lissage qui masquerait les bords sensibles.
On peut y cliquer les sommets d'un **détourage** pour ne garder que l'élément. Le
masque est enregistré dans le canal alpha du PNG, si bien que le repère reste un seul
fichier qu'on peut ouvrir et vérifier à l'œil.

Détourer compte quand le fond autour de l'élément change d'un lancement à l'autre —
une liste qui défile derrière un bouton rond, par exemple. Les pixels exclus ne
pèsent plus dans le verdict.

#### Deux façons de comparer

| Mode | Mesure | Comportement |
| --- | --- | --- |
| Au pixel *(défaut)* | Proportion de pixels concordants, en couleur, à ±12 niveaux par canal | Stricte. Ne confond pas deux éléments voisins, mais peut refuser de démarrer sur une différence de rendu invisible à l'œil |
| Par corrélation | Corrélation croisée normalisée centrée, sur la luminance | Tolérante aux variations uniformes de luminosité et de contraste — changement de thème, survol — au prix d'un risque de confusion |

La comparaison au pixel n'est pas « plus fiable » dans l'absolu, elle est plus
**stricte** : elle déplace le risque du mauvais clic vers le refus de démarrer. C'est
le bon arbitrage ici, et la tolérance par canal absorbe l'antialiasing. Si le rejeu
échoue malgré tout, augmenter la tolérance, détourer plus serré, ou passer en
corrélation.

#### Ce qui se passe au rejeu

Avant le moindre clic :

1. l'emplacement attendu du repère est calculé avec la règle de redimensionnement ;
2. il est cherché dans un rayon de 96 px, selon le mode choisi ;
3. sous le seuil (95 % par défaut), **le rejeu s'interrompt** avec un message chiffré
   qui explique les causes probables ;
4. sinon l'écart entre position trouvée et position attendue corrige toutes les
   positions de la macro.

Le repère est donc un garde-fou autant qu'un point de référence. Sans lui, une macro
rejouée sur une fenêtre trop étroite clique dans le vide en silence.

## Limites connues

- **Applications élevées** — Windows (UIPI) bloque `SendInput` vers un processus plus
  privilégié. Pour piloter un outil lancé en administrateur, lancer AutoClic en
  administrateur aussi.
- **Sans fenêtre cible** — les coordonnées restent absolues, donc liées à la
  disposition du bureau au moment de l'enregistrement. Choisir une cible lève cette
  limite pour tout ce qui tombe dans la fenêtre.
- **Mise à l'échelle de l'affichage** — changer le facteur d'échelle Windows entre
  l'enregistrement et le rejeu fait échouer la reconnaissance du repère : les pixels
  capturés ne correspondent plus. L'erreur le mentionne.
- **Capture refusée** — quelques applications renvoient une image noire à
  `PrintWindow` ; on retombe alors sur une copie de l'écran, qui exige que la fenêtre
  soit visible et non masquée.

## Le calque de superposition

`OverlayWindow` sert deux usages : le rectangle de sélection du repère, et le curseur
de simulation. Sa fluidité tient à un détail d'implémentation qui mérite d'être
protégé.

Le dessin va **directement dans une section DIB au format `Format32bppPArgb`**, allouée
une seule fois. GDI+ y écrit en alpha prémultiplié, seul format accepté par
`AC_SRC_ALPHA` — il n'y a donc ni copie ni passe de conversion entre le dessin et
l'affichage.

La voie naïve — un bitmap neuf par image, prémultiplié pixel par pixel en code managé,
puis recopié via `GetHbitmap` — impose **trois passes sur toute la surface à chaque
image**. Sur une fenêtre plein écran cela suffit à faire saccader l'animation. Si le
rectangle de sélection redevient saccadé un jour, c'est le premier endroit à regarder.

`LayeredSurfaceTests` vérifie l'hypothèse centrale en relisant les octets stockés :
un rouge pur à 50 % d'opacité doit ressortir avec une composante rouge proche de
l'alpha, pas à 255. Sans cela les zones semi-transparentes seraient blanchies.

La cadence est régulée sur le temps réellement passé, et `timeBeginPeriod(1)` relève
la résolution de l'horloge — sans quoi une attente de 16 ms en dure couramment 30.

## Les boîtes de dialogue

Aperçu du repère et nettoyage sont des **fenêtres indépendantes**, pas des
`ContentDialog`. Ce dernier borne son contenu à la fenêtre hôte et à une largeur
maximale interne : un contenu plus large s'y fait rogner au lieu d'agrandir la boîte.

`ModalHost` s'en charge, et trois de ses choix méritent explication :

- **Possession par `GWLP_HWNDPARENT`**, pas par `AppWindow.OwnerWindowId` : cette
  propriété est en lecture seule, et le seul moyen officiel d'attribuer un
  propriétaire — `AppWindow.Create(presenter, ownerWindowId)` — ne s'applique pas à
  une fenêtre XAML, dont l'`AppWindow` est créée pour nous.
- **Modalité par `EnableWindow`**, pas par `OverlappedPresenter.IsModal` : mesuré sur
  cette version, `IsModal` laisse la fenêtre mère active, donc cliquable. La fenêtre
  mère est réactivée en toute première instruction de la restauration — laissée
  désactivée, elle ne recevrait plus ni clic ni `WM_SYSCOMMAND`, donc plus même
  Alt+F4.
- **Échelle par `GetDpiForWindow`**, pas par `XamlRoot.RasterizationScale` : la boîte
  est d'abord déplacée sur le moniteur de sa fenêtre mère, puis mesurée. Sans cela,
  une boîte née sur un écran à 100 % et centrée sur un écran à 150 % serait figée aux
  deux tiers de la taille nécessaire.

La taille se déduit de `DesiredSize` du contenu, bornée à la zone de travail du
moniteur. La rangée de boutons vit hors du `ScrollViewer` : sur un petit écran, un
contenu plus haut que l'écran ne doit jamais la rendre inatteignable.

### Pourquoi une taille estimée avant l'activation

Chaque boîte fournit à `ModalHost` une **taille estimée**, appliquée avant `Activate()`,
puis corrigée après mesure si l'écart dépasse six pixels. Ce détour paraît inélégant ;
les deux voies directes ne marchent pas.

*Mesurer avant d'activer* est impossible : sans activation le contenu n'a pas de
`XamlRoot`, et `DesiredSize` vaut zéro.

*Activer puis redimensionner* fait apparaître la fenêtre à la taille par défaut du
système — mesurée à **1920×1023** — avant de la voir se rétracter. C'est visible.

*Activer hors écran, dimensionner, puis ramener* échoue autrement : WinUI passe par le
compositeur, qui ne fait **aucune passe de mise en page** pour une fenêtre qu'aucun
moniteur ne couvre. `Loaded` ne se déclenche alors jamais, la mesure n'a pas lieu, et
la fenêtre reste perdue hors champ. Vérifié : elle demeurait à (40, −30000) en
1920×1023. Cette astuce fonctionne pour une fenêtre GDI, pas pour du contenu XAML.

L'estimation n'a donc pas à être exacte, seulement proche. Mesuré : les deux boîtes
n'affichent qu'un seul état, sans redimensionnement visible.

## Piège de configuration

`AutoClic.App.csproj` force `<EnableMsixTooling>true</EnableMsixTooling>` alors que
l'application n'est pas empaquetée. Ce n'est pas une incohérence : cette propriété
choisit quelles cibles MSBuild génèrent `resources.pri`. À `true`, ce sont celles
livrées dans le paquet NuGet, qui appellent `makepri`. À `false`, le Windows App SDK
retombe sur les anciennes cibles `MrtCore`, qui réclament les tâches d'empaquetage
APPX de Visual Studio — la build échoue alors sur un SDK .NET seul (`MSB4062`).
`WindowsPackageType=None` reste maître : aucun MSIX n'est produit.

## Pistes

- Déclenchement par raccourci global, sans passer par la fenêtre.
- Bibliothèque de macros dans `%APPDATA%\AutoClic` (`MacroStorage.DefaultFolder` est déjà prévu).
- Édition des pas (suppression, réordonnancement, ajustement des délais).
- Compression des déplacements en interpolation, pour des fichiers plus petits.

## Traduction

Sept langues : français, anglais, allemand, espagnol, portugais, chinois, coréen.
Un fichier JSON plat par langue dans `src/AutoClic.Localization/Strings`, chargé au
démarrage et sélectionnable à chaud dans l'interface.

**Le cœur ne porte aucun texte d'interface.** Il lève des `AutoClicException` avec un
`ErrorCode` et des faits chiffrés ; `Message` reste en anglais pour les journaux, et
c'est `Localizer.Describe` qui met l'erreur en mots. Le message de refus du repère est
même composé de fragments traduits, choisis selon les faits : le rétrécissement de la
fenêtre n'est mentionné que s'il a eu lieu.

Une clé absente tombe sur l'anglais, et si elle y manque aussi, la clé elle-même
s'affiche — un oubli devient visible sans casser l'écran.

`CatalogueTests` verrouille trois invariants : mêmes clés partout, mêmes paramètres
`{0}` et `{nom}` dans chaque traduction, et une entrée pour chaque `ErrorCode`. Un
paramètre en trop lèverait à l'exécution ; un paramètre en moins ferait disparaître
une information sans prévenir.

### Ajouter une langue

Copier `en.json`, le traduire, reconstruire. Attention : une compilation
**incrémentale ne recopie pas** un fichier de langue nouvellement ajouté, et la langue
manquerait silencieusement à l'exécution.

```bash
dotnet build AutoClic.sln --no-incremental
```

Le chinois et le coréen n'ont pas été relus par un locuteur natif.

## Licence

GNU General Public License v3.0 — voir [LICENSE](LICENSE).

Toute redistribution, modifiée ou non, doit rester sous la même licence et être
accompagnée de son code source.
