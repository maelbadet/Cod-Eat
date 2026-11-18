# Vérifications de livraison

Les contrôles sont locaux, sur Windows avec SDK .NET 10.0.401. Aucun accès au VPS ni à l’instance BookStack réelle.

## Contrôles réalisés

- Compilation Release : réussie sans avertissement ni erreur.
- Migrations : aucun changement de modèle non migré ; SQL PostgreSQL généré dans `docs/schema-postgresql.sql`.
- Dépendances : aucun package NuGet vulnérable signalé, transitifs inclus ; `npm audit` ne signale aucune vulnérabilité. L’image Docker finale reste à scanner.
- Tests xUnit : domaine, HTTP réel dans TestServer, base SQLite relationnelle, Identity, contraintes uniques, isolation des foyers et PDF analysés avec PdfPig. Dernier passage : 63 tests réussis, aucun échec.
- Deux utilisateurs d’un foyer voient la même révision de semaine ; un autre foyer voit une autre semaine et reçoit 404 pour les PDF du premier foyer.
- Redirection des anonymes, refus des actions administratives aux membres, POST sans antiforgery refusé, conflit de modification en 409.
- Réimport idempotent et conservation des enrichissements, panne simulée sans perte de catalogue.
- Un lot de quatre portions pour deux créneaux, conversions compatibles et maintien des décisions/lignes manuelles, refus de dépassement des portions.
- Inconnues exclues des filtres concernés, avertissements de conservation inconnue et dépassée.
- Rappels saisonniers et priorité mensuelle ; deux lectures concurrentes et contrainte unique en base.
- Inscription fermée au départ, création d’un nouveau foyer isolé après activation, second membre invité, expiration et usage unique, confirmation d’adresse par jeton Identity.
- Catalogue public limité aux publications, JSON-LD analysé, dépublication retirée de la fiche et du sitemap.
- PDF A4 téléchargés dans Chromium et analysés comme vrais documents ; test de texte accentué, décimales et pagination sur liste longue.

## Navigateur proche production

Application lancée avec `ASPNETCORE_ENVIRONMENT=Production`, HTTPS local et cookies Secure/HttpOnly, base de démonstration créée explicitement. Le certificat autosigné est réservé au test. L’exception SQLite en Production est activée uniquement pour ce contrôle (`Demo__AllowProduction=true`).

- Liste chargée : 32 produits aux noms longs et quantités décimales, en plus des lignes générées. Tab atteint un élément avec focus ; une requête HTTP portant un faux en-tête de proxy reste redirigée vers HTTPS.
- Écrans semaine, recettes, courses, batch et réglages : contrôlés à **320, 390, 768 et 1440 px**, aucun débordement horizontal détecté.
- Parcours à **390 et 1440 px** : ajouter un repas, modifier ses portions, traiter un rappel, cocher une ligne et constater sa persistance, finaliser et télécharger les deux PDF.
- Lighthouse sur catalogue et recette publiée, en **mobile et desktop** : **SEO 100/100 et accessibilité 100/100 sur les quatre audits**. Rapports HTML dans [qa](qa/), résultats bruts dans [qa/results.json](qa/results.json).
- Les audits automatisés ne constituent pas un audit manuel exhaustif d’accessibilité. Les formulaires ont des libellés, le focus est visible et les avertissements sont textuels ; les tailles, clavier et contrastes sont vérifiables avec les captures et les rapports.

Les PDF et captures conservés contiennent uniquement les exemples culinaires. Aucune adresse de compte, aucun jeton d’invitation et aucun secret ne sont stockés dans les rapports.

## À vérifier avec votre infrastructure

Le moteur Docker est arrêté sur cette machine : pas de construction ni de scan de l’image finale, pas d’exécution des migrations PostgreSQL contre un serveur. Le schéma et les migrations sont fournis ; les tests utilisent SQLite, qui ne prouve pas tous les comportements PostgreSQL.

