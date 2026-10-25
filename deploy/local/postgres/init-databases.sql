-- Une base par service (ARC-02). Identifiants de développement local uniquement.
CREATE DATABASE referentiels;
CREATE DATABASE audit;
CREATE DATABASE affilies;
CREATE DATABASE personnes;
CREATE DATABASE postes_risques;
CREATE DATABASE integrations;
CREATE DATABASE surveillance_medicale;
CREATE DATABASE planification;
CREATE DATABASE obligations;
CREATE DATABASE documents;
-- Instance zone médicale du service Documents (décision #356), lancée avec le profil « par-zone ».
CREATE DATABASE documents_medicale;
CREATE DATABASE communications;
CREATE DATABASE reintegration;
-- Bus d'événements de développement (ADR 0004) : jamais hors poste de développement et tests.
CREATE DATABASE bus;
