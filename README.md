# Cod & Eat

Application française de planification culinaire en **ASP.NET Core Razor Pages / .NET 10**, Identity, EF Core et PostgreSQL. Thème local inspiré des couleurs demandées pour mael-badet.fr. Aucun service distant n’est nécessaire pour la démonstration. Aucun déploiement ni envoi de messages à des tiers n’a été effectué.

## Démarrage local immédiat (PowerShell, .NET 10)

Depuis la racine du dépôt :

```powershell
dotnet restore
dotnet build
powershell -ExecutionPolicy Bypass -File tools/start-demo.ps1 -Initialize
```

Le script demande les deux adresses et mots de passe, crée **deux comptes dans un même foyer**, puis charge explicitement les recettes et la semaine de démonstration. Aucun identifiant n’est prédéfini. Ouvrir **http://localhost:5100**. Pour les démarrages suivants :

```powershell
powershell -ExecutionPolicy Bypass -File tools/start-demo.ps1
```

Le fichier SQLite local est dans `src/CodEat.Web/codeat-demo.db` (ignoré par Git). SQLite est uniquement un secours de démonstration et de tests : la configuration normale utilise PostgreSQL. Le script exige un dépôt neuf à l’initialisation ; il refuse de recréer les comptes existants. Lancer l’initialisation avant le premier accès web. `--seed-demo` seul permet d’ajouter les exemples à une base initialisée vide. Le chargement est idempotent tant que le catalogue contient déjà des recettes et ne s’exécute jamais au démarrage normal.

## Parcours disponible

1. **Ma semaine** : sept jours, midi/soir, portions de deux par défaut ; ajout/remplacement, retrait, déplacement, lots partagés, propositions dans les créneaux libres et recherche avec filtres. Une nouvelle affectation à un lot existant ne rajoute pas une préparation. Les quantités affectées ne peuvent dépasser le lot.
2. **Recettes** : fiches validées et publiées ; l’administration permet de créer, compléter, valider, publier et dépublier en repassant en brouillon. Une sauvegarde de fiche retire sa validation. Les valeurs inconnues restent inconnues dans les filtres.
3. **Courses** : agrégation par référence canonique, rayon et famille d’unité ; lignes manuelles, achat, stock, ajustement et confirmation des changements ; rappels à traiter et copie texte.
4. **Batch cooking** : préparations uniques, portions affectées, date de préparation modifiable, cumul des temps actifs, appareils dédupliqués, consignes et alertes de conservation calculées pour chaque repas.
5. **Finaliser** : produit une nouvelle version et deux vrais PDF A4. Les liens restent attachés au foyer et les documents ne changent plus après finalisation. Les éléments à vérifier exigent une confirmation explicite et restent visibles dans le document.
6. **Réglages** : appareils, règles de produits récurrents et invitations à usage unique valables 48 heures. Deux membres maximum sont garantis par une contrainte de base. Créer un nouveau lien ne consomme pas le précédent ; le premier second membre inscrit occupe la seule place disponible.
7. **Administration** : synchronisation BookStack, alias exacts et deux commutateurs indépendants pour catalogue et inscriptions publiques, désactivés au départ. SMTP doit être configuré avant d’activer les inscriptions dans l’interface.

Le jeu de démonstration présente le cas de quatre portions préparées le dimanche et réparties lundi midi / mardi soir. Les courses comptent 600 g de cabillaud et 1 kg de pommes de terre une seule fois. La conservation saisie de 48 h déclenche une alerte pour le mardi soir : ce sont des données de test, pas des durées conseillées.

## Choix métier documentés

