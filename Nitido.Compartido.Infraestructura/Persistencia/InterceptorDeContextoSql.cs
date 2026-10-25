using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Nitido.Compartido.Multiempresa;

namespace Nitido.Compartido.Infraestructura.Persistencia;

/// <summary>
/// Fija el <c>SESSION_CONTEXT</c> de SQL Server en cada conexión que EF abre: <c>UUID_USUARIOS</c>,
/// <c>UUID_FIRMAS</c>, <c>UUID_EMPRESAS</c> y <c>CORREO_INGRESO</c>, todas con <c>@read_only = 1</c> (segunda
/// barrera, ADR 0003). Las políticas de seguridad por fila leen esas llaves. <c>CORREO_INGRESO</c> va vacía salvo
/// en la búsqueda del usuario al ingresar (ver <see cref="ContextoDeAlcance"/>).
/// </summary>
/// <remarks>
/// Se engancha en <c>ConnectionOpened</c> (no en <c>Opening</c>: ahí la conexión todavía no puede ejecutar nada) y
/// en su versión asíncrona. Las tres llaves se fijan siempre, en un solo comando: sin sesión valen
/// <see cref="Guid.Empty"/>, para que nadie pueda ponerles un valor después en esa misma conexión. Sin contexto de
/// EF o sin <see cref="IContextoSesion"/> registrado falla cerrado (todo <see cref="Guid.Empty"/>). Al reutilizar
/// una conexión del pool, SQL Server restablece la sesión (y con ella las llaves de solo lectura) con la primera
/// orden que viaja, que es la de este interceptor. No registra los valores en el log.
/// </remarks>
internal sealed class InterceptorDeContextoSql : DbConnectionInterceptor
{
    private const string Orden = """
        EXEC sys.sp_set_session_context @key = N'UUID_USUARIOS', @value = @uuidUsuarios, @read_only = 1;
        EXEC sys.sp_set_session_context @key = N'UUID_FIRMAS',   @value = @uuidFirmas,   @read_only = 1;
        EXEC sys.sp_set_session_context @key = N'UUID_EMPRESAS', @value = @uuidEmpresas, @read_only = 1;
        EXEC sys.sp_set_session_context @key = N'CORREO_INGRESO', @value = @correoIngreso, @read_only = 1;
        """;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var comando = Preparar(connection, eventData.Context);
        comando.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var comando = Preparar(connection, eventData.Context);
        await comando.ExecuteNonQueryAsync(cancellationToken);
    }

    private static DbCommand Preparar(DbConnection conexion, DbContext? contexto)
    {
        var sesion = ResolverSesion(contexto);
        var comando = conexion.CreateCommand();
        comando.CommandText = Orden;
        AgregarParametro(comando, "@uuidUsuarios", sesion?.UuidUsuarios ?? Guid.Empty);
        AgregarParametro(comando, "@uuidFirmas", sesion?.UuidFirmas ?? Guid.Empty);
        AgregarParametro(comando, "@uuidEmpresas", sesion?.UuidEmpresas ?? Guid.Empty);

        var correo = comando.CreateParameter();
        correo.ParameterName = "@correoIngreso";
        correo.DbType = DbType.String;
        correo.Size = 256;
        correo.Value = ResolverCorreoDeIngreso(contexto);
        comando.Parameters.Add(correo);
        return comando;
    }

    private static string ResolverCorreoDeIngreso(DbContext? contexto)
    {
        if (contexto is null)
        {
            return string.Empty;
        }

        try
        {
            var alcance = contexto.GetService<ContextoDeAlcance>();
            return alcance.Fijado ? alcance.CorreoDeIngreso : string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static IContextoSesion? ResolverSesion(DbContext? contexto)
    {
        if (contexto is null)
        {
            return null;
        }

        try
        {
            return contexto.GetService<IContextoSesion>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static void AgregarParametro(DbCommand comando, string nombre, Guid valor)
    {
        var parametro = comando.CreateParameter();
        parametro.ParameterName = nombre;
        parametro.DbType = DbType.Guid;
        parametro.Value = valor;
        comando.Parameters.Add(parametro);
    }
}
