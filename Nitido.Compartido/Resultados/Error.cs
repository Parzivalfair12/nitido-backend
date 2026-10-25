using System.Collections.Frozen;

namespace Nitido.Compartido.Resultados;

/// <summary>
/// Error de negocio esperado, con un código estable para el frontend y un mensaje en español.
/// </summary>
/// <param name="Tipo">Clase de error; decide el código HTTP.</param>
/// <param name="Codigo">Código estable, en minúsculas, que el frontend puede usar para reaccionar.</param>
/// <param name="Mensaje">Texto en español para mostrar al usuario.</param>
/// <param name="Campos">Mensajes por campo, solo para errores de validación.</param>
public sealed record Error(
    TipoError Tipo,
    string Codigo,
    string Mensaje,
    IReadOnlyDictionary<string, string[]>? Campos = null)
{
    public static Error Conflicto(string codigo, string mensaje) =>
        new(TipoError.Conflicto, codigo, mensaje);

    public static Error NoEncontrado(string codigo, string mensaje) =>
        new(TipoError.NoEncontrado, codigo, mensaje);

    public static Error NoAutorizado(string codigo, string mensaje) =>
        new(TipoError.NoAutorizado, codigo, mensaje);

    public static Error Validacion(IDictionary<string, string[]> campos)
    {
        ArgumentNullException.ThrowIfNull(campos);

        // Copia claves y arreglos: si quien llama modifica los suyos después, el error no cambia.
        var copia = campos.ToFrozenDictionary(campo => campo.Key, campo => campo.Value.ToArray());
        return new(TipoError.Validacion, "validacion", "Revisa los datos marcados.", copia);
    }
}