- Locale `fr-FR`, lundi comme début de semaine, dates de semaines en `DateOnly`. Instants persistés en UTC (création des exports, expiration des invitations, préparation), saisie et affichage des préparations en Europe/Paris. Midi signifie **12 h 30**, soir **19 h 30**, heure de Paris. Les heures de consommation ne sont pas personnalisables dans cette version.
- Un lot copie la fiche utilisée lors de son ajout. Une modification du catalogue ne modifie pas les lots déjà créés : remplacer le repas par une nouvelle préparation pour reprendre une fiche révisée. Une version finalisée conserve aussi le JSON complet et les octets des PDF.
- Concurrence optimiste sur la semaine, la fiche recette et la consommation des invitations ; réponse 409 et lien de rechargement pour la semaine. Toute mutation du planning/courses/rappels d’une semaine vérifie sa révision. Les mises à jour simples des paramètres globaux restent « dernière sauvegarde gagnante ».
- Ingrédients : trim, minuscules et espaces normalisés, puis alias **exact** administré ; aucune similarité linguistique. g/kg et ml/cl/L sont convertis ; pièces, cuillères, masse et volume restent distincts. Calculs en `decimal`. Pas de conversion automatique en conditionnements marchands. Une quantité qualitative est signalée à vérifier, même si d’autres quantités chiffrées sont disponibles.
- Cadence des rappels en mois civils : numéro du mois `année * 12 + mois - 1`, divisé par la cadence et arrondi vers le bas. Le lundi de la semaine détermine la période et la saison ; hiver = décembre–février, automne = septembre–novembre. Une exception mensuelle prime sur la saison, qui prime sur la quantité habituelle. La cadence reste annuelle constante ; les exceptions modifient la quantité. Pour changer une cadence saisonnière, remplacer la règle dans les réglages. Supprimer une règle conserve les rappels déjà générés. Un report clôt l’occurrence courante jusqu’à la période suivante. Ouvrir la page crée l’occurrence mais ne la traite pas. Unicité `(foyer, règle, période)` en base et identifiant stable pour la ligne ajoutée.
- Les règles se remplacent en supprimant puis recréant une règle ; il n’y a pas d’éditeur en place. Les occurrences déjà créées gardent leur quantité d’origine.
- BookStack reste propriétaire du titre et du contenu éditorial. Les champs préremplis peuvent suivre les mises à jour de la source tant qu’ils n’ont pas été corrigés localement ; les corrections et suppressions locales sont conservées. Les anciennes fiches sans métadonnées gardent leurs valeurs déjà renseignées. Le contenu externe est affiché **en texte encodé**, jamais en HTML actif. Le modèle de page et les routes vérifiées sont dans [docs/bookstack-modele.md](docs/bookstack-modele.md).
- PDFsharp **6.2.3**, sous licence **MIT** pour cette version (ne pas mettre à niveau vers les versions à licence différente sans revue), et police DejaVu Sans intégrée pour les accents. Licences dans `docs/licenses/`. Les PDF utilisent une composition éditoriale A4 : grands titres, marges régulières, numérotation des sections, tableaux alignés et accents rouges discrets. Les courses présentent les produits et les quantités en colonnes avec cases à cocher dessinées ; les repas sont organisés dans un tableau hebdomadaire, suivi de pages consacrées aux préparations. Les en-têtes de continuation et les pieds de page sont répétés ; le corps reste noir sur blanc, lisible en impression monochrome ; aucune dépendance à une imprimante ou à un navigateur serveur.

## PostgreSQL et préparation Docker / VPS

Fournis : Dockerfile multi-stage, utilisateur final non-root, migrations EF Core, volume PostgreSQL 18 et volume des clés Data Protection. PostgreSQL n’expose aucun port ; Kestrel n’est pas publié sur l’hôte.

```powershell
Copy-Item .env.example .env
# Remplir les valeurs du fichier .env, sans jamais le committer.
docker network create --subnet 172.30.0.0/24 codeat-proxy
# Exemple uniquement : PROXY_NETWORK_NAME=codeat-proxy et PROXY_NETWORK_CIDR=172.30.0.0/24.
# Si Nginx Proxy Manager utilise déjà un réseau, réutiliser son nom et son CIDR réel.
docker compose build
docker compose up -d db
docker compose run --rm app --migrate
```

Initialiser les comptes avec des variables d’environnement, sans les mettre dans Compose ni dans l’historique :

```powershell
$env:Bootstrap__Email = Read-Host 'Adresse administrateur'
$env:Bootstrap__Password = [System.Net.NetworkCredential]::new('', (Read-Host 'Mot de passe' -AsSecureString)).Password
$env:Bootstrap__SecondEmail = Read-Host 'Adresse du second membre'
$env:Bootstrap__SecondPassword = [System.Net.NetworkCredential]::new('', (Read-Host 'Mot de passe du second membre' -AsSecureString)).Password
docker compose run --rm -e Bootstrap__Email -e Bootstrap__Password -e Bootstrap__SecondEmail -e Bootstrap__SecondPassword app --bootstrap
Remove-Item Env:Bootstrap__Password,Env:Bootstrap__SecondPassword
docker compose up -d app
```

Ajouter `--seed-demo` à la commande d’initialisation seulement si souhaité. En production normale, laisser `Demo__Sqlite` et `BookStack__Simulated` absents ou faux. Les comptes initialisés sont déjà confirmés ; les nouveaux inscrits doivent confirmer leur adresse en production.

