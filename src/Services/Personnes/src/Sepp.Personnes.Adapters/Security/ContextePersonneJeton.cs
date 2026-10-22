using Microsoft.AspNetCore.Http;

using Sepp.Personnes.Application;

namespace Sepp.Personnes.Adapters.Security;

/// <summary>
/// Lit la personne du travailleur dans le claim <c>personne_id</c> du jeton (ADR 0005, POR-10) : une valeur unique et
/// valide, sinon aucune personne (un claim répété est ambigu et refusé).
/// </summary>
internal sealed class ContextePersonneJeton(IHttpContextAccessor accessor) : IContextePersonne
{
    public const string ClaimType = "personne_id";

    public Guid? PersonneId
    {
        get
        {
            var valeurs = (accessor.HttpContext?.User.FindAll(ClaimType) ?? []).Select(c => c.Value.Trim()).Distinct().ToList();
            return valeurs.Count == 1 && Guid.TryParse(valeurs[0], out var id) && id != Guid.Empty ? id : null;
        }
    }
}
