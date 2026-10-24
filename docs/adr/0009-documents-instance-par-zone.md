# ADR 0009 — Documents : une instance par zone de sensibilité, clés racines HSM et chiffrement par enveloppe

## Statut
Acceptée le 2026-10-24 par le responsable produit (écart au §14.3 validé). Références : rouffou/veilla#356, #359.

## Contexte
ARC-04 exige des réseaux, des bases, des clés de chiffrement et des droits distincts pour chaque zone (standard, médicale,
psychosociale). NF-03 exige des clés distinctes pour les données médicales et psychosociales. ARC-44 exige que les clés des
zones sensibles soient protégées par HSM avec une rotation automatique. ARC-06 interdit qu'une donnée clinique quitte sa
zone par un événement ou une API. Le §14.3 place Documents en zone standard (« contenu chiffré par zone ») et le §14.4 lui
confie DOC-01 et DOC-02.

Le câblage Azure (#352, #355) a montré que l'implémentation actuelle ne respecte pas ces exigences :
- **Clés lues d'une autre zone.** Documents, en zone standard, lit les clés AES des coffres médical et psychosocial, avec le
  rôle Key Vault Secrets User sur ces coffres. L'isolation des clés par zone est rompue. De plus, les points de terminaison
  privés de ces coffres n'admettent que l'environnement de leur zone (NSG `pe-<zone>`) : les références de secrets ne se
  résolvent pas.
- **Contenu clinique traité en zone standard.** Pour la copie du dossier (SAN-43) et le carnet de vaccination (SAN-50),
  Documents lit le contenu clinique auprès de Surveillance médicale et le fusionne en zone standard. C'est contraire à
  ARC-06, et le NSG de `cae-medicale` le bloque, puisque seule la plateforme y entre. Les pièces jointes du dossier
  (SAN-41) suivraient le même chemin.
- **Stockage unique.** Le stockage WORM est un seul compte, situé en zone standard.
- **Clés exportables.** Les clés sont des secrets Key Vault, donc exportables : ARC-44 n'est pas satisfait.

Le formulaire d'évaluation de santé (SAN-30) ne contient que les données de `DecisionEmise` (catégorie, mesures, validité),
qu'ARC-06 autorise à sortir de la zone. Seuls ses exemplaires travailleur et dossier relèvent de la zone médicale.

## Décision
1. **Un service, une image, une instance par zone de données.** Documents reste un seul contexte délimité : même code,
   même image, même pipeline (ARC-01).
   - Il est déployé une fois par zone qui produit des documents : `documents` (standard) et `documents-medicale`.
     `documents-psychosociale` sera ajouté au catalogue quand le service Psychosocial produira des documents.
   - Chaque instance a, dans sa zone : sa base sur le serveur PostgreSQL de la zone, son compte de stockage WORM (point
     privé `pe-<zone>`), sa clé racine dans le coffre de la zone et son identité managée.
   - La configuration `Documents:Zones` fixe la zone servie. Hors développement, une instance sert exactement une zone,
     égale à `SEPP__ZONE`.
   - Une instance refuse la génération, la validation de modèle et la lecture d'un document d'une autre zone.
2. **Génération au plus près des données.**
   - Les contenus cliniques (copie du dossier, carnet de vaccination, pièces jointes) sont lus, fusionnés, rendus et
     archivés par l'instance médicale, par des appels intra-zone.
   - Le formulaire d'évaluation de santé est produit par les deux instances à partir du même `DecisionEmise` : l'exemplaire
     employeur par l'instance standard, les exemplaires travailleur et dossier par l'instance médicale. Chaque instance est
     idempotente (`decision:<id>:<exemplaire>`).
   - Les modèles d'une zone vivent dans l'instance de cette zone. Ils sont validés par le CPMT dirigeant (DOC-01).
3. **Chiffrement par enveloppe, sans sortie de zone.**
   - Chaque document est chiffré en AES-256-GCM avec une clé de données aléatoire.
   - Cette clé est enveloppée (`wrapKey`, RSA-OAEP-256) par la clé racine `documents-kek` de la zone : clé RSA-HSM 3072
     non exportable dans le Key Vault Premium des zones médicale et psychosociale (ou dans leur Managed HSM), clé RSA dans
     le coffre standard.
   - La rotation est automatique, par politique de rotation, sans expiration des versions. L'objet archivé porte la version
     de la clé racine, et les anciennes versions restent utilisables pour déballer. Aucune version n'est désactivée ni
     supprimée tant que des archives l'utilisent ; une version introuvable ou refusée par le coffre est une erreur
     d'exploitation, distincte d'une archive altérée (#377).
   - Seule l'identité de l'instance de la zone reçoit le rôle « Key Vault Crypto Service Encryption User » sur cette clé.
   - Les données associées authentifiées lient l'objet à sa zone, à son document et à sa clé.
4. **Publication commune.** Les instances publient `documents.document-publie.v1` sur le topic `documents`, par un émetteur
   partagé comme pour `audit`. Les consommateurs ne changent pas.
5. **Lecture.** Les BFF interrogent l'instance de la zone (flux plateforme → zone, déjà permis).
   - Le BFF travailleur fusionne les listes des instances et lit le contenu auprès de l'instance qui détient le document.
   - Le CPMT lit les documents médicaux auprès de l'instance médicale, avec `dossier-sante:lire` et un motif.
   - Chaque lecture est journalisée dans la zone du document, avant la restitution.
6. **Aucune ouverture réseau.** Aucune règle NSG n'est assouplie. Aucun service ne référence le coffre d'une autre zone,
   ce que vérifient une validation Terraform et des tests d'architecture.

**Écart au §14.3** (« Documents : zone standard ») : validé par le responsable produit le 2026-10-24. L'écart porte sur le
déploiement, pas sur le découpage : il n'y a toujours qu'un service Documents, propriétaire de DOC-01 et DOC-02.

## Alternatives
- **Enveloppe dont Documents (standard) déballe lui-même la clé dans le HSM de la zone.** La clé racine ne sort pas de la
  zone. Mais le droit `unwrapKey` permet de tout déchiffrer, il faut ouvrir le flux `cae-standard` → `pe-medicale`, et le
  contenu clinique reste traité en zone standard (ARC-06). Écartée.
- **Enveloppe asymétrique : Documents chiffre avec la clé publique, seule la zone déchiffre.** L'isolation est bonne au
  chiffrement. Mais cette option ne règle pas la génération des contenus cliniques (SAN-43, SAN-50, SAN-41). À la lecture,
  soit la clé de données sort de la zone, soit la zone médicale lit le stockage standard, ce qui ouvre un flux réseau.
  Écartée.
- **Génération et archivage délégués aux services de zone (Surveillance médicale, Réintégration, Psychosocial).**
  L'isolation est complète. Mais DOC-01 et DOC-02 seraient dupliqués dans trois services (éditeur et validation des
  modèles, PDF/A, WORM, horodatage, signature), contrairement au §14.4 et à ARC-01. Écartée.
- **Documents entièrement en zone médicale.** Les documents standard seraient sur-classifiés. Il faudrait ouvrir le flux
  `cae-medicale` → standard pour la langue et les affiliés, et le portail employeur dépendrait de la zone médicale.
  Écartée.
- **Statu quo : une instance standard qui lit les clés des trois zones.** Contraire à ARC-04, ARC-06 et ARC-44, et bloqué
  par le NSG. Écarté.

## Conséquences
- **Terraform** :
  - attribut `instance_de` dans le catalogue des services, et entrée `documents-medicale` ;
  - un compte de stockage WORM par zone ;
  - une clé `documents-kek` par coffre, créée par azapi sur le plan de contrôle, avec le rôle porté sur la clé ;
  - suppression des rôles inter-zones et des secrets `documents--chiffrement--*` ;
  - injection du nom d'abonnement Service Bus ;
  - documentation de la procédure de sauvegarde des clés racines : leur perte rend illisibles les archives de la zone.
- **Code** : configuration `Documents:Zones`, port d'enveloppe (adaptateurs Key Vault et local), format d'archive
  `SEPPDOC2` (la lecture de `SEPPDOC1` est conservée), répartition des exemplaires du formulaire d'évaluation de santé.
- **Coût** : par zone sensible dotée d'une instance, une Container App, une base (sur le serveur existant), un compte de
  stockage et un point privé. Les clés RSA-HSM coûtent quelques euros par mois. Le Managed HSM reste facultatif.
- **Formulaire d'évaluation de santé** : il n'est plus produit dans une seule transaction. Chaque instance traite ses
  exemplaires et les rejoue indépendamment. Obligations ne dépend que de l'exemplaire employeur.
- **BFF** : le BFF travailleur appelle deux instances (fusion des listes, routage du contenu). Le BFF employeur et
  Communications ne changent pas.
- **Moindre privilège** : un rôle technique distinct, `documents-medicale`, porte `dossier-sante:transmettre-documents`.
- **Langue des documents** : l'instance médicale ne peut pas joindre Affiliés ni Personnes. La langue viendra d'une
  projection alimentée par des événements (langue de la personne et régime de l'affilié, en champs facultatifs v1). C'est
  un prérequis de production. Réalisé par le lot 7 (#366) : projection `contexte_linguistique`, configuration
  `Documents:SourceLinguistique` (`Http` | `Projection`), champs facultatifs sur `affilies.affilie-cree.v1`,
  `affilies.affilie-modifie.v1` et `personnes.occupation-debutee.v1`, nouveau contrat `personnes.langue-modifiee.v1`
  (voir `docs/architecture/evenements.md` et le README de Documents). Le rattrapage des données antérieures à la mise
  en service de la projection est suivi par #369.
- **Développement local** : une instance unique peut servir les trois zones. Le compose propose aussi `documents-medicale`.
- **Suites** : appliquer le même principe à `FieldEncryptor` (clés de données enveloppées par la clé racine HSM de la
  zone), et contrôler la relation de soin par l'instance médicale (appel intra-zone à Surveillance médicale).