Dans Nginx Proxy Manager : connecter le proxy au réseau déclaré, destination **http://app:8080**, domaine `PUBLIC_HOST`, terminer TLS, transmettre `Host`, `X-Forwarded-For` et `X-Forwarded-Proto`. Utiliser un réseau proxy dédié et de confiance : tous les conteneurs de ce CIDR peuvent émettre ces en-têtes. Préférer `Proxy__KnownProxies__0` pour une IP de proxy fixe si possible (retirer alors la variable KnownNetworks de Compose). Sans proxy explicitement configuré, les en-têtes transférés ne sont pas traités. Ils sont traités avant redirection HTTPS et authentification. `AllowedHosts` et les URL canoniques viennent de la configuration serveur.

Après branchement réel : contrôler `https://votre-hote/health`, la connexion et les cookies Secure/HttpOnly, puis `curl -I http://votre-hote` et `curl -I https://votre-hote/Account/Login`. HTTP doit rejoindre HTTPS sans boucle 307. Kestrel reste joignable seulement via les réseaux Docker. La vérification de cette topologie nécessite votre moteur Docker/VPS.

Migrations supplémentaires :

```powershell
dotnet tool restore
dotnet ef migrations add NomDuChangement --project src/CodEat.Web
dotnet ef database update --project src/CodEat.Web
```

Pour les commandes EF hors Docker, renseigner `ConnectionStrings__Database` avec une base PostgreSQL accessible. Le démarrage ne migre jamais implicitement la production : exécuter `--migrate` avant chaque nouvelle version.

Sauvegarde/restauration (shell Linux, archive des clés à protéger comme un secret) :

```sh
docker compose exec -T db pg_dump -U codeat -d codeat -Fc > codeat.dump
docker compose cp app:/keys ./keys-backup
# Restaurer dans une base dédiée/vide, application arrêtée :
docker compose stop app
docker compose exec -T db pg_restore -U codeat -d codeat --clean --if-exists < codeat.dump
# Si restauration sur un nouvel hôte, remettre aussi les clés dans le volume /keys.
docker compose start app
```

## SMTP, confirmation et récupération

### Diagnostic du premier démarrage PostgreSQL

Npgsql 10 peut afficher `Cannot load library libgssapi_krb5.so.2` lorsqu’il tente une négociation GSSAPI sans Kerberos installé. Compose utilise explicitement `GSS Encryption Mode=Disable` pour notre PostgreSQL interne authentifié par mot de passe ; ce paramètre est distinct du réglage TLS. Référence : https://www.npgsql.org/doc/release-notes/10.0.html.

Sur une base neuve, un message `Failed executing DbCommand` pour la lecture de `__EFMigrationsHistory` peut précéder la création de cette table et la réussite des migrations. Le fragment seul ne prouve pas un échec : vérifier la suite du journal, le code de sortie de la commande et le message `Base de données prête : initialisation et migrations terminées.`. Les autres erreurs SQL ne doivent pas être ignorées.

Après mise à jour du code/configuration, exécuter `docker compose build app`, puis `docker compose run --rm app --migrate`. En cas d’échec, relever les dernières lignes de la sortie, notamment l’exception et le code SQLSTATE, sans partager les secrets. Ne pas supprimer le volume pour résoudre ce diagnostic.

Renseigner `Smtp__Host`, `Port`, `From`, `User`, `Password`, `Tls`. Les liens utilisent `Public__Origin` (HTTPS). Les jetons de confirmation et de réinitialisation sont fournis par Identity. Pour tester localement avec Mailpit :

```powershell
docker compose --profile mail up -d mailpit
$env:Smtp__Host='localhost'
$env:Smtp__Port='1025'
$env:Smtp__Tls='false'
$env:Smtp__From='codeat@localhost.test'
```

Lire les messages sur `http://localhost:8025`. Pour suivre les liens en HTTPS, configurer une origine locale HTTPS et un certificat de développement. Depuis l’application Docker, utiliser `SMTP_HOST=mailpit`, `SMTP_PORT=1025`, `SMTP_TLS=false`. Pour les tests SMTP de confirmation en mode Development, suivre les liens même si la connexion locale n’exige pas la confirmation. La production l’exige. Aucun message réel n’a été envoyé pendant cette intervention.

## Sécurité et SEO

