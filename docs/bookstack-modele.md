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
