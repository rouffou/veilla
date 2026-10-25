namespace Sepp.Audit.Domain.Parametres;

/// <summary>
/// Valeur d'un paramètre légal du service Référentiels (<c>referentiels.parametre-legal-modifie</c>, ARC-21), pour une
/// période de validité. Seule la durée de conservation du journal (<c>AUDIT.CONSERVATION</c>, NF-04) est suivie.
/// Clé : code et début de validité ; écriture par clé (idempotente).
/// </summary>
public sealed class ParametreLegalLocal
{
    private ParametreLegalLocal()
    {
    }

    public ParametreLegalLocal(string code, DateOnly valideDu, DateOnly? valideJusquAu, decimal valeur, string unite)
    {
        Code = code;
        ValideDu = valideDu;
        Appliquer(valideJusquAu, valeur, unite);
    }

    public string Code { get; private set; } = string.Empty;

    public DateOnly ValideDu { get; private set; }

    public DateOnly? ValideJusquAu { get; private set; }

    public decimal Valeur { get; private set; }

    public string Unite { get; private set; } = string.Empty;

    public void Appliquer(DateOnly? valideJusquAu, decimal valeur, string unite)
    {
        ValideJusquAu = valideJusquAu;
        Valeur = valeur;
        Unite = unite;
    }
}
