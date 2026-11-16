# ADR 0018 — Index aveugle du NISS : contrôle par recalcul, rotation sur incident, bascule unique avec coupure

## Statut
Acceptée le 2026-11-16 par le responsable produit (#514, suite de la revue de sécurité de #510). Prolonge l'ADR 0012
(chiffrement des champs par enveloppe) pour la clé HMAC de l'index aveugle du NISS (`personne.niss_hash`, DAT-06).

## Contexte
#473 (PR #510) a livré la capacité de tourner la clé de l'index aveugle : lecture à double clé (clé courante, seule
écrite, et clé précédente acceptée en lecture), recalcul de `niss_hash` par le moteur de lots du rechiffrement, et
contrôle avant le retrait de l'ancienne clé. La revue de sécurité de #510 a laissé trois questions :
- comment prouver, avant le retrait de l'ancienne clé, que toutes les empreintes sont celles de la nouvelle ;
- faut-il tourner cette clé hors compromission, et à quelle fréquence ;
- la procédure en deux bascules laisse une période où deux révisions écrivent avec deux clés différentes. Deux créations
  simultanées du même NISS y produisent deux lignes que l'index unique ne voit pas (P2-1). Pendant toute la période, la
  clé précédente reste aussi acceptée en lecture.

L'empreinte est HMAC(K, NISS) sur un espace de NISS petit : quiconque détient K et des empreintes retrouve les NISS par
dictionnaire, hors ligne. Une rotation n'a donc d'intérêt réel qu'après une compromission de K.

## Décision
1. **Contrôle par recalcul plutôt qu'une colonne de version.** La vérification (`verif-personnes`, et la fin de
   `rech-personnes`) déchiffre chaque NISS dans un seul instantané et compare `niss_hash` à l'empreinte de la clé
   courante. Le code 0 n'est rendu que si toutes les empreintes sont celles de la clé courante.
2. **Rotation sur incident uniquement.** La clé ne tourne qu'en cas de compromission avérée ou suspectée. Aucune
   rotation périodique n'est planifiée.
3. **Procédure d'urgence à bascule unique, avec coupure.** Les déploiements de Personnes sont gelés pendant tout
   l'incident (aucun `deploy.yml`, aucun `apply` hors de celui qui retire la clé précédente, aucun retour arrière), et le
   gel est annoncé dans l'incident : rien n'empêche techniquement une révision de redémarrer, et une révision qui ne
   connaît que la nouvelle clé pendant le recalcul crée des doublons. `ca-personnes` est mis à 0 réplique, la nouvelle
   clé remplace la clé courante (la clé compromise n'est plus acceptée nulle part, même en lecture), toute révision
   créée par un `apply` est désactivée avant `rech-personnes`, l'absence de révision active est revérifiée avant chaque
   job et avant le redémarrage, `rech-personnes` recalcule puis `verif-personnes` vérifie, et le service redémarre. La
   valeur de contrôle (KCV) de chaque clé, journalisée au démarrage par le service et par les jobs, permet de vérifier
   que tous utilisent la nouvelle clé. La procédure en deux bascules reste décrite comme variante hors urgence, avec
   écritures bloquées pendant la bascule : désactiver toutes les révisions, **ensuite** échanger les secrets, **puis**
   créer la nouvelle révision.
4. **Pas de verrou consultatif.** Le verrou `pg_advisory_xact_lock` sur `min(H_old, H_new)` n'aurait d'effet que pris
   dans la même transaction que la recherche et l'insertion, ce qui obligerait à ajouter une transaction explicite aux
   quatre chemins de création (API, import, DIMONA, mutations du registre national) pour une procédure exceptionnelle.
   La bascule unique supprime la course ; la variante l'évite en bloquant les écritures pendant la bascule.
5. **Filets de sécurité.** Un doublon trouvé sous les deux empreintes est journalisé (identifiants techniques seulement)
   et compté (métrique `sepp.personnes.niss.doublons`) ; le recalcul signale toute collision (code 1).
6. **Runbook RGPD.** La procédure d'incident inclut l'évaluation de la violation et la notification (RGPD art. 33 et
   34) : la base vivante est exposée jusqu'au recalcul, les sauvegardes PITR pendant toute leur rétention, les copies
   sorties sans limite.

Procédures détaillées : `infra/README.md`, « Rotation de la clé de l'index aveugle ».

## Alternatives
- **Colonne de version de clé** (`niss_hash_version`) : migration expand / contract, et le domaine doit connaître la
  version de la clé. Surtout, elle peut mentir (empreinte calculée avec une clé, version d'une autre) et ne détecte ni
  une empreinte altérée ni une troisième clé. Écartée.
- **Rotation périodique** (annuelle) : chaque rotation ouvre une période de double clé et coûte un balayage complet,
  sans réduire le risque principal (une clé déjà sortie reste exploitable sur les sauvegardes et les copies). Écartée.
- **Procédure d'urgence en deux bascules, sans coupure** : la clé compromise reste acceptée en lecture pendant tout le
  recalcul, et la bascule demande un verrou consultatif ou un blocage des écritures. Gardée comme variante hors urgence
  seulement.
- **Verrou consultatif dans `TrouverOuCreerAsync`** : voir le point 4. Écarté tant que la variante en deux bascules reste
  exceptionnelle.

## Conséquences
- En cas d'incident, Personnes est indisponible pendant le recalcul (API, imports, DIMONA, mutations, identifications
  pour Intégrations). Intégrations met ses échanges en erreur relançable ; ils sont relancés après le redémarrage.
- La vérification coûte un déchiffrement AES-GCM en mémoire par personne : à lancer en heures creuses hors incident.
- Le service journalise au démarrage la KCV de chaque clé (événements 5114 et 5115) : l'exploitation note celle de la
  clé en service pour pouvoir reconnaître la clé compromise.
- La métrique de doublon est exportée par les valeurs par défaut des services (`AddMeter("Sepp.*")`). Une règle d'alerte
  sur cette métrique reste à créer dans le module `alerting`.
- Les délais de prise en compte d'une nouvelle version de secret par les jobs Container Apps restent à constater au
  premier exercice ; la KCV journalisée est le contrôle.
