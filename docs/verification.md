# Vérifications de livraison

Les contrôles sont locaux, sur Windows avec SDK .NET 10.0.401. Aucun accès au VPS ni à l’instance BookStack réelle.

## Contrôles réalisés

- Compilation Release : réussie sans avertissement ni erreur.
- Migrations : aucun changement de modèle non migré ; SQL PostgreSQL généré dans `docs/schema-postgresql.sql`.
- Dépendances : aucun package NuGet vulnérable signalé, transitifs inclus ; `npm audit` ne signale aucune vulnérabilité. L’image Docker finale reste à scanner.
- Tests xUnit : domaine, HTTP réel dans TestServer, base SQLite relationnelle, Identity, contraintes uniques, isolation des foyers et PDF analysés avec PdfPig. Dernier passage : 19 tests réussis, aucun échec.
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