Authentification par défaut sur toutes les pages métier ; rôle Admin pour tout le dossier d’administration ; aucun identifiant de foyer fourni par le client n’est accepté. Les exports/rappels sont recherchés avec le foyer de l’utilisateur connecté. Antiforgery sur les POST ; cookies HttpOnly et Secure en production ; verrouillage Identity et limitation par IP (15 requêtes/minute sur les formulaires de compte). Les pages et PDF privés sont `no-store`/`noindex`. Les erreurs de connexion et de récupération ne révèlent pas si une adresse existe. CSP locale sans CDN ni exécution du contenu importé. Les clés Data Protection sont persistées ; chiffrer/protéger le volume et ses sauvegardes au niveau de l’hôte. Aucun secret ni contenu de formulaire n’est explicitement journalisé.

Catalogue rendu côté serveur à `/catalogue`, détails `/catalogue/id/slug`, métadonnées propres, canonique HTTPS, Open Graph, sitemap et robots selon le mode. Les anciens slugs mémorisés redirigent en 301 si la recette reste publiée. Le JSON-LD reprend les ingrédients, étapes et valeurs disponibles, et omet les valeurs inconnues. Les variantes à paramètres sont `noindex,follow` et canoniques vers la page sans paramètres. Les filtres et la recherche métier restent privés. Aucun visuel distant n’est utilisé.

## Vérifications et limites

```powershell
dotnet test
dotnet build -c Release
dotnet list src/CodEat.Web package --vulnerable --include-transitive --no-restore
npm.cmd ci
npm.cmd audit
powershell -ExecutionPolicy Bypass -File tools/local-certificate.ps1
# Utiliser un Chromium Playwright installé :
$env:CHROME_PATH='C:\chemin\vers\chrome.exe'
node tools/browser-qa.mjs
```

Le script navigateur crée une base `.qa` isolée et deux comptes à mot de passe aléatoire en mémoire, démarre l’application en **Production HTTPS**, contrôle les parcours, puis arrête son serveur. Le certificat de test autosigné est valable sept jours et n’est jamais destiné au VPS. Si Chromium n’est pas installé : `npx.cmd playwright install chromium`, puis omettre CHROME_PATH pour utiliser le navigateur Playwright par défaut.

**Résultats réels :** voir [docs/verification.md](docs/verification.md), [rapports Lighthouse et PDF](docs/qa/). Le moteur Docker de cette machine est arrêté : la construction/inspection de l’image et les migrations sur un serveur PostgreSQL n’ont pas été exécutées. Les tests de base locaux utilisent SQLite. SMTP réel, instance BookStack, réseau proxy et domaine public restent à vérifier sur votre infrastructure. Les tests des jetons de confirmation, invitations et récupération métier ne remplacent pas un test de délivrabilité SMTP. L’inspection Search Console exige votre domaine et votre compte Google.

Valeurs réelles à fournir : **URL BookStack HTTPS, ID du livre, Token ID et Token Secret du compte technique, nom d’hôte public, nom/CIDR ou IP du réseau proxy approuvé, mot de passe PostgreSQL et paramètres SMTP**. Ne les déposer ni dans Git ni dans les rapports de tests.



### Contrôler la connexion BookStack

Sur le VPS, après mise à jour du code : `docker compose build app`, puis `docker compose run --rm app --check-bookstack`. Ce contrôle est en lecture seule et n’affiche pas les jetons ni l’URL interne. La synchronisation dans Administration affiche également un code d’erreur précis. Voir les codes et les vérifications dans [docs/bookstack-modele.md](docs/bookstack-modele.md).

### Synchronisation BookStack sans attente du proxy

La synchronisation s’exécute maintenant en tâche de fond, avec compteur de fiches et bilan visibles dans Administration. Fermer la page ne l’arrête pas. Le suivi et la file sont en mémoire dans l’unique conteneur applicatif ; après un redémarrage du conteneur, relancer un import interrompu. Le catalogue reste persistant et les imports restent atomiques. Voir [les limites et le diagnostic des 504](docs/bookstack-modele.md).

### Après l’import des recettes

Les recettes importées arrivent en brouillon. Le format de carnet fourni est désormais reconnu en Markdown et en HTML : tableaux d’ingrédients, quantités/unités/états au pesage, portions, catégorie, tags, temps explicites, kcal et provenance, étapes, conservation et service sont préremplis lorsqu’ils sont indiqués. Les valeurs manquantes restent inconnues et les allergènes ne sont jamais confirmés automatiquement. Ouvrir **Administration → Brouillons à compléter**, relire les points à vérifier, confirmer les allergènes après vérification, puis choisir **Valider explicitement la fiche**. Elles deviennent utilisables par le foyer ; il n’est pas nécessaire de les publier.

