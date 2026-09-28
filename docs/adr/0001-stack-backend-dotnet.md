# ADR 0001 — Backend en .NET 10 (LTS) / C#

## Contexte
Le cahier des charges (§14.9) propose .NET ou Java (Spring Boot) en version LTS et accepte un autre langage maintenu s'il est justifié (ARC-08). La cible est Azure, en microservices DDD / clean architecture (ARC-01, ARC-03), avec tests d'architecture (ARC-20), tests de contrat (ARC-23) et un gabarit commun (ARC-24).

## Décision
**.NET 10 (LTS, supporté jusqu'en novembre 2028) et C#** : ASP.NET Core Minimal APIs, EF Core 10 + Npgsql (PostgreSQL), OpenTelemetry.

## Justification

| Critère | .NET 10 | Java 21/25 + Spring Boot | Go | Node / TypeScript |
|---|---|---|---|---|
| Intégration Azure (Service Bus, Key Vault, Entra ID, identités managées, App Insights) | SDK de premier rang | Très bonne (Spring Cloud Azure) | Correcte | Correcte |
| Modélisation DDD riche (records, types valeur, pattern matching, nullabilité stricte) | Excellente | Bonne | Faible | Bonne, mais pas de garantie à l'exécution |
| Tests d'architecture | NetArchTest | ArchUnit (référence) | Limités | Limités |
| Conteneurs (KEDA, mise à zéro) | Images *chiseled* ~100 Mo, démarrage < 1 s, AOT possible | Plus lourd sans GraalVM | Excellente | Bonne |
| Performance (NF-31, NF-32) | Parmi les meilleures | Bonne | Excellente | Moyenne |
| Écosystème santé belge (eHealth, HL7/FHIR) | FHIR (Firely), clients SOAP/WS-Security | Le plus riche (connecteurs eHealth historiques en Java) | Pauvre | Pauvre |

**Java est la seule alternative sérieuse**, principalement pour les connecteurs eHealth. Ce risque est circonscrit par la couche anti-corruption (service Intégrations, INT-*) : si un connecteur Java s'avère indispensable, il sera isolé dans un conteneur dédié derrière une API interne, sans impact sur les autres services. Go et Node n'offrent pas la richesse de modélisation nécessaire à un domaine réglementaire aussi dense (délais légaux, règles par risque, workflows).

À capacités équivalentes, .NET l'emporte sur la cohérence avec la plateforme Azure imposée (ARC-08), l'outillage Microsoft (Entra ID, Azure Monitor) et l'empreinte des conteneurs.

## Licences des bibliothèques
MediatR, MassTransit (v9+) et FluentAssertions (v8+) sont passés sous licence commerciale en 2025. Le socle fournit donc ses propres abstractions légères (répartiteur de cas d'usage, outbox, inbox) et les tests utilisent **Shouldly**. Voir ADR 0004.

## Conséquences
- SDK épinglé par `global.json` ; versions NuGet centralisées (`Directory.Packages.props`) ; avertissements traités comme erreurs.
- Passage à la LTS suivante (.NET 12, novembre 2027) à planifier avant fin de support.