Pas de test de délivrabilité SMTP, d’import contre votre livre, de TLS public ni de chaîne Nginx Proxy Manager → Kestrel. Les simulations et contrôles locaux ne prouvent pas la configuration réelle des droits BookStack, du réseau Docker, des redirections du domaine ou de la persistance après remplacement d’un conteneur.

Après démarrage Docker, scanner l’image avec `docker scout cves` ou l’outil du VPS et corriger les niveaux élevés/critiques avant exposition. L’audit NuGet transitif et `npm audit` sont exécutés séparément ; les outils npm de QA ne sont pas embarqués dans l’image .NET finale.

Lighthouse local ne remplace pas l’inspection d’URL Search Console ni la validation des données enrichies sur le domaine public. Google peut imposer des recommandations supplémentaires (images, auteur, avis) : ces données ne sont pas inventées et ne sont pas nécessaires à l’usage privé.


## Mise à jour de la charte rouge et noire

Interface : noir #111111, surfaces #171717/#1F1F1F, accents #EF4444 et actions #B91C1C, navigation active soulignée, repères rouges et cartes arrondies. Contrôle navigateur de la semaine à 390 et 1440 px sans débordement horizontal ; captures `qa/charte-semaine-390.png` et `qa/charte-semaine-1440.png`.

PDF : bandeau noir, marque rouge et blanche, sections noires avec filet rouge, avertissements rouges, corps noir sur blanc et pagination avec en-tête répété. Police grasse embarquée. Exemples actualisés : `qa/charte-courses.pdf` et `qa/charte-repas.pdf`. Les 19 tests passent après modification du moteur PDF. Les anciens scores Lighthouse ci-dessus correspondent au contrôle initial ; ils n’ont pas été remesurés pour cette modification graphique.

Les exports déjà finalisés conservent leur présentation historique. Finaliser une nouvelle version de la semaine pour obtenir les nouveaux PDF.

## Refonte de la composition des écrans et des PDF

La version actuelle remplace les bandeaux noirs des PDF par une mise en page éditoriale : grands titres, grille A4, marges de 44 points, numérotation des sections, colonnes dédiées aux quantités, cases vectorielles, tableau déjeuner/dîner, puis chapitre de préparations. Les sauts de page conservent le contexte et la pagination totale. Les données de production sont transmises au moteur sous forme structurée ; elles ne sont plus déduites de lignes de texte.

Interface : navigation latérale sur ordinateur, navigation compacte sur téléphone, indicateurs de semaine, créneaux alignés, états vides allégés, commandes secondaires et réglages des courses repliés. Les couleurs de la charte restent noir, charbon et rouge, avec des tons neutres chauds.

Contrôles de cette refonte : 21 tests xUnit, dont listes PDF longues avec vérification des limites de colonnes, et planning/préparations sur des pages séparées. Vérification Chromium des cinq écrans à 320, 390, 768 et 1440 px sans débordement horizontal. Parcours à 390 et 1440 px : ajout de repas, modification des portions, ajustement d’une ligne de courses, achat persistant, rappel et téléchargement des deux PDF. Ce contrôle graphique utilise le mode Development en HTTP local ; il ne remesure pas les scores Lighthouse historiques ni la configuration HTTPS de production.

Reproduction des aperçus (après `dotnet build` et `npm ci`) : `node tools/preview-design.mjs`. Le script crée une base isolée sous `.qa`, génère un mot de passe aléatoire en mémoire et arrête le serveur à la fin. Définir CHROME_PATH pour un Chromium existant, ou installer le navigateur correspondant avec `npx playwright install chromium`.

Aperçus actualisés : `qa/charte-semaine-1440.png`, `qa/charte-semaine-390.png`, `qa/studio-shopping.png`, `qa/studio-recipes.png`, `qa/studio-batch.png`, `qa/studio-settings.png`, `qa/charte-courses.pdf`, `qa/charte-repas.pdf`.

## Correction des délais de synchronisation BookStack

