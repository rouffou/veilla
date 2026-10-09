# Service Communications

Envoi des messages aux affiliés, travailleurs et dirigeants, journal et preuves d'envoi (cahier des charges §10.3 DOC-03 à DOC-05, SAN-10, SAN-11, SAN-13). Base `communications`, port local 5120.

## Ce que fait le service

- **Messages déclenchés par des événements** (ARC-31, consommateurs idempotents) : `documents.document-publie` (notification et lien, jamais le document), `planification.convocation-emise` (seul événement qui crée une convocation : Planification décide qui est convoqué, `rendez-vous-planifie` n'en crée plus ; canal indiqué, recommandé éventuel), `planification.rappel-rendez-vous-du`, `planification.rendez-vous-annule` (annule les convocations et rappels pas encore partis, prévient le travailleur) et `audit.bris-de-glace-signale` (alerte du CPMT dirigeant ou du CPAP dirigeant).
- **Événements publiés** (ARC-32, ARC-33, outbox) : `communications.message-envoye` à chaque preuve d'envoi, `communications.message-abandonne` à l'abandon (erreur définitive, reprises épuisées, destinataire inconnu ou recommandé impossible dès la création). Ils portent `ReferenceOrigineId` (pour une convocation : le `ConvocationId`, colonne `message.reference_origine_id`), le type de message, le canal et, pour l'abandon, un code d'erreur ; jamais de contenu ni de coordonnée. Ils sont écrits dans l'outbox dans le même `SaveChanges` que la preuve d'envoi (`Message` lève `MessageEnvoyeDomaine` / `MessageAbandonneDomaine`, `ExpediteurMessages` les publie). Planification les consomme pour `ConvocationEnvoyee` / `ConvocationNonRemise`.
- **Canaux** (un port par canal, un simulateur et un adaptateur réel) : portail, e-mail, SMS, courrier, recommandé électronique, eBox Entreprise, eBox Citoyen. `Communications:Adaptateurs:<Canal>` vaut `Simulateur` ou `Reel`, sans défaut.
- **Aucune donnée sensible par e-mail ou SMS** : ces canaux ne reçoivent que la notification générique et un lien opaque (identifiant du message) vers l'espace sécurisé, quel que soit le type de message. Le gabarit est unique, la règle est imposée par le domaine (`Message.Creer` refuse tout contenu détaillé sur ces canaux) et testée. Seuls les canaux authentifiés ou nominatifs portent un contenu détaillé, limité à ce que l'événement transporte (date du rendez-vous).
- **Choix du canal** : canal demandé par l'émetteur s'il est joignable, sinon préférence du destinataire, sinon cascade (travailleur : e-mail, SMS, courrier, portail ; affilié : eBox Entreprise, e-mail, courrier, portail). Si la loi impose un recommandé (SAN-11), un second envoi s'ajoute : recommandé électronique, à défaut courrier recommandé ; s'il est impossible, un message abandonné `recommande-impossible` reste visible.
- **Envoi idempotent** : clé d'idempotence unique par fait métier ; une convocation annoncée par deux événements ne part qu'une fois. Le rejeu d'un événement ne crée rien.
- **Statuts et reprises** (DOC-05) : en attente, en échec (reprise à 1 min, 5 min, 30 min, 2 h, 12 h), envoyé, abandonné (erreur définitive ou reprises épuisées), annulé. Relance manuelle d'un message abandonné. Le traitement périodique réserve chaque message (compare-and-swap) : plusieurs instances n'envoient pas deux fois.
- **Journal** : tables `message` et `preuve_envoi` (accusé de dépôt SMTP, référence, horodatage, empreinte SHA-256 du contenu), consultables par `GET /api/v1/messages` et `GET /api/v1/messages/{id}` (permission `communications:lire`). Aucune coordonnée n'est conservée : elles sont lues auprès des services propriétaires à chaque envoi, et les erreurs consignées ne contiennent que des codes techniques.

## Adaptateurs

| Port | Réel | Simulateur |
|---|---|---|
| E-mail | SMTP via MailKit (MIT) : `Communications:Smtp` (`Hote`, `Port`, `Securite`, `Utilisateur`, `MotDePasse`, `Expediteur`) ; testé contre un serveur MailHog | boîte d'envoi en mémoire |
| Portail | le message du journal est la notification (aucun appel sortant) | idem |
| SMS, courrier, recommandé électronique, eBox Entreprise, eBox Citoyen | **non raccordés** : l'envoi échoue avec `canal-non-raccorde` (le message reste visible et relançable) ; prestataires et API à désigner | boîte d'envoi en mémoire, panne programmable |
| `IAnnuaireDestinataires` | HTTP interne : `GET /api/v1/personnes/{id}` et `GET /api/v1/affilies/{id}` avec le compte technique (rôle `communications`) | données fictives dérivées de l'identifiant |

## API manquantes ou à compléter dans les autres services

- Personnes : identifiant eBox Citoyen du travailleur, existence d'un compte portail (le portail est supposé actif), consentement SMS.
- Affiliés : adresse de correspondance et canal préféré de l'affilié (on utilise l'adresse de la première unité d'établissement et la personne de contact) ; l'identifiant eBox Entreprise est déduit du numéro BCE.
- Fournisseur d'identité : membres des rôles CPMT dirigeant et CPAP dirigeant ; en attendant, la liste est configurée (`Communications:Annuaire:Dirigeants:<zone>`).
- Realm Keycloak : client `veilla-communications` (rôle `communications`) à créer pour l'annuaire réel.
- Portails : la consultation du message par lien (`/messages/{id}`) est à réaliser côté portails ; une API de boîte de réception des externes (périmètre `personne_id`, `affilie_id`) reste à ajouter.
