# ADR 0012 — Chiffrement des champs par enveloppe : une clé racine non exportable par service, DEK en mémoire

## Statut
Proposée le 2026-10-25 (à valider par l'architecte). Références : rouffou/veilla#208 (ARC-44), #282. Prolonge l'ADR 0009
(« Suites ») au chiffrement des champs.

## Contexte
`FieldEncryptor` (socle commun) chiffre en AES-256-GCM les champs sensibles de quatre services : Personnes (NISS, avec
un index aveugle HMAC pour la recherche, DAT-06), Intégrations (charges utiles), Surveillance médicale et Réintégration
(contenu clinique, section `ZoneMedicale:Encryption`). Ses clés AES sont des **secrets** Key Vault, injectés par
référence : elles sont exportables par quiconque lit le secret, et la clé de l'index aveugle aussi. ARC-44 n'est pas
satisfait. Documents a déjà adopté l'enveloppe (ADR 0009 : `documents-kek`, une clé de données par document).

Un champ chiffré est lu et écrit de façon synchrone par les convertisseurs EF Core, souvent par lots : un appel au coffre
par valeur (comme Documents par document) est exclu, pour la latence comme pour les quotas du coffre.

## Décision
1. **Une clé racine par service, dans le coffre de sa zone** : `<service>-fields-kek`, RSA 3072, opérations `wrapKey` et
   `unwrapKey`, créée par Terraform comme `documents-kek` (azapi, plan de contrôle) : RSA-HSM dans le Key Vault Premium
   des zones sensibles ou dans leur Managed HSM, RSA dans le coffre standard ; rotation automatique tous les 12 mois, sans
   expiration des versions. Seule l'identité du service reçoit « Key Vault Crypto Service Encryption User » **sur sa
   clé** ; le rôle « Key Vault Crypto User » au niveau du coffre est retiré. Le service reçoit l'URI de la clé sans
   version (`<Section>:Enveloppe:CleKek`, `Enveloppe:Type = KeyVault`).
   - *Par service plutôt que par zone* : un service est dans une seule zone, donc la clé reste propre à la zone (ARC-04,
     NF-03). Une clé de zone partagée donnerait à chaque service le droit de déballer les clés des autres (Surveillance
     médicale et Réintégration partagent la zone médicale) ; le coût (quelques euros par clé et par mois) est négligeable.
2. **Clés de données (DEK) enveloppées, déballées au démarrage, en mémoire.** Une DEK AES-256 par service et par période
   de rotation, identifiée (`D1`, `D2`…). Elle est stockée **enveloppée** (`<kid versionné>|<base64>`) dans un secret Key
   Vault `<service>--<préfixe>--deks--<id>` : ce secret ne se déballe que dans le coffre, par l'identité du service. Au
   démarrage, le service déballe chaque DEK configurée (un appel par DEK) et les garde en mémoire ; aucun appel au coffre
   par valeur. Le démarrage échoue si une DEK ne se déballe pas (kid étranger, clé altérée, droit manquant).
   - La DEK est produite par l'exploitation à partir de la **clé publique** de la clé racine (`openssl rand | openssl
     pkeyutl` en RSA-OAEP-256, sans fichier intermédiaire) : la DEK en clair n'est jamais écrite ni affichée (procédure
     dans `infra/README.md`).
3. **Format versionné.** Nouvelles valeurs : `v2:<dekId>:<base64(nonce|chiffré|tag)>`, données associées `v2:<dekId>`.
   Le format historique `v1:<keyId>:…` reste lisible tant que ses clés AES sont configurées en `<Section>:Keys:<id>`
   (secrets `--keys--`, `encryption_legacy_key_ids`) ; il n'est plus jamais écrit. `NeedsRotation` signale les valeurs v1
   et celles d'une ancienne DEK.
4. **Rotation.** Clé racine : automatique ; une DEK enveloppée garde son kid versionné et reste déballable (aucune version
   n'est désactivée ni supprimée, comme pour `documents-kek`, #377). DEK : créer `<préfixe>--deks--<nouvel id>` avec la
   version courante de la clé racine, placer l'identifiant en tête de `encryption_key_ids` en conservant les anciens ;
   un identifiant n'est retiré qu'une fois les valeurs rechiffrées.
5. **Index aveugle (Personnes).** Sa clé HMAC est traitée de la même manière : `BlindIndex:CleEnveloppee`, enveloppée par
   la clé racine de Personnes et déballée au démarrage. La migration enveloppe la clé existante **telle quelle** : l'index
   ne change pas et la recherche par NISS continue de fonctionner. La clé de l'index n'est pas tournée (un changement
   impose de recalculer l'index de toutes les personnes ; à décider s'il devient nécessaire).
6. **Socle commun.** Le port `ICleRacine`, les adaptateurs `CleRacineKeyVault` (validation du kid contre la clé configurée,
   anti-SSRF ; classement des refus du coffre 400 = altération, 404 et 401/403 = erreur d'exploitation, #377/#380) et
   `CleRacineLocale`, et `OperationsCleRacineAzure` passent dans `Sepp.BuildingBlocks.Infrastructure`. Documents les
   utilise derrière son port `IEnveloppeCles`, sans changement de format ni de comportement.
7. **Développement et tests.** `Enveloppe:Type = Local` : clé racine en configuration (AES-GCM), refusée hors
   `Development`, `Test` et `Testing`. Les `appsettings.Development.json` portent des valeurs de développement sans
   valeur de sécurité ; le compose ne change pas.

## Alternatives
- **Enveloppe par valeur** (une DEK par champ, comme Documents) : un appel au coffre par lecture ; exclu.
- **DEK générée par le service et stockée dans sa base** : rotation entièrement automatique, mais une migration par
  service, une table partagée entre réplicas (création concurrente, rafraîchissement des autres réplicas) et un appel au
  coffre depuis les convertisseurs synchrones quand une DEK inconnue apparaît. Écartée pour l'instant ; à reconsidérer si
  la rotation manuelle des DEK pèse.
- **Clé racine par zone** : moins de clés, mais droit de déballage partagé entre services d'une zone. Écartée.
- **Chiffrement par le coffre (`encrypt`/`decrypt`) de chaque valeur** : même coût qu'une enveloppe par valeur. Écartée.

## Conséquences
- **Terraform** : `field_kek_names` (module `keyvault`), `field_kek_assignments` (module `identity`), injection de
  `<préfixe>__Enveloppe__Type`, `__Enveloppe__CleKek`, `__CurrentKeyId` et des secrets `--deks--` ; retrait de
  `crypto_services` ; `encryption_legacy_key_ids` (anciennes clés v1, lecture seule) ; secret Personnes
  `blindindex--cleenveloppee` à la place de `blindindex--key`.
- **Exploitation** : avant l'`apply`, créer les secrets `--deks--d1` et `blindindex--cleenveloppee` (procédure dans
  `infra/README.md`), sauvegarder les clés racines comme `documents-kek` ; après rechiffrement des valeurs v1, vider
  `encryption_legacy_key_ids` puis supprimer les secrets `--keys--` et `blindindex--key` (seule copie exportable restante).
- **Rechiffrement des valeurs v1** : non automatisé ici (aucun travail de rechiffrement n'existe) ; une valeur v1 n'est
  rechiffrée en v2 que lorsque le champ est modifié. Un travail de rechiffrement par service reste à prévoir avant de retirer les
  anciennes clés.
- **Activation du Managed HSM** et domaine de sécurité : manuels (#282), comme pour `documents-kek`.
