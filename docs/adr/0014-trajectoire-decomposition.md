# ADR 0014 — Trajectoire de décomposition : maintien des 12 services, monolithe modulaire par zone en option conditionnelle

## Statut
Proposée le 2026-11-14 par l'architecte solution, à valider par le responsable produit. Référence :
[état des lieux de l'architecture](../architecture/etat-des-lieux-architecture.md), épique #389.

## Contexte
Le système compte 12 services métier, 3 BFF et une instance de zone de Documents. Cinq autres services sont déclarés
dans Terraform mais n'ont pas de code. ARC-01 et le §14.3 du cahier des charges imposent un service par contexte
délimité, avec un cycle de déploiement indépendant. L'état des lieux constate :

- un couplage fonctionnel fort entre Obligations et Planification (11 contrats d'aller-retour) et un rôle pivot
  d'Obligations (24 gestionnaires, saga de reprise) ;
- aucun service anémique : Audit, Référentiels et Communications sont petits mais ont une responsabilité nette ;
- un coût d'exploitation qui croît avec le nombre de services et d'environnements (amorçage des rôles PostgreSQL,
  secrets, répliques minimales), pour une équipe réduite ;
- une démo commerciale (#389) qui tourne en local et dépend des écrans de l'application interne, pas du découpage.

## Décision
1. **Les 12 services et les 3 BFF sont maintenus tels quels.** Aucune fusion ni aucun découpage avant la démo #389.
2. **Aucune fusion ciblée n'est retenue.** Obligations + Planification et Affiliés + Personnes sont écartées (voir
   « Alternatives »).
3. **Le monolithe modulaire par zone reste une option d'exploitation, ni retenue ni rejetée.** Il s'agirait d'un hôte
   par zone qui compose les modules existants, sans toucher à leur domaine. On le réévalue si l'un des critères
   ci-dessous est franchi, et seulement après accord du responsable produit sur l'écart à ARC-01.
4. **Les invariants qui rendent l'option réversible sont conservés** :
   - aucune référence de projet entre services (ADR 0002) ;
   - des contrats uniquement dans `src/Contracts` ;
   - une base par service ;
   - un `Program.cs` mince qui compose les couches Adapters et Infrastructure.

### Critères de réévaluation
On rouvre l'option du monolithe modulaire par zone, ou une fusion, si l'un des seuils suivants est franchi. Les seuils
sont à confirmer par le responsable produit.

- Le coût Azure mensuel hors production dépasse le budget fixé par le responsable produit (valeur à fournir).
- Le temps d'exploitation manuel par livraison complète (amorçage, secrets, migrations) dépasse une demi-journée.
- Plus de la moitié des PR d'un trimestre touchent à la fois Obligations et Planification, ou à la fois Affiliés et
  Personnes : c'est le signe qu'une frontière est mal placée.
- La démo ou le poste de développement ne tiennent plus sur 16 Go de mémoire.

## Alternatives
- **Fusion Obligations + Planification.** Elle supprime 11 contrats, mais crée le plus gros service du dépôt et mélange
  l'échéance légale (§5.2) avec la logistique (§8). Elle touche le cœur de la démo. Écartée.
- **Fusion Affiliés + Personnes.** Elle supprime quelques lectures, mais élargit le périmètre d'accès au NISS chiffré et
  à son index aveugle (ADR 0012). Écartée.
- **Monolithe modulaire par zone, tout de suite.**
  - Gain : environ 6 Container Apps au lieu de 16, et une démo plus légère.
  - Coût : un écart majeur à ARC-01, une isolation dégradée entre services d'une même zone (identités et clés par
    service, ADR 0010 et 0012), des garde-fous à réécrire et 3 à 5 semaines de gel des fonctionnalités.
  - Conservé comme option conditionnelle, non retenu maintenant.
- **Découpages supplémentaires** (service de saga, scission de Surveillance médicale) : plus de services pour la même
  équipe. Le service de saga a déjà été écarté par l'ADR 0008. Écartés.

## Conséquences
- Aucun changement de code. La démo n'est pas affectée.
- Le coût d'exploitation est traité par la rationalisation du catalogue (ADR 0015), pas par une fusion.
- Les mesures des critères (coût, temps d'exploitation, mémoire) sont à instrumenter avant la préproduction
  (`devops-engineer`, `cloud-architect`).
