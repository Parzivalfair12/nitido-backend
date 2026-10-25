using FluentValidation.Results;
using Nitido.Compartido.Resultados;

namespace Nitido.Clientes.Aplicacion.Clientes;

internal static class ValidacionDeCampos
{
    /// <summary>Convierte el resultado de FluentValidation en un error de validación por campo (nombres en camelCase).</summary>
    public static Error ComoError(this ValidationResult resultado)
    {
        var campos = resultado.Errors
            .GroupBy(e => string.Join('.', e.PropertyName.Split('.').Select(parte => char.ToLowerInvariant(parte[0]) + parte[1..])))
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray());
        return Error.Validacion(campos);
    }
}
