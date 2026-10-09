# Service Documents

Modèles de documents, génération, archivage à valeur probante et signature (cahier des charges §10.3 DOC-01 et DOC-02, NF-21, NF-41, §15.3). Base `documents`, port local 5119.

## Ce que fait le service

- **Modèles versionnés par langue** (DOC-01) : une ligne par couple (code, langue, version) ; cycle brouillon → validé → publié (une seule version publiée par code et par langue, la précédente est retirée). Une version validée ou publiée n'est plus modifiable : on crée une nouvelle version. Les modèles de la zone médicale sont validés par le CPMT dirigeant (`documents:modele-valider-medical`), ceux de la zone psychosociale par le CPAP dirigeant, les autres par l'administrateur fonctionnel ; la publication reste à l'administrateur fonctionnel.
- **Champs de fusion typés** (texte, date, nombre, booléen, liste) déclarés par le modèle, validés avant enregistrement et avant publication.
- **Fusion sûre** : pas d'expression ni de code. Syntaxe : `# titre`, `## sous-titre`, `- puce`, `---`, `{{champ}}`, `{{#si champ}}…{{#sinon}}…{{/si}}`, `{{#chaque liste}}…{{.}}…{{/chaque}}`. La structure est analysée sur le modèle avant toute substitution ; une valeur est toujours insérée comme texte brut (contrôles neutralisés, longueur bornée). Toute balise ou champ non déclaré est refusé.
- **Langue** (NF-41) : langue demandée, sinon régime linguistique de l'affilié (région unilingue : langue de la région ; Bruxelles-Capitale : choix du travailleur pour ses documents, langue de l'affilié pour l'employeur), sinon choix du travailleur, sinon français. Le motif du choix est conservé avec le document. Les règles sont à valider par le service juridique.
- **Archivage probant** (DOC-02, NF-21) : PDF → empreinte SHA-256 → chiffrement AES-256-GCM avec la clé **de la zone** (standard, médicale, psychosociale : trois clés distinctes, refusées si identiques) → écriture unique dans le stockage objet → horodatage. Le contenu n'est jamais en base. L'objet chiffré est lié à son document et à sa zone (données associées authentifiées) : copié ailleurs, il ne se déchiffre pas. `GET /api/v1/documents/{id}/integrite` recalcule les empreintes et le déchiffrement authentifié.
- **Droits** : un document médical (resp. psychosocial) n'est lisible qu'avec `dossier-sante:lire` (resp. `dossier-psy:lire`) ; le travailleur lit ses documents publiés, l'employeur ceux de la zone standard publiés pour son affilié ; toute lecture de contenu est journalisée (zone du document) avant d'être rendue.
- **Formulaire d'évaluation de santé** : le service consomme `surveillance-medicale.decision-emise.v1` et produit **trois exemplaires** (employeur, travailleur, dossier de santé). L'exemplaire de l'employeur est en zone standard et ne contient que la décision (catégorie, mesures, validité, référence, date) ; les deux autres sont en zone médicale. Les exemplaires employeur et travailleur sont publiés (`documents.document-publie.v1`) pour envoi par Communications ; celui du dossier n'est jamais envoyé. Idempotent (inbox + clé `decision:<id>:<exemplaire>`).
- **Signature qualifiée** : port `ISignatureQualifiee` (preuve détachée sur l'empreinte ; le PDF archivé n'est jamais réécrit) ; simulateur sans valeur juridique.

## Modèles de départ à valider

Au démarrage (`Database:MigrateOnStartup`), neuf modèles de départ (3 exemplaires × FR, NL, DE) sont créés **en brouillon**. Ils s'inspirent du formulaire d'évaluation de santé (annexe I.4-2 du code du bien-être au travail) mais ne le reproduisent pas à l'identique : ils doivent être relus, complétés et validés par le CPMT dirigeant (exemplaires de la zone médicale) et l'administrateur fonctionnel, puis publiés. Tant qu'ils ne le sont pas, le traitement de `DecisionEmise` échoue et le message est rejoué après publication.

## PDF/A

Bibliothèque : **PDFsharp 6.2 (licence MIT)** ; polices Lato incorporées (SIL Open Font License 1.1, `Pdf/Polices/OFL.txt`). Le rendu produit un PDF/A-1a : métadonnées XMP `pdfaid` part 1 / conformance A, profil de sortie sRGB (OutputIntent), polices incorporées, document balisé (titres, paragraphes, listes) et langue. Les tests vérifient ces marqueurs ; **la validation complète par veraPDF n'est pas automatisée** (veraPDF demande Java, absent de l'environnement de développement) et reste à passer avant la mise en production. Limite connue : seuls les caractères de Lato au jeu WinAnsi sont garantis (français, néerlandais, allemand, anglais).

## Adaptateurs

| Port | Production | Développement et tests |
|---|---|---|
| `IStockageDocuments` | `AzureBlob` : un conteneur par zone (`documents-standard`, `documents-medicale`, `documents-psychosociale`) créé par Terraform avec une stratégie d'immutabilité WORM ; écriture avec `If-None-Match: *` ; identité managée (`Documents:BlobEndpoint`) | `Local` : fichiers en écriture unique, lecture seule (sans valeur probante) ; chaîne `ConnectionStrings:DocumentsBlob` pour Azurite |
| `IServiceHorodatage` | `Reel` : horodatage qualifié RFC 3161, **à raccorder** (prestataire à désigner) | `Simulateur` (sans valeur probante) |
| `ISignatureQualifiee` | `Reel` : eID, itsme via un prestataire qualifié eIDAS, **à raccorder** | `Simulateur` (sans valeur juridique) |
| `ISourceLinguistique` | `Reel` : `GET /api/v1/affilies/{id}` et `GET /api/v1/personnes/{id}` avec le compte technique OIDC (rôle `documents`) | `Simulateur` : régime français |

Configuration : `Documents:Adaptateurs:{Horodatage|Signature|SourceLinguistique}` (`Simulateur` ou `Reel`, sans défaut), `Documents:Stockage:Type`, `Documents:Chiffrement:<zone>:Encryption:{CurrentKeyId,Keys:<id>}` (clés Key Vault, HSM pour les zones médicale et psychosociale en production), `Documents:ServicesInternes`, `Documents:CompteTechnique`.

Hors périmètre ou à compléter : le client OIDC `veilla-documents` (rôle `documents`) et le claim `personne_id` existent dans le realm local (`deploy/local/keycloak`) ; en production, `personne_id` doit être fourni par la fédération du portail travailleur ; les API d'Affiliés et de Personnes doivent exposer `regimeLinguistique` et `langue`.

## API (`/api/v1`)

`/modeles` (liste, détail, création, modification d'un brouillon, `/versions`, `/validation`, `/renvoi-en-brouillon`, `/publication`, `/apercu`) et `/documents` (génération, liste par objet ou destinataire, détail, `/contenu` avec l'en-tête `X-Motif-Acces`, `/integrite`, `/publication`, `/signatures`).
