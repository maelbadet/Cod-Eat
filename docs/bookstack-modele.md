# BookStack — format de recette et import structuré

Le carnet fourni « Inspiration / Repère / Ingrédients / Préparation / Cuisiner à deux et conserver / Allergènes et énergie » est reconnu en Markdown et dans le HTML de l’API. L’HTML est transformé en texte inerte en conservant titres, listes et tableaux. Les images, scripts, styles et cibles des liens ne sont pas exécutés ni importés dans les champs affichés.

## Exemple de structure reconnue

Les valeurs de cet exemple sont éditoriales et doivent être adaptées à votre fiche réelle. Les durées de conservation doivent être renseignées à partir de vos consignes validées.

```markdown
# Cabillaud citron & pommes de terre

**Inspiration : France · Mer · Chaud**

Une recette pour **4 portions individuelles**, soit deux repas à deux.

| Repère | Valeur |
|---|---|
| Temps actif indicatif | 15 min |
| Temps de cuisson | 30 min |
| Temps total indicatif | 45 min |
| Énergie estimée | à renseigner |

## Ingrédients

| Ingrédient | Quantité pour 4 | État au pesage |
|---|---|---|
| cabillaud | 600 g | cru, net |
| pommes de terre | 1 kg | poids net |
| citron | 1 pièce | entier |
| huile de colza | 30 g | prête à utiliser |
| sel | selon le goût | facultatif |

## Préparation

1. Laver et couper les pommes de terre.
2. Les cuire au four avec l’huile prévue dans la fiche.
3. Ajouter le poisson et le citron, puis poursuivre jusqu’à cuisson complète.

## Allergènes et énergie

- Allergènes identifiés dans les ingrédients : poisson.
- Étiquettes à vérifier : les produits réellement utilisés.
```

Le titre et les étapes restent gérés dans BookStack. Le titre de l’API est conservé ; les étapes sont extraites de la section Préparation/Étapes/Instructions. Les champs reconnus sont préremplis, mais la fiche reste en brouillon et `Allergènes vérifiés` reste décoché. Une page sans tableau d’ingrédients ou sans section d’étapes reste à structurer manuellement ; les pages de navigation ne sont pas transformées artificiellement en recettes prêtes à planifier.

API vérifiée dans les sources officielles BookStack :
- https://github.com/BookStackApp/BookStack/blob/development/app/Entities/Controllers/PageApiController.php
- https://github.com/BookStackApp/BookStack/blob/development/app/Api/ListingResponseBuilder.php
- GET api/pages?count=100&offset=N&sort=%2Bid&filter[book_id]=ID : enveloppe data/total, incrément par nombre réellement retourné.
- GET api/pages/ID : name, book_id, markdown ou html ; Authorization: Token ID:SECRET.

Avant utilisation réelle, vérifier /api/docs sur votre version et que le compte technique a accès à l’ensemble du livre. Les disparitions ne peuvent être interprétées que dans ce périmètre de permissions. Les requêtes aboutissent toutes avant l’enregistrement du catalogue ; une erreur ne publie pas un import partiel. Les pages absentes sont signalées et conservées.

## Diagnostic BookStack depuis le VPS

Après mise à jour du code, exécuter dans Bash :

```sh
docker compose build app
docker compose run --rm app --check-bookstack
```

Cette commande utilise les valeurs de `.env` via Compose et lit le livre et ses pages sans modifier la base. Elle renvoie un code de sortie non nul en cas d’échec, avec un message assaini et un code `BS_…` :

| Code | Vérification |
|---|---|
| `BS_CONFIG_URL` | URL HTTPS de base de BookStack, pas la fiche d’un livre ni `/api/docs`. |
| `BS_CONFIG_BOOK` | Identifiant numérique du livre. |
| `BS_CONFIG_TOKEN` | Token ID et Token Secret tous les deux présents, sans préfixe `Token` ni concaténation des deux. |
| `BS_HTTP_401` | Identifiants API refusés. |
| `BS_HTTP_403` | Expiration du jeton, droit du rôle d’accéder à l’API, lecture du livre ou restriction du proxy. |
| `BS_HTTP_404` | Livre inexistant/inaccessible ou URL de base incorrecte. |
| `BS_HTTP_301`, `BS_HTTP_302`, etc. | Redirection du proxy : utiliser l’URL HTTPS finale de l’installation. |
| `BS_DNS`, `BS_NETWORK`, `BS_TLS`, `BS_TIMEOUT` | DNS, accès depuis le conteneur, certificat ou disponibilité. |
| `BS_RESPONSE` | Réponse JSON inattendue, notamment une page HTML envoyée par un proxy ou une connexion intermédiaire. |

