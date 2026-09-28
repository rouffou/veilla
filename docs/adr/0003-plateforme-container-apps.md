# ADR 0003 — Exécution sur Azure Container Apps (CTR-00)

## Contexte
CTR-00 demande de choisir entre App Service, Container Apps et AKS au regard des critères du §14.8, des coûts et des compétences d'exploitation.

## Décision
**Azure Container Apps** pour toute la solution, avec **un environnement Container Apps par zone de sensibilité** (plateforme, standard, médicale, psychosociale), chacun dans son sous-réseau (CTR-10).

## Justification
- Une vingtaine de microservices, majoritairement consommateurs d'événements : KEDA intégré (mise à l'échelle sur la longueur des files Service Bus, CTR-13) et jobs pour les traitements planifiés (purges, alimentation quotidienne de l'entrepôt REP-21).
- Plateforme entièrement managée : pas de cluster à maintenir, adaptée à une équipe sans expertise Kubernetes dédiée.
- Révisions avec répartition du trafic : déploiement progressif et retour arrière (ARC-50).
- Environnement dédié + sous-réseau par zone : réponse directe à ARC-04 et CTR-10 ; chiffrement pair à pair intégré (ARC-46).
- Facturation à l'usage, mise à zéro hors production.

## Alternatives
- **App Service** : adapté aux fronts et BFF à charge stable, moins aux consommateurs d'événements (pas de KEDA).
- **AKS** : contrôle total (maillage, Gatekeeper), mais coût d'exploitation élevé. Les images étant portables (CTR-05), une migration reste possible sans toucher au code.

## Conséquences
Terraform décrit les environnements et les applications (ADR 0007). La disponibilité de Container Apps avec redondance de zone en Belgium Central est à confirmer avant la mise en production (ARC-53).
