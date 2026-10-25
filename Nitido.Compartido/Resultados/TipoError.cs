namespace Nitido.Compartido.Resultados;

/// <summary>
/// Clase de error de negocio esperado. Decide cómo se responde al cliente (400, 404, 409 o 401).
/// </summary>
public enum TipoError
{
    Validacion,
    NoEncontrado,
    Conflicto,
    NoAutorizado,
}
