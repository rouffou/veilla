using Sepp.BuildingBlocks.Domain;
using Sepp.Documents.Domain.Documents;

namespace Sepp.Documents.Domain.Langues;

/// <summary>Régime linguistique de l'affilié (AFF-01), selon le siège d'exploitation.</summary>
public enum RegimeLinguistique
{
    Francais,
    Neerlandais,
    Allemand,
    BruxellesCapitale,
}

/// <summary>Langue retenue pour un document et sa justification (journalisée avec le document).</summary>
public sealed record ChoixLangue(Language Langue, string Motif);

/// <summary>Éléments connus pour déterminer la langue d'un document.</summary>
/// <param name="LangueDemandee">Langue imposée explicitement par l'auteur de la demande (par ex. traduction).</param>
/// <param name="Regime">Régime linguistique de l'affilié, s'il est connu.</param>
/// <param name="LangueAffilie">Langue de correspondance de l'affilié.</param>
/// <param name="ChoixTravailleur">Langue choisie par le travailleur (portail, fiche personne).</param>
public sealed record ContexteLinguistique(
    Language? LangueDemandee,
    RegimeLinguistique? Regime,
    Language? LangueAffilie,
    Language? ChoixTravailleur);

/// <summary>
/// NF-41 : langue des documents selon le régime linguistique de l'affilié ou le choix du travailleur, conformément à la
/// législation sur l'emploi des langues en matière sociale. Règles appliquées (à valider par le service juridique) :
/// <list type="number">
/// <item>une langue demandée explicitement dans la demande de génération prévaut (traduction de courtoisie, décision humaine) ;</item>
/// <item>région unilingue (régime français, néerlandais ou allemand) : la langue de la région s'impose à tous les documents,
/// quel que soit le choix du travailleur (décrets de la Communauté flamande du 19 juillet 1973, de la Communauté française
/// du 30 juin 1982, lois coordonnées du 18 juillet 1966 pour la région de langue allemande) ;</item>
/// <item>Bruxelles-Capitale : un document adressé au travailleur ou versé à son dossier est rédigé dans sa langue s'il a
/// choisi le français ou le néerlandais ; un document adressé à l'employeur suit la langue de l'affilié (français ou
/// néerlandais) ; à défaut, français ;</item>
/// <item>régime inconnu : choix du travailleur pour ses documents, langue de l'affilié pour l'employeur, à défaut français.</item>
/// </list>
/// L'anglais n'est retenu que s'il est demandé explicitement (NF-40 : l'anglais est réservé aux portails).
/// </summary>
public static class RegleLinguistique
{
    public static ChoixLangue Determiner(ContexteLinguistique contexte, TypeDestinataire destinataire)
    {
        if (contexte.LangueDemandee is { } demandee)
        {
            return new ChoixLangue(demandee, "langue demandée explicitement");
        }

        var pourTravailleur = destinataire is TypeDestinataire.Personne or TypeDestinataire.Dossier;
        switch (contexte.Regime)
        {
            case RegimeLinguistique.Francais:
                return new ChoixLangue(Language.Fr, "régime linguistique de l'affilié : région de langue française");
            case RegimeLinguistique.Neerlandais:
                return new ChoixLangue(Language.Nl, "régime linguistique de l'affilié : région de langue néerlandaise");
            case RegimeLinguistique.Allemand:
                return new ChoixLangue(Language.De, "régime linguistique de l'affilié : région de langue allemande");
            case RegimeLinguistique.BruxellesCapitale:
                if (pourTravailleur && contexte.ChoixTravailleur is Language.Fr or Language.Nl)
                {
                    return new ChoixLangue(contexte.ChoixTravailleur.Value, "Bruxelles-Capitale : langue choisie par le travailleur");
                }

                return contexte.LangueAffilie is Language.Fr or Language.Nl
                    ? new ChoixLangue(contexte.LangueAffilie.Value, "Bruxelles-Capitale : langue de l'affilié")
                    : new ChoixLangue(Language.Fr, "Bruxelles-Capitale : français par défaut");
        }

        if (pourTravailleur && contexte.ChoixTravailleur is Language.Fr or Language.Nl or Language.De)
        {
            return new ChoixLangue(contexte.ChoixTravailleur.Value, "régime inconnu : langue choisie par le travailleur");
        }

        return contexte.LangueAffilie is Language.Fr or Language.Nl or Language.De
            ? new ChoixLangue(contexte.LangueAffilie.Value, "régime inconnu : langue de l'affilié")
            : new ChoixLangue(Language.Fr, "régime inconnu : français par défaut");
    }
}
