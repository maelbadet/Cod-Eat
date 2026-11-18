# BookStack — modèle de recette

Une page correspond à une recette. Son titre et son contenu éditorial sont gérés dans BookStack. Exemple de titre : « Cabillaud citron & pommes de terre ».

## Description
Un plat familial pour deux repas.

## Ingrédients (4 portions)
- Cabillaud : 600 g
- Pommes de terre : 1 kg
- Citron : 1 pièce
- Sel : selon le goût

## Étapes
1. Laver et couper les pommes de terre.
2. Les cuire au four avec l’huile.
3. Ajouter le poisson et le citron ; poursuivre jusqu’à cuisson complète.

Après import, compléter les données structurées dans Administration. Le contenu libre reste du texte encodé : aucun HTML externe n’est exécuté. L’import ne tente pas de deviner les quantités, allergènes ou durées de conservation. Une recette arrive en brouillon. Le titre et les étapes d’une fiche importée se corrigent dans BookStack ; les autres champs se corrigent dans Cod & Eat. Une mise à jour éditoriale repasse la fiche en brouillon sans toucher aux données structurées ni aux copies déjà planifiées.

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
