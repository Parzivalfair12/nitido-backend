namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Las cuatro columnas de auditoría de una fila, leídas directo de la base (sin pasar por EF). Se lee con una
/// sesión (<see cref="SesionSql"/>) que pueda ver la fila: con la seguridad por fila activa, sin sesión no hay
/// filas y <see cref="LeerAsync"/> falla en <c>Assert.Single</c> en vez de pasar en vacío.
/// </summary>
internal sealed record AuditoriaDeFila(DateTime FechaCreacion, Guid UsuarioCreacion, DateTime? FechaModificacion, Guid? UsuarioModificacion)
{
    public static async Task<AuditoriaDeFila> LeerAsync(
        string cadena,
        SesionSql sesion,
        string tabla,
        string columnaLlave,
        Guid llave,
        CancellationToken token)
    {
        var filas = await ConsultasSql.FilasAsync(
            cadena,
            sesion,
            $"SELECT FECHA_CREACION, UUID_USUARIOS_CREACION, FECHA_MODIFICACION, UUID_USUARIOS_MODIFICACION FROM {tabla} WHERE {columnaLlave} = @llave",
            token,
            ("@llave", llave));
        var fila = Assert.Single(filas);
        return new AuditoriaDeFila((DateTime)fila[0]!, (Guid)fila[1]!, (DateTime?)fila[2], (Guid?)fila[3]);
    }
}
