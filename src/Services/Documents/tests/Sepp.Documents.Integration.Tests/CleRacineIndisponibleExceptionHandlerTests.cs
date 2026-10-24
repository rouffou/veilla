using System.Text.Json;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Sepp.Documents.Adapters.Api;
using Sepp.Documents.Application;

using Shouldly;

namespace Sepp.Documents.Integration.Tests;

/// <summary>
/// #377 : une version de clé racine introuvable ou refusée répond 503 avec un code dédié, sans divulguer le kid au client,
/// et est journalisée en erreur d'exploitation avec le kid (NF-62).
/// </summary>
public class CleRacineIndisponibleExceptionHandlerTests
{
    private const string Kid = "https://kv-sepp-med.vault.azure.net/keys/documents-kek/0123456789abcdef";
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(CauseCleRacineIndisponible.Introuvable)]
    [InlineData(CauseCleRacineIndisponible.AccesRefuse)]
    public async Task Une_cle_racine_indisponible_repond_503_sans_kid_et_journalise_le_kid_en_erreur(CauseCleRacineIndisponible cause)
    {
        var journal = new JournalMemoire();
        var gestionnaire = new CleRacineIndisponibleExceptionHandler(journal);
        var contexte = Contexte();

        (await gestionnaire.TryHandleAsync(contexte, new CleRacineIndisponibleException(Kid, cause, null), _ct)).ShouldBeTrue();

        contexte.Response.StatusCode.ShouldBe(StatusCodes.Status503ServiceUnavailable);
        contexte.Response.Body.Position = 0;
        var corps = await new StreamReader(contexte.Response.Body).ReadToEndAsync(_ct);
        JsonDocument.Parse(corps).RootElement.GetProperty("code").GetString().ShouldBe(CleRacineIndisponibleExceptionHandler.Code);
        corps.ShouldNotContain("documents-kek");
        corps.ShouldNotContain("vault.azure.net");

        var entree = journal.Entrees.ShouldHaveSingleItem();
        entree.Niveau.ShouldBe(LogLevel.Error);
        entree.Message.ShouldContain(Kid);
        entree.Message.ShouldContain(cause.ToString());
    }

    [Fact]
    public async Task Une_alteration_n_est_pas_traitee_par_ce_gestionnaire()
    {
        var journal = new JournalMemoire();

        (await new CleRacineIndisponibleExceptionHandler(journal).TryHandleAsync(Contexte(), new ContenuAltereException("altéré"), _ct)).ShouldBeFalse();

        journal.Entrees.ShouldBeEmpty();
    }

    private static DefaultHttpContext Contexte() => new()
    {
        RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
        Response = { Body = new MemoryStream() },
    };

    private sealed class JournalMemoire : ILogger<CleRacineIndisponibleExceptionHandler>
    {
        public List<(LogLevel Niveau, string Message)> Entrees { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entrees.Add((logLevel, formatter(state, exception)));
    }
}
