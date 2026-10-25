namespace Nitido.Compartido.Infraestructura;

/// <summary>
/// Opciones de conexión a la base de datos. Se llenan desde <c>ConnectionStrings:Nitido</c>. La leen los
/// <c>DbContext</c> de los módulos y el host (comprobación de salud). Nunca se registra su contenido.
/// </summary>
public sealed class OpcionesBaseDeDatos
{
    /// <summary>Cadena de conexión a SQL Server.</summary>
    public string CadenaDeConexion { get; set; } = string.Empty;
}
