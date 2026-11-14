---
titre: Exigences non fonctionnelles — <Projet>
auteur: solution-architect
statut: brouillon
date: AAAA-MM-JJ
---

# Exigences non fonctionnelles

| ID | Catégorie | Exigence mesurable | Méthode de vérification | Priorité |
|---|---|---|---|---|
| NFR-PERF-01 | Performance | p95 < ___ ms pour ___ à ___ req/s | Test de charge (k6 / Gatling) | |
| NFR-DISP-01 | Disponibilité | SLO ___ % mensuel | Monitoring SLO | |
| NFR-RES-01 | Résilience | RPO ___ / RTO ___ | Exercice de restauration | |
| NFR-SCAL-01 | Scalabilité | Supporte ___ utilisateurs simultanés | Test de charge | |
| NFR-SEC-01 | Sécurité | Authentification OIDC, MFA pour admins | Revue + pentest | |
| NFR-SEC-02 | Sécurité | Chiffrement au repos et en transit (TLS 1.2+) | Revue config | |
| NFR-OBS-01 | Observabilité | Traces distribuées sur 100 % des requêtes entrantes | Revue | |
| NFR-ACC-01 | Accessibilité | WCAG 2.2 AA | Audit axe / Lighthouse | |
| NFR-CONF-01 | Conformité | RGPD : durée de conservation ___ | Revue DPO | |
| NFR-COST-01 | Coût | Coût cloud < ___ €/mois en prod | FinOps | |
| NFR-MAINT-01 | Maintenabilité | Couverture tests > ___ %, dette Sonar note A | CI | |
| NFR-PORT-01 | Portabilité | Conteneurisé, aucune dépendance propriétaire non justifiée | Revue | |
