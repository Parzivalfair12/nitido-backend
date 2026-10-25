namespace Nitido.Compartido.Resultados;

/// <summary>
/// Resultado de una operación sin valor de retorno: sale bien o sale con un <see cref="Resultados.Error"/>.
/// </summary>
public sealed class Resultado
{
    private static readonly Resultado ExitoUnico = new(null);

    private readonly Error? _error;

    private Resultado(Error? error) => _error = error;

    public bool EsExito => _error is null;

    public bool EsError => _error is not null;

    /// <summary>El error. Lanza <see cref="InvalidOperationException"/> si el resultado fue exitoso.</summary>
    public Error Error => _error
        ?? throw new InvalidOperationException("El resultado fue exitoso y no tiene error.");

    public static Resultado Exito() => ExitoUnico;

    public static implicit operator Resultado(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Resultado(error);
    }
}
