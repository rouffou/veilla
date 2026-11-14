# Definition of Done (DoD) commune

## Pour un livrable documentaire
- [ ] En-tête complet (titre, auteur, statut, date)
- [ ] Hypothèses et questions ouvertes listées
- [ ] Décisions structurantes tracées en ADR
- [ ] Relu par l'agent « A » (approbateur) indiqué dans `raci.md`
- [ ] Rangé au bon endroit dans `docs/`

## Pour une modification de code
- [ ] Respecte `army/stacks/<stack>/STANDARDS.md`
- [ ] Compile / build sans nouveau warning
- [ ] Tests unitaires ajoutés ou mis à jour, tous verts
- [ ] Linter / formateur passés
- [ ] Aucun secret, credential ou donnée personnelle en dur
- [ ] Logs et gestion d'erreurs conformes aux standards
- [ ] Revue par `code-reviewer` sans remarque P0/P1 ouverte
- [ ] Documentation (README, ADR, changelog) mise à jour si nécessaire
- [ ] Critères d'acceptation de la user story vérifiés par `qa-engineer`

## Pour une modification d'infrastructure / pipeline
- [ ] Décrite en Infrastructure as Code (aucun changement manuel)
- [ ] `plan` / `what-if` / `diff` relu avant application
- [ ] Coûts estimés si nouvelles ressources
- [ ] Monitoring et alerting couverts
- [ ] Procédure de rollback documentée
