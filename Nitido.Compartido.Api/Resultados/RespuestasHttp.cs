using Microsoft.AspNetCore.Http;
using Nitido.Compartido.Resultados;

namespace Nitido.Compartido.Api.Resultados;

/// <summary>
/// Traduce un <see cref="Resultado{T}"/> a una respuesta HTTP con ProblemDetails (RFC 9457): el texto para la
/// persona va en <c>title</c> y el código estable en la extensión <c>codigo</c>. Los errores de validación llevan
/// además los mensajes por campo en <c>errors</c>.
/// </summary>
public static class RespuestasHttp
{
    public static IResult ARespuestaHttp(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        var extensiones = new Dictionary<string, object?> { ["codigo"] = error.Codigo };
        return error.Tipo switch
        {
            TipoError.Validacion when error.Campos is { Count: > 0 } campos =>
                TypedResults.ValidationProblem(
                    campos.ToDictionary(c => c.Key, c => c.Value),
                    title: error.Mensaje,
                    extensions: extensiones),
            TipoError.Validacion => TypedResults.Problem(title: error.Mensaje, statusCode: StatusCodes.Status400BadRequest, extensions: extensiones),
            TipoError.NoEncontrado => TypedResults.Problem(title: error.Mensaje, statusCode: StatusCodes.Status404NotFound, extensions: extensiones),
            TipoError.Conflicto => TypedResults.Problem(title: error.Mensaje, statusCode: StatusCodes.Status409Conflict, extensions: extensiones),
            TipoError.NoAutorizado => TypedResults.Problem(title: error.Mensaje, statusCode: StatusCodes.Status401Unauthorized, extensions: extensiones),
            _ => throw new ArgumentOutOfRangeException(nameof(error), error.Tipo, "Tipo de error sin respuesta HTTP."),
        };
    }

    public static IResult ARespuestaHttp<T>(this Resultado<T> resultado, Func<T, IResult> exito)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        ArgumentNullException.ThrowIfNull(exito);

        return resultado.EsExito ? exito(resultado.Valor) : resultado.Error.ARespuestaHttp();
    }
}
