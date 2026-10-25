namespace Nitido.Compartido.Resultados;

/// <summary>
/// Resultado de una operación con valor de retorno: trae un <typeparamref name="T"/> o un <see cref="Resultados.Error"/>.
/// </summary>
/// <typeparam name="T">Tipo del valor cuando la operación sale bien.</typeparam>
public sealed class Resultado<T>
{
    private readonly T? _valor;
    private readonly Error? _error;

    private Resultado(T? valor, Error? error)
    {
        _valor = valor;
        _error = error;
    }

    public bool EsExito => _error is null;

    public bool EsError => _error is not null;

    /// <summary>El valor. Lanza <see cref="InvalidOperationException"/> si el resultado es un error.</summary>
    public T Valor => _error is null
        ? _valor!
        : throw new InvalidOperationException("El resultado es un error y no tiene valor.");

    /// <summary>El error. Lanza <see cref="InvalidOperationException"/> si el resultado fue exitoso.</summary>
    public Error Error => _error
        ?? throw new InvalidOperationException("El resultado fue exitoso y no tiene error.");

    /// <summary>
    /// Crea un resultado exitoso. Es la única forma cuando <typeparamref name="T"/> es una interfaz,
    /// porque C# no aplica conversiones implícitas definidas por el usuario desde un tipo interfaz.
    /// </summary>
    public static Resultado<T> Exito(T valor) => new(valor, null);

    public static implicit operator Resultado<T>(T valor) => Exito(valor);

    public static implicit operator Resultado<T>(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Resultado<T>(default, error);
    }
}
