using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Sepp.BuildingBlocks.Infrastructure.Security;

/// <summary>
/// Contexte EF Core dont le modèle embarque des convertisseurs de chiffrement liés à un <see cref="FieldEncryptor"/>.
/// Le modèle étant mis en cache par processus, ce chiffreur fait partie de la clé de cache
/// (<see cref="ModelCacheKeyFactoryChiffre"/>) : deux hôtes du même processus avec des clés différentes ont chacun leur modèle.
/// </summary>
public interface IContexteChiffre
{
    /// <summary>Chiffreur capturé par les convertisseurs du modèle.</summary>
    FieldEncryptor Chiffreur { get; }
}

/// <summary>
/// Clé de cache du modèle EF Core : type de contexte, mode conception (comme la clé par défaut) et identité du chiffreur
/// pour les <see cref="IContexteChiffre"/>. En production, un seul chiffreur (singleton) par processus : un seul modèle,
/// construit une fois, comme avant ; aucun déballage ni appel supplémentaire par valeur.
/// <para>
/// La clé repose sur l'<b>identité de référence</b> du <see cref="FieldEncryptor"/> : le tuple compare ses éléments par
/// <see cref="object.Equals(object)"/>, qui reste celui de <see cref="object"/> tant que <see cref="FieldEncryptor"/> ne le
/// redéfinit pas. Ne redéfinir ni <c>Equals</c>, ni <c>GetHashCode</c>, ni <c>ToString</c> sur ce type (ni en faire un
/// <c>record</c>) : deux chiffreurs de clés différentes mais d'état égal partageraient un modèle, donc les convertisseurs du
/// premier.
/// </para>
/// </summary>
public sealed class ModelCacheKeyFactoryChiffre : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        (context.GetType(), designTime, (context as IContexteChiffre)?.Chiffreur);
}
