## Objet

<!-- Que change cette PR et pourquoi ? -->

## Exigences couvertes

<!-- Identifiants du cahier des charges, par ex. AFF-01, POR-02, ARC-34, CTR-20. Obligatoire. -->
- 

## Service(s) et zone de sensibilité

<!-- Services touchés et zone Container Apps concernée (ADR 0003). Cocher la ou les zones. -->
- Service(s) : 
- [ ] plateforme
- [ ] standard
- [ ] médicale
- [ ] psychosociale

## Tests

- [ ] Tests unitaires ajoutés ou mis à jour
- [ ] Tests d'architecture au vert (aucune dépendance entre services, règles de couches)
- [ ] Tests d'intégration (Testcontainers) si persistance, API ou messagerie modifiées
- [ ] Tests front (Vitest) et lint (accessibilité des templates) si un front est modifié
- [ ] Vérification manuelle décrite ci-dessous, le cas échéant

<!-- Décrire la vérification manuelle éventuelle. -->

## Contrôles de sécurité et de conformité

- [ ] **ARC-06** : aucune donnée médicale ni psychosociale (ni donnée personnelle superflue) dans les journaux, traces, métriques, messages d'erreur ou événements d'intégration
- [ ] Contrats d'événements : changement rétrocompatible ou nouvelle version (ARC-34)
- [ ] Aucun secret, chaîne de connexion ou configuration d'environnement dans le code ou l'image
- [ ] Migrations de base de données réversibles ou plan de retour arrière documenté
- [ ] Infrastructure : changements décrits en Terraform, aucune modification manuelle (ARC-51)
- [ ] Documentation / ADR mise à jour si une décision d'architecture change

## Notes pour la revue

<!-- Points d'attention, captures d'écran, liens. -->
