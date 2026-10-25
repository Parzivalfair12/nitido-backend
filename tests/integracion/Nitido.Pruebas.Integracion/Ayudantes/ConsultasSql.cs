using System.Data;
using Microsoft.Data.SqlClient;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Lecturas y escrituras directas con SqlClient, sin pasar por EF, para comprobar lo que de verdad quedó en la
/// base. Desde F0-02b las tablas de firmas, usuarios, clientes y accesos tienen seguridad por fila: una conexión
/// sin <c>SESSION_CONTEXT</c> no ve ninguna fila. Por eso <b>toda verificación con SQL directo usa una variante
/// con <see cref="SesionSql"/></b>; las variantes sin sesión quedan solo para DDL, para contar tablas del
/// catálogo (<c>sys.*</c>) y para las pruebas que justamente quieren demostrar que sin sesión no se ve nada.
/// Una aserción como «no se guardó» hecha sin sesión pasaría siempre, aunque la fila sí se hubiera guardado.
/// </summary>
internal static class ConsultasSql
{
    // ---------------------------------------------------------------------------------------
    // Sin sesión: DDL, catálogo y pruebas de «sin contexto no se ve nada».
    // ---------------------------------------------------------------------------------------

    public static async Task<List<object?[]>> FilasAsync(
        string cadena,
        string sql,
        CancellationToken token,
        params (string Nombre, object Valor)[] parametros)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync(token);
        return await LeerFilasAsync(conexion, sql, token, parametros);
    }

    public static async Task<object?> EscalarAsync(
        string cadena,
        string sql,
        CancellationToken token,
        params (string Nombre, object Valor)[] parametros)
    {
        var filas = await FilasAsync(cadena, sql, token, parametros);
        return filas.Count == 0 ? null : filas[0][0];
    }

    public static async Task EjecutarAsync(string cadena, string sql, CancellationToken token)
    {
        await using var conexion = new SqlConnection(cadena);
        await conexion.OpenAsync(token);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        await comando.ExecuteNonQueryAsync(token);
    }

    // ---------------------------------------------------------------------------------------
    // Con sesión: fijan las tres llaves de SESSION_CONTEXT en la misma conexión, antes de la consulta.
    // ---------------------------------------------------------------------------------------

    public static async Task<List<object?[]>> FilasAsync(
        string cadena,
        SesionSql sesion,
        string sql,
        CancellationToken token,
        params (string Nombre, object Valor)[] parametros)
    {
        await using var conexion = await AbrirConSesionAsync(cadena, sesion, token);
        return await LeerFilasAsync(conexion, sql, token, parametros);
    }

    public static async Task<object?> EscalarAsync(
        string cadena,
        SesionSql sesion,
        string sql,
        CancellationToken token,
        params (string Nombre, object Valor)[] parametros)
    {
        var filas = await FilasAsync(cadena, sesion, sql, token, parametros);
        return filas.Count == 0 ? null : filas[0][0];
    }

    /// <summary>Cuenta filas de una tabla o historial vistas por la sesión dada.</summary>
    public static async Task<int> ContarAsync(string cadena, SesionSql sesion, string tabla, CancellationToken token) =>
        Convert.ToInt32(await EscalarAsync(cadena, sesion, $"SELECT COUNT(*) FROM {tabla}", token));

    /// <summary>Las llaves (una columna Guid) que ve la sesión en una consulta, ordenadas para comparar.</summary>
    public static async Task<List<Guid>> LlavesAsync(
        string cadena,
        SesionSql sesion,
        string sql,
        CancellationToken token,
        params (string Nombre, object Valor)[] parametros) =>
        [.. (await FilasAsync(cadena, sesion, sql, token, parametros)).Select(f => (Guid)f[0]!).Distinct().Order()];

    /// <summary>
    /// Ejecuta una orden de escritura con la sesión dada y devuelve las filas afectadas. Si SQL Server rechaza
    /// la orden lanza <see cref="SqlException"/>.
    /// </summary>
    public static async Task<int> EjecutarAsync(
        string cadena,
        SesionSql sesion,
        string sql,
        CancellationToken token,
        params (string Nombre, object Valor)[] parametros)
    {
        await using var conexion = await AbrirConSesionAsync(cadena, sesion, token);
        await using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        AgregarParametros(comando, parametros);
        return await comando.ExecuteNonQueryAsync(token);
    }

    /// <summary>
    /// Abre una conexión nueva y le fija las tres llaves de <c>SESSION_CONTEXT</c> (de solo lectura, como el
    /// interceptor de la aplicación). La primera orden de una conexión reutilizada del pool lleva el
    /// restablecimiento de la sesión anterior, así que las llaves de una lectura anterior no estorban.
    /// </summary>
    public static async Task<SqlConnection> AbrirConSesionAsync(string cadena, SesionSql sesion, CancellationToken token)
    {
        var conexion = new SqlConnection(cadena);
        try
        {
            await conexion.OpenAsync(token);
            await using var comando = conexion.CreateCommand();
            comando.CommandText = """
                EXEC sys.sp_set_session_context @key = N'UUID_USUARIOS', @value = @uuidUsuarios, @read_only = 1;
                EXEC sys.sp_set_session_context @key = N'UUID_FIRMAS',   @value = @uuidFirmas,   @read_only = 1;
                EXEC sys.sp_set_session_context @key = N'UUID_EMPRESAS', @value = @uuidEmpresas, @read_only = 1;
                """;
            comando.Parameters.Add(new SqlParameter("@uuidUsuarios", SqlDbType.UniqueIdentifier) { Value = sesion.UuidUsuarios });
            comando.Parameters.Add(new SqlParameter("@uuidFirmas", SqlDbType.UniqueIdentifier) { Value = sesion.UuidFirmas });
            comando.Parameters.Add(new SqlParameter("@uuidEmpresas", SqlDbType.UniqueIdentifier) { Value = sesion.UuidEmpresas ?? Guid.Empty });
            await comando.ExecuteNonQueryAsync(token);
            return conexion;
        }
        catch
        {
            await conexion.DisposeAsync();
            throw;
        }
    }

    // ---------------------------------------------------------------------------------------

    private static async Task<List<object?[]>> LeerFilasAsync(
        SqlConnection conexion,
        string sql,
        CancellationToken token,
        (string Nombre, object Valor)[] parametros)
    {
        await using var comando = conexion.CreateCommand();
        comando.CommandText = sql;
        AgregarParametros(comando, parametros);

        var filas = new List<object?[]>();
        await using var lector = await comando.ExecuteReaderAsync(token);
        while (await lector.ReadAsync(token))
        {
            var fila = new object?[lector.FieldCount];
            for (var i = 0; i < fila.Length; i++)
            {
                fila[i] = await lector.IsDBNullAsync(i, token) ? null : lector.GetValue(i);
            }

            filas.Add(fila);
        }

        return filas;
    }

    private static void AgregarParametros(SqlCommand comando, (string Nombre, object Valor)[] parametros)
    {
        foreach (var (nombre, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nombre, valor);
        }
    }
}
