using FluentValidation.Results;
using Nitido.Compartido.Resultados;

namespace Nitido.Firmas.Aplicacion.Validacion;

internal static class ValidacionDeCampos
{
    /// <summary>
    /// Convierte el resultado de FluentValidation en un error de validación por campo. Los nombres de campo salen en
    /// camelCase y con puntos para los anidados (<c>administrador.clave</c>), como los espera el frontend.
    /// </summary>
    public static Error ComoError(this ValidationResult resultado)
    {
        var campos = resultado.Errors
            .GroupBy(e => NombreDeCampo(e.PropertyName))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        return Error.Validacion(campos);
    }

    private static string NombreDeCampo(string propiedad) =>
        string.Join('.', propiedad.Split('.').Select(parte => char.ToLowerInvariant(parte[0]) + parte[1..]));
}