Pour activer ce préremplissage sur une installation existante, après transfert du code :

```sh
docker compose build app
docker compose run --rm app --migrate
docker compose up -d --force-recreate app
```

Relancer ensuite **Synchroniser BookStack** : les fiches déjà importées seront préremplies même si leur contenu source n’a pas changé. Les valeurs locales déjà renseignées sont conservées sur les anciennes fiches ; les champs remplis automatiquement sont suivis séparément pour les imports futurs. Voir [le format et les règles de conservation des corrections](docs/bookstack-modele.md).



### Utiliser un grand catalogue importé dans le planning privé

Après mise à jour du code, exécuter sur le serveur :

```bash
docker compose build app
docker compose run --rm app --migrate
docker compose up -d --force-recreate app
```

Dans Administration, choisir **Examiner et activer les imports en lot** (`/Admin/ReviewImports`). L’aperçu compte les fiches structurellement complètes et les fiches à compléter. Confirmer le lot rend les fiches complètes disponibles dans le planning privé, sans les publier ni modifier leur statut Brouillon ou leur vérification d’allergènes. Si les ingrédients sont encore absents, resynchroniser BookStack pour appliquer l’extraction structurée avant de préparer le lot. Les corrections locales restent conservées.

Un aperçu expire après 30 minutes et contient les identifiants et révisions exacts des fiches. Une fiche modifiée entraîne le rejet du lot entier. Une modification ultérieure de la source désactive l’accès privé de la fiche concernée jusqu’à une nouvelle revue. Les exclusions d’allergènes continuent d’écarter toutes les fiches non vérifiées ; les écrans et exports conservent les avertissements. La finalisation des exports demande de confirmer ces éléments.

Le catalogue privé affiche 24 recettes par page, l’administration et la revue 50. Le planning propose au maximum 50 choix répartis entre les catégories ; utiliser les filtres pour accéder aux autres recettes. La recherche porte sur tout le catalogue disponible.

### Pages de liaison et guides BookStack

L’activation en lot exige désormais une classification source `recipe` issue de l’extracteur version 2. Un tableau d’ingrédients seul ne suffit pas : la source doit également contenir une préparation numérotée avec une instruction de cuisine reconnue. Les index, accueils, listes de liens et guides restent conservés dans l’administration, hors du lot d’activation. Les formats ambigus passent en revue manuelle ; ce contrôle privilégie l’exclusion en cas de doute, sans prétendre comprendre toute recette possible.

Après cette mise à jour, relancer une synchronisation BookStack avant l’activation en lot : même les pages inchangées sont réanalysées. Les anciens imports sans classification ne peuvent pas être activés en lot. Aucun titre de pays ou de page n’est utilisé seul pour décider. Une page non reconnue ne conserve pas une ancienne activation privée après réanalyse.

### Publication publique des imports en lot

Pour publier les imports, utiliser **Administration → Examiner et publier les imports en lot** (`/Admin/ReviewImports?Mode=public`), après resynchronisation avec l’extracteur version 2. Ce mode inclut les brouillons et fiches validées importés, même déjà disponibles dans le planning privé. Il exige une vraie recette reconnue, les champs structurés obligatoires et les allergènes explicitement vérifiés sur la fiche. Les pages de liaison et les formats non reconnus restent hors du lot. La publication ne coche jamais la vérification des allergènes.

Préparer l’aperçu puis confirmer la publication publique. Un ticket de planning privé ne peut pas autoriser une publication. Les fiches publiées reçoivent leur statut et URL publique ; les anciens slugs restent redirigés. Le catalogue doit aussi être ouvert dans **Administration → Ouverture au public → Catalogue public**. Cette ouverture reste un réglage explicite distinct. La liste publique affiche 24 fiches par page et permet de rechercher dans tout le catalogue publié.

Si toutes les fiches restent bloquées sur « allergènes vérifiés », leur import n’a pas remplacé cette revue : ouvrir et vérifier les fiches avant de préparer un nouveau lot public.

### Recherche de recettes et ajout au planning

