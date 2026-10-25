using Microsoft.EntityFrameworkCore;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Lo que ve SQL Server en la conexión que EF acaba de abrir: las tres llaves de <c>SESSION_CONTEXT</c>
/// (<c>null</c> si la llave no está fijada), el tipo con que quedó guardada la de la firma, y la identidad de la
/// conexión física (<c>@@SPID</c> y <c>connect_time</c>), para saber si el pool entregó la misma.
/// </summary>
internal sealed class FilaDeSesion
{
    public Guid? UuidUsuarios { get; set; }

    public Guid? UuidFirmas { get; set; }

    public Guid? UuidEmpresas { get; set; }

    public string? TipoDeUuidFirmas { get; set; }

    public short Spid { get; set; }

    public DateTime ConnectTime { get; set; }
}

internal static class LecturaDeSesionSql
{
    public const string Consulta = """
        SELECT CAST(SESSION_CONTEXT(N'UUID_USUARIOS') AS UNIQUEIDENTIFIER) AS UuidUsuarios,
               CAST(SESSION_CONTEXT(N'UUID_FIRMAS')   AS UNIQUEIDENTIFIER) AS UuidFirmas,
               CAST(SESSION_CONTEXT(N'UUID_EMPRESAS') AS UNIQUEIDENTIFIER) AS UuidEmpresas,
               CAST(SQL_VARIANT_PROPERTY(SESSION_CONTEXT(N'UUID_FIRMAS'), 'BaseType') AS NVARCHAR(30)) AS TipoDeUuidFirmas,
               @@SPID AS Spid,
               (SELECT c.connect_time FROM sys.dm_exec_connections AS c WHERE c.session_id = @@SPID) AS ConnectTime
        """;

    /// <summary>Lee la sesión en una conexión abierta por EF (camino asíncrono: <c>ConnectionOpenedAsync</c>).</summary>
    public static async Task<FilaDeSesion> LeerAsync(DbContext contexto, CancellationToken token) =>
        (await contexto.Database.SqlQueryRaw<FilaDeSesion>(Consulta).ToListAsync(token)).Single();

    /// <summary>Lee la sesión en una conexión abierta por EF (camino síncrono: <c>ConnectionOpened</c>).</summary>
    public static FilaDeSesion Leer(DbContext contexto) =>
        contexto.Database.SqlQueryRaw<FilaDeSesion>(Consulta).ToList().Single();
}