La synchronisation est exécutée par un service de fond avec scope EF Core propre. Le POST de démarrage n’attend plus l’API BookStack et le suivi JSON, réservé aux administrateurs et non mis en cache, expose l’état, la phase et le nombre de fiches lues. Un verrou dans la file refuse les lancements simultanés dans le conteneur applicatif. Fermer le navigateur ne fournit plus le jeton d’annulation de la tâche.

Tests ajoutés : API volontairement bloquée alors que le POST répond déjà, annulation de la requête de lancement sans annuler l’import, clic concurrent refusé, succès final et progression ; accès anonyme/membre refusé au suivi ; échec HTTP 504 de l’API après lecture de la liste sans import partiel et possibilité de relancer avec succès.

Vérification Chromium locale avec API simulée : lancement, suivi JavaScript/JSON, affichage du bilan, réactivation du bouton et rechargement explicite du catalogue ; écrans d’administration contrôlés à 390 et 1440 px. Captures `qa/bookstack-sync-390.png` et `qa/bookstack-sync-1440.png`. Le reverse proxy du VPS et l’instance réelle restent à vérifier sur place.

Limite documentée : file et suivi en mémoire, adaptés au conteneur unique de Compose. Un arrêt du conteneur impose de relancer explicitement une tâche interrompue. Aucun changement de schéma ni migration supplémentaire pour cette correction.


## Extraction structurée du carnet BookStack

Le texte intégral de la poêlée provençale fourni dans la conversation est conservé comme fixture de test. Résultat vérifié : 11 lignes, 4 portions, poulet 600 g, eau 120 ml, temps actif 20 min, total 45 min, kcal estimées 610, repère source de réfrigération 48 h, instructions de service et huit étapes distinctes. Les ingrédients évoqués seulement dans les substitutions et les saveurs ne sont pas ajoutés au tableau. Le temps de cuisson global, la durée de congélation et les consignes de décongélation manquantes restent inconnus. Les réserves sur les allergènes et le caractère non testé de la source restent visibles dans la revue ; aucune fiche n’est validée ou publiée automatiquement.

Contrôles supplémentaires : conversion HTML conservant tables et listes, branche API HTML sans Markdown, décimales et fractions, quantités ambiguës, portions contradictoires, classement explicite des catégories, conservation des valeurs locales et de leurs effacements, provenance des kcal, préremplissage d’une page inchangée déjà importée, idempotence, invalidation après changement de la source, sauvegarde/validation du formulaire conservant les métadonnées et sauvegarde d’un brouillon incomplet. Calculs de courses vérifiés sur les ingrédients extraits : poulet 300 g et eau 60 ml pour deux portions ; un seul lot de quatre portions desservant deux repas reste compté une fois. Les allergènes non vérifiés et les équipements inconnus restent exclus des filtres concernés.

Une migration `StructuredRecipeImport` ajoute `Recipes.ImportStateJson`. Le script SQL PostgreSQL est régénéré et le modèle doit correspondre à la dernière migration. La migration et le parcours contre l’instance BookStack réelle restent à exécuter sur le VPS ; les tests de persistance locaux emploient SQLite. Le préremplissage des anciennes fiches est déclenché par une nouvelle synchronisation, sans nécessiter de modifier les pages BookStack.

## Activation privée des imports en lot — 18 novembre 2026

- Ajout de `/Admin/ReviewImports` : analyse des brouillons BookStack, aperçu sans modification, confirmation explicite, ticket signé limité à 30 minutes et lié à l’administrateur. Le lot est rejeté si une révision a changé ; l’enregistrement EF est transactionnel.
- Migration `20261118212704_PrivateRecipeAvailability` : booléen distinct du statut éditorial, faux par défaut. Aucun changement automatique de publication ou de vérification d’allergènes. Une source modifiée retire cette disponibilité ; une correction locale qui rend la fiche incomplète la retire également.
- Catalogue privé paginé à 24 fiches, administration/revue à 50, choix du planning limités à 50, recherche sur le catalogue entier. Les données complètes sont chargées pour les fiches affichées et les préparations sauvegardées.
- 65 tests réussis, dont activation de 2 600 fiches, conservation du statut Brouillon et des allergènes non vérifiés, exclusion du catalogue public, filtres allergènes, affectation au planning, pagination, confirmation, ticket invalide, révision périmée, accès administrateur et antiforgery.
- Build Release réussi sans avertissement ; modèle EF conforme aux migrations ; SQL PostgreSQL régénéré. Tests exécutés avec SQLite, sans migration d’une base PostgreSQL ni déploiement distant.
- Chromium : aperçu, case décochée, confirmation et activation privée vérifiés à 390 et 1440 px, sans débordement horizontal. Captures `docs/qa/activation-imports-390.png` et `activation-imports-1440.png`.

