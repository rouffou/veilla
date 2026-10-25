namespace Sepp.Bff.Interne.Aval;

// Contrats JSON des services aval utiles aux écrans composites, recopiés de leurs documents OpenAPI (ARC-30). Le BFF ne
// référence aucun assembly de service (ARC-02, ARC-43) : seuls les champs lus sont déclarés. Les appels relayés tels quels
// (RelaisServices) ne passent pas par ces contrats.

// --- Service Personnes et occupations (port 5112) : GET /api/v1/personnes/{id} ---

/// <summary>Nom et prénom d'une personne, pour l'annuaire ; le NISS masqué renvoyé par le service n'est pas lu (DAT-06).</summary>
public sealed record PersonneAval(Guid Id, string Nom, string Prenom);

// --- Service Affiliés (port 5111) : GET /api/v1/affilies/{id} ---

public sealed record AffilieAval(Guid Id, FicheAffilieAval Fiche);

public sealed record FicheAffilieAval(string NumeroBce, string Denomination);
