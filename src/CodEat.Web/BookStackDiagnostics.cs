using System.Net;
using System.Text.Json;

namespace CodEat.Web;

public sealed class BookStackFailure(string code, string guidance) : Exception(guidance)
{
    public string Code { get; } = code;
}

public record BookStackDiagnostic(string Code, string Guidance)
{
    public static BookStackDiagnostic From(Exception failure) => failure switch
    {
        BookStackFailure known => new(known.Code, known.Message),
        HttpRequestException { StatusCode: HttpStatusCode.Unauthorized } => Http(401),
        HttpRequestException { StatusCode: HttpStatusCode.Forbidden } => Http(403),
        HttpRequestException { StatusCode: not null } request => Http((int)request.StatusCode.Value),
        HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError } => new("BS_DNS", "Le nom du serveur BookStack ne peut pas être résolu depuis le conteneur. Vérifiez le DNS et BOOKSTACK_URL."),
        HttpRequestException { HttpRequestError: HttpRequestError.SecureConnectionError } => new("BS_TLS", "La connexion HTTPS à BookStack a échoué. Vérifiez la validité du certificat et sa chaîne de confiance ; conservez la vérification TLS activée."),
        HttpRequestException => new("BS_NETWORK", "Le conteneur ne parvient pas à joindre BookStack. Vérifiez le réseau Docker, le pare-feu, le port HTTPS et le reverse proxy."),
        OperationCanceledException => new("BS_TIMEOUT", "La requête BookStack a été interrompue ou a dépassé le délai de 30 secondes. Vérifiez la disponibilité du serveur et réessayez."),
        JsonException or KeyNotFoundException or FormatException => new("BS_RESPONSE", "La réponse reçue ne correspond pas au JSON de l’API BookStack. Vérifiez l’URL de base et qu’un proxy ne renvoie pas une page de connexion ou une page HTML."),
        _ => new("BS_INTERNAL", "Une erreur locale empêche la synchronisation. Consultez les journaux de l’application avec ce code de diagnostic.")
    };

    public static BookStackDiagnostic Http(int status) => new($"BS_HTTP_{status}", status switch
    {
        401 => "BookStack refuse l’authentification (HTTP 401). Vérifiez BOOKSTACK_TOKEN_ID et BOOKSTACK_TOKEN_SECRET : ce sont les deux éléments du jeton API, pas les identifiants de connexion.",
        403 => "Accès BookStack refusé (HTTP 403). Vérifiez l’expiration du jeton, l’autorisation d’accès à l’API du rôle, les droits de lecture du livre et les restrictions du reverse proxy.",
        404 => "Ressource BookStack introuvable (HTTP 404). Vérifiez BOOKSTACK_URL et l’identifiant numérique BOOKSTACK_BOOK_ID ; un livre sans droit de lecture peut aussi être masqué.",
        >= 300 and < 400 => $"L’API BookStack renvoie une redirection (HTTP {status}). Utilisez son URL HTTPS finale dans BOOKSTACK_URL et vérifiez les règles du reverse proxy.",
        429 => "La limite de requêtes de l’API BookStack a été atteinte (HTTP 429). Attendez avant de réessayer.",
        504 => "Le proxy devant BookStack a dépassé son délai sur une requête API (HTTP 504). Vérifiez les ressources de BookStack, sa base de données et les journaux de son proxy.",
        >= 500 => $"BookStack ou son reverse proxy est indisponible (HTTP {status}). Vérifiez les journaux de ces services.",
        _ => $"L’API BookStack refuse la requête (HTTP {status}). Vérifiez sa configuration et sa disponibilité."
    });
}