### Renforcement du tri des pages BookStack

Extracteur version 2 : l’activation en lot exige une classification source `recipe` (tableau d’ingrédients et préparation numérotée contenant une instruction de cuisine reconnue). Une classification ancienne ou absente exige une resynchronisation. Les pages de liaison, guides et formats non reconnus restent en revue manuelle ; une réanalyse non reconnue retire une ancienne activation privée. La classification est stockée dans le JSON d’import existant, sans nouvelle migration.

70 tests réussis : ajout de cinq cas accueil/index/guide/liste de liens, plus exclusion du lot de pages aux champs artificiellement complets mais non reconnues, et des anciens imports non classés. Les cas recette fournie et HTML restent reconnus. Ce tri est volontairement conservateur : une recette de format différent peut demander une revue manuelle.

### Mode publication publique des imports

Ajout du mode public à la revue en lot : les brouillons et recettes validées importés peuvent être publiés après confirmation, uniquement si la source est reconnue comme recette et les champs obligatoires/allergènes vérifiés. Statut Publiée et slug sont enregistrés atomiquement, sans modifier les données d’allergènes ni ouvrir automatiquement le catalogue. Le ticket signé inclut la destination ; un ticket privé ne peut autoriser la publication. Liste publique paginée à 24 fiches et recherche dans tout le catalogue publié.

Test d’intégration supplémentaire : 26 fiches publiées, fiches inconnues/non vérifiées exclues, destination du ticket contrôlée, URL publique/JSON-LD/sitemap et pagination vérifiés. Chromium : aperçu, confirmation et accès public vérifiés à 390 et 1440 px ; captures `publication-imports-390.png` et `publication-imports-1440.png`. Build Release sans avertissement. Aucune publication distante effectuée.

## Catalogue fluide et planification depuis une fiche — 18 novembre 2026

- Recherche visible avec grille actualisée après 300 ms, annulation des requêtes périmées et maintien du focus. Filtres préchargés, protéines déterminées depuis les ingrédients, kcal min/max, durées, tri, options avancées ; fonctionnement conservé sans JavaScript.
- Suggestions et tri par nombre de repas de la semaine sélectionnée, limités au foyer connecté. Pagination 12/24/48, fiches détaillées sur une page distincte, URL de retour locale validée.
- Affectation à une date et un créneau, 1 à 20 personnes ; restes = double quantité préparée et repas réservé le lendemain. Pas d’écrasement des créneaux occupés, protection antiforgery, isolation des foyers et concurrence EF.
- Passage dimanche/lundi : lot préparé compté une seule fois dans les courses, report de restes sans rachat, réservations empêchant une double affectation. La préparation partagée conserve sa date et son volume. Supprimer le repas du dimanche ne supprime pas les courses nécessaires aux restes réservés du lundi.
- Vérification Chromium à 320, 390, 768 et 1440 px sans débordement : recherche sans perte de focus, filtres, 48 cartes, pagination, page détaillée, ajout de trois personnes avec six portions et deux repas. Captures `catalogue-recettes-{390,1440}.png` et `fiche-recette-{390,1440}.png`.
- Modèle EF inchangé : compléments des préparations dans le JSON existant, aucune nouvelle migration. Tests et exécution locale SQLite ; aucun déploiement distant ni validation PostgreSQL en fonctionnement.


Validation finale : 76 tests réussis en Release, compilation sans avertissement ni erreur.
