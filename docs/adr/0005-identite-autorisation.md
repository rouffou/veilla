# ADR 0005 — Identité OIDC et autorisation centralisée

## Décision
- **Microsoft Entra ID** pour les internes (MFA par accès conditionnel, NF-01) ; **fédération CSAM** (itsme, eID) en OIDC pour employeurs et travailleurs (POR-01, POR-10). **Keycloak** en local et en test (alternative acceptée au §14.9).
- Jetons de courte durée propagés du BFF aux services (ARC-40) ; chaque service valide émetteur et audience.
- **Autorisation** : rôles et permissions de la matrice §3.3 définis une seule fois dans le socle et évalués dans chaque service par des politiques ASP.NET Core. Les règles de périmètre (affilié, région, centre) et de relation de soin sont évaluées par le service propriétaire. Un point de décision externe (Open Policy Agent) pourra remplacer l'évaluateur sans changer les points d'application (ARC-41, « ou équivalent »).
- Tout accès à une donnée sensible est journalisé par le service Audit (NF-04) ; l'accès « bris de glace » exige un motif et alerte le CPMT dirigeant.