La synchronisation vérifie désormais que le livre existe et est lisible avant de paginer les pages. Elle demande explicitement JSON et conserve le catalogue précédent si un appel échoue. Les diagnostics administrateur et les journaux dédiés n’exposent ni corps de réponse externe ni jeton ni URL interne. Références officielles : [authentification API BookStack](https://github.com/BookStackApp/BookStack/blob/development/app/Api/ApiTokenGuard.php), [lecture d’un livre](https://github.com/BookStackApp/BookStack/blob/development/app/Entities/Controllers/BookApiController.php).

Après correction du `.env`, recréer le conteneur pour appliquer sa configuration :

```sh
docker compose up -d --force-recreate app
```

Un simple `docker compose restart app` ne recharge pas les variables d’environnement du conteneur. Ne pas publier le résultat complet de `docker compose config`, qui peut contenir les secrets ; partager uniquement le code de diagnostic assaini.

## Synchronisation en arrière-plan et erreurs 504

Le bouton d’administration démarre désormais une tâche de fond et répond immédiatement par une redirection. Le navigateur consulte ensuite un endpoint de suivi réservé aux administrateurs toutes les deux secondes. L’import ne dépend plus de la durée d’une requête du navigateur ni de sa déconnexion. Le formulaire d’édition n’est pas rechargé automatiquement : un lien permet d’actualiser le catalogue après réussite sans perdre silencieusement une saisie en cours.

Une seule synchronisation peut être active dans le conteneur applicatif. Le service ouvre son propre scope EF Core et conserve la limite de 30 secondes par appel HTTP, avec une durée totale maximale de 30 minutes. Les appels restent séquentiels pour ne pas multiplier la charge sur l’API. Tous les appels externes aboutissent avant l’écriture atomique du catalogue : une panne ne laisse pas d’import partiel.

Le suivi est en mémoire et prévu pour l’unique conteneur `app` du Compose fourni. Fermer le navigateur ne l’interrompt pas ; redémarrer le conteneur l’interrompt et réinitialise le suivi. Dans ce cas, relancer explicitement l’import après redémarrage. Le catalogue persistant contient soit la version précédente, soit un import entièrement validé par la transaction. Ne pas déployer plusieurs instances applicatives avec cette file en mémoire sans ajouter un verrou et une file distribués.

Mise à jour sur le VPS (les migrations sont relançables et appliquent aussi le schéma de la version courante) :

```sh
docker compose build app
docker compose run --rm app --migrate
docker compose up -d --force-recreate app
```

Une erreur 504 affichée directement par le navigateur peut provenir du proxy devant Cod & Eat lorsque l’ancienne synchronisation gardait la requête ouverte trop longtemps. Avec cette version, si le suivi affiche plutôt `[BS_HTTP_504]`, c’est une réponse 504 reçue depuis l’API BookStack ou son proxy : examiner alors les journaux et ressources de BookStack, de sa base et de son proxy. Si le navigateur reçoit encore une 504 dès le clic, vérifier que la nouvelle image a bien été reconstruite et déployée, puis les journaux de l’application et de Nginx Proxy Manager.


## Préremplissage du carnet et revue

Une nouvelle synchronisation traite aussi les fiches existantes dont le texte n’a pas changé lorsque la version d’extraction est nouvelle. La migration `StructuredRecipeImport` ajoute uniquement une colonne de métadonnées à Recipes, sans supprimer les recettes, les semaines ou les exports.

Après déploiement du code :

```sh
docker compose build app
docker compose run --rm app --migrate
docker compose up -d --force-recreate app
```

Relancer Synchroniser BookStack dans Administration, attendre le bilan puis ouvrir **Brouillons à compléter**. Les notes indiquent les informations à vérifier. Après lecture des ingrédients et des étiquettes, confirmer les allergènes et choisir **Valider explicitement la fiche** ; publier n’est pas nécessaire pour un foyer privé.

### Valeurs reprises et inconnues

- Seul le tableau d’ingrédients est utilisé pour les lignes de courses. Les produits cités dans les substitutions, les saveurs ou les instructions génériques ne sont pas ajoutés comme ingrédients supplémentaires.
- Les noms sont conservés, avec normalisation de casse et d’espaces. Les alternatives telles que « huile de colza ou d’olive » ne sont ni divisées ni regroupées avec un autre produit.
- Les quantités numériques, décimales et fractions usuelles gardent leur unité ; aucune équivalence cuillère/pièce/masse/volume n’est inventée. Les quantités qualitatives ou ambiguës restent vides, avec la valeur source en commentaire et un point à vérifier.
- Les portions viennent de l’introduction et de l’en-tête du tableau. Une contradiction laisse le nombre inconnu sur une nouvelle fiche.
- Les kcal sont reprises par portion avec leur provenance. Aucun calcul nutritionnel n’est inventé. Une correction locale des kcal ne garde pas automatiquement l’attribution de la valeur importée ; renseigner sa provenance avant validation.
- Les temps globaux sont repris seulement s’ils sont explicitement fournis. Le temps de cuisson n’est pas calculé en soustrayant le temps actif du total.
- Les informations de conservation et de service sont reprises comme propositions à vérifier. La mention « Organisation prévue sous 48 h » est reconnue comme repère source ; les instructions et réserves restent visibles dans les notes. Aucune durée de congélation ni consigne de décongélation absente n’est créée.
- La mention « aucun identifié » reste une déclaration de la source, sans confirmer l’absence d’allergènes. Les étiquettes à vérifier sont conservées. Le filtre d’exclusion continue de rejeter une fiche aux allergènes non vérifiés.
- Les appareils ne sont pas déduits d’une référence occasionnelle dans les étapes : une section explicite Ustensiles/Équipements/Matériel est reprise, sinon le champ reste à compléter et un filtre d’équipements exclut la donnée inconnue.
- Les rayons sont proposés pour des noms exacts courants et restent à relire. Les autres produits sont marqués « À classer ».

Pour la poêlée fournie : 11 ingrédients, 4 portions, 20 min actives, 45 min au total, ~610 kcal estimées par portion et un repère de 48 h repris de la source. Le temps de cuisson global, la durée de congélation et la décongélation restent inconnus. Les huit étapes restent distinctes des autres sections. La source non testée et les réserves sur les allergènes sont signalées.

### Corrections locales et mises à jour

Pour chaque champ prérempli, une empreinte de la dernière valeur appliquée est conservée. Tant que cette valeur n’a pas été modifiée localement, une évolution de la source peut la mettre à jour. Une correction locale, y compris une suppression volontaire, est conservée lors des synchronisations suivantes. Modifier la liste d’ingrédients la rend locale dans son ensemble : une évolution du tableau source est alors signalée pour revue plutôt que d’écraser la liste corrigée.

Pour les anciennes fiches sans métadonnées, les champs vides sont complétés et les valeurs déjà renseignées sont conservées. Cela inclut les anciens défauts de portions, catégorie et congélation : lorsqu’ils diffèrent de la source, les notes affichent les deux valeurs à comparer. Les semaines et préparations déjà créées gardent leurs copies ; une nouvelle finalisation reste nécessaire pour créer de nouveaux exports.

Toute modification éditoriale ou structurée importée qui affecte une fiche la remet en brouillon et retire la vérification des allergènes. Une synchronisation réellement inchangée ne retire pas la validation. Il n’existe ni validation ni publication automatique.


### Pages de liaison et guides BookStack

L’activation en lot exige désormais une classification source `recipe` issue de l’extracteur version 2. Un tableau d’ingrédients seul ne suffit pas : la source doit également contenir une préparation numérotée avec une instruction de cuisine reconnue. Les index, accueils, listes de liens et guides restent conservés dans l’administration, hors du lot d’activation. Les formats ambigus passent en revue manuelle ; ce contrôle privilégie l’exclusion en cas de doute, sans prétendre comprendre toute recette possible.

Après cette mise à jour, relancer une synchronisation BookStack avant l’activation en lot : même les pages inchangées sont réanalysées. Les anciens imports sans classification ne peuvent pas être activés en lot. Aucun titre de pays ou de page n’est utilisé seul pour décider. Une page non reconnue ne conserve pas une ancienne activation privée après réanalyse.

### Publication publique des imports en lot

Pour publier les imports, utiliser **Administration → Examiner et publier les imports en lot** (`/Admin/ReviewImports?Mode=public`), après resynchronisation avec l’extracteur version 2. Ce mode inclut les brouillons et fiches validées importés, même déjà disponibles dans le planning privé. Il exige une vraie recette reconnue, les champs structurés obligatoires et les allergènes explicitement vérifiés sur la fiche. Les pages de liaison et les formats non reconnus restent hors du lot. La publication ne coche jamais la vérification des allergènes.

Préparer l’aperçu puis confirmer la publication publique. Un ticket de planning privé ne peut pas autoriser une publication. Les fiches publiées reçoivent leur statut et URL publique ; les anciens slugs restent redirigés. Le catalogue doit aussi être ouvert dans **Administration → Ouverture au public → Catalogue public**. Cette ouverture reste un réglage explicite distinct. La liste publique affiche 24 fiches par page et permet de rechercher dans tout le catalogue publié.

Si toutes les fiches restent bloquées sur « allergènes vérifiés », leur import n’a pas remplacé cette revue : ouvrir et vérifier les fiches avant de préparer un nouveau lot public.