L’onglet Recettes propose une barre de recherche actualisée sans rechargement complet, des suggestions fondées sur les repas du foyer dans la semaine affichée et des filtres préchargés (Terre/Mer, viandes/poissons/autres protéines réellement présentes dans les ingrédients, kcal minimum/maximum, durée, ingrédients, allergènes et appareils). Les exclusions d’allergènes continuent de rejeter les fiches non vérifiées. Sans JavaScript, le bouton Rechercher et la pagination restent fonctionnels.

Affichage initial de 12 cartes, choix de 24 ou 48 par page ; tri par fréquence dans la semaine du foyer, nom, durée ou kcal. Les calories inconnues sont exclues lorsqu’une borne est active. Les fiches complètes sont accessibles sur `/recettes/{id}`, avec retour aux filtres du catalogue et accès réservé aux membres connectés.

Depuis la fiche, choisir la date, Midi/Soir et le nombre de personnes (1–20), puis Ajouter à ma semaine. L’option Restes double les portions à cuisiner et ajoute le même nombre de portions au midi du lendemain. Les créneaux occupés ne sont pas écrasés. Les courses comptent le lot une seule fois. Du dimanche au lundi, les courses restent dans la semaine de cuisson ; les portions de chaque semaine sont réservées et ne peuvent pas être réaffectées ailleurs. La date et le volume de ces lots partagés sont conservés pour éviter de désynchroniser les deux semaines.

Ces données supplémentaires sont enregistrées dans les instantanés JSON existants : aucune nouvelle migration de base n’est nécessaire pour cette évolution. Déployer le code puis reconstruire et recréer le service app.

### Recettes complètes depuis le planning, le batch cooking et les PDF

Le titre de chaque repas et le lien « Voir la recette complète » ouvrent une fiche dédiée sur `/preparations/{weekId}/{preparationId}`. Le batch cooking propose le même accès. La fiche reprend l’instantané du planning du foyer : elle reste lisible si la recette du catalogue change ou repasse en brouillon. Elle présente les repas associés, les ingrédients ajustés au volume du lot, toutes les étapes, les allergènes, les durées, les ustensiles et la conservation.

Le PDF des repas conserve la vue hebdomadaire et ajoute les ingrédients et les étapes complètes pour chaque préparation, avec continuation sur plusieurs pages si nécessaire. Les quantités d’ingrédients sont adaptées au lot ; le texte des étapes est conservé tel quel et une note signale les quantités de référence lorsque les portions changent. Une quantité inconnue reste à préciser. Le PDF des courses conserve sa fonction de liste de courses.

Les versions PDF déjà finalisées sont immuables. Après déploiement, finaliser une nouvelle version pour obtenir le document enrichi. Les anciens plannings ne disposant pas des ingrédients ou étapes complets indiquent les données manquantes ; replanifier la recette à partir du catalogue enrichi pour disposer d’un nouvel instantané complet. Aucune migration supplémentaire n’est nécessaire.

### Curseur de calories

Les deux champs kcal du catalogue sont remplacés par une barre horizontale à deux poignées, avec valeurs visibles, clavier natif et recherche automatique. L’échelle est calculée à partir des calories connues du catalogue et des bornes de l’URL, avec un minimum d’échelle de 1 200 kcal. Les extrémités signifient sans minimum/sans maximum : une recherche sans borne conserve les recettes aux calories inconnues. Les bornes ne peuvent pas se croiser. Sans JavaScript, deux champs numériques restent disponibles.

### Photos optionnelles des recettes

Les cartes et fiches privées/publiques, ainsi que les fiches des préparations planifiées, disposent d’un emplacement photo au ratio 8:5. Sans fichier local, aucun encadré ni espace vide n’est affiché. Ajouter les images dans `src/CodEat.Web/wwwroot/images/recipes/`, nommées avec l’identifiant de recette (`42.webp`, par exemple). Formats acceptés : webp, avif, jpg, jpeg, png. Les fichiers sont des assets publics livrés au déploiement ; voir le README du dossier. Les images sont chargées à la demande et leur version change avec le fichier. Une image devenue indisponible est masquée. Aucun schéma de base ni export PDF n’est modifié par cet emplacement photo.

### Navigation directe entre les semaines

Cliquer sur les dates au centre de la navigation ouvre un sélecteur de date. Choisir un jour affiche sa semaine du lundi au dimanche ; les mois et années sont accessibles via le calendrier natif du navigateur. Les flèches restent disponibles et le raccourci « Cette semaine » revient à la semaine courante à Paris. Le formulaire conserve l’onglet et les filtres. Sans JavaScript, ouvrir le sélecteur, choisir la date puis cliquer Afficher.

