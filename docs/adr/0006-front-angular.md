# ADR 0006 — Front Angular

## Décision
Un workspace Angular (`src/Frontends/veilla-web`) : application interne, portail employeur, portail travailleur (PWA), et une bibliothèque partagée. Un BFF par canal (ARC-43).

## Justification
Angular est proposé au §14.9. Cadre complet (routage, formulaires typés, tests) adapté à une application métier riche en formulaires ; typage strict aligné sur les contrats OpenAPI des BFF (génération de clients).

## Conséquences
Une image par application, configuration injectée au démarrage (CTR-05).
