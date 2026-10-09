# Politique de sécurité

## Signaler une vulnérabilité

Ne publiez pas de vulnérabilité dans une issue publique. Utilisez le signalement privé de GitHub :
onglet **Security** → **Report a vulnerability** du dépôt `rouffou/veilla`.

Indiquez la version ou le commit concerné, le composant (service, BFF, front, infrastructure), les étapes de
reproduction et l'impact estimé. Un accusé de réception est envoyé sous 5 jours ouvrables.

## Périmètre

Le logiciel traite des données de santé et des données psychosociales (zones de sensibilité médicale et
psychosociale, ADR 0003). Sont en particulier dans le périmètre : tout contournement du contrôle d'accès ou du secret
médical, toute fuite de données sensibles dans les journaux, traces ou événements (ARC-06), toute faiblesse du
chiffrement applicatif (ARC-45) ou de l'authentification.

Les secrets présents dans `deploy/local` et dans les fichiers `appsettings.Development.json` sont des valeurs de
développement local, sans valeur en dehors d'un poste de développement : leur présence n'est pas une vulnérabilité.

## Mesures en place

CodeQL (C# et TypeScript) sur chaque PR et chaque semaine, Trivy et SBOM sur les images, Dependabot (dépendances et
alertes de sécurité), analyse des secrets avec protection à la poussée, branche `main` protégée (voir
`CONTRIBUTING.md`).
