namespace Nitido.Compartido.Infraestructura.Persistencia;

/// <summary>
/// Nombres de las cuatro propiedades sombra de auditoría que <see cref="DbContextNitido"/> agrega a toda
/// entidad. Las usan el interceptor y las pruebas, para no repetir los textos.
/// </summary>
public static class ColumnasDeAuditoria
{
    /// <summary>Momento (UTC) en que se creó la fila. Columna <c>FECHA_CREACION</c>.</summary>
    public const string FechaCreacion = "FechaCreacion";

    /// <summary>Usuario que creó la fila. Columna <c>UUID_USUARIOS_CREACION</c>.</summary>
    public const string UuidUsuariosCreacion = "UuidUsuariosCreacion";

    /// <summary>Momento (UTC) de la última modificación. Columna <c>FECHA_MODIFICACION</c>.</summary>
    public const string FechaModificacion = "FechaModificacion";

    /// <summary>Usuario que hizo la última modificación. Columna <c>UUID_USUARIOS_MODIFICACION</c>.</summary>
    public const string UuidUsuariosModificacion = "UuidUsuariosModificacion";
}
