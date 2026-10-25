namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Escrituras con SQL directo para las pruebas de seguridad por fila y de restricciones. Cada método devuelve la
/// orden y sus parámetros; la prueba las ejecuta con la <see cref="SesionSql"/> que quiera probar.
/// </summary>
internal static class InsertsSql
{
    public static (string Sql, (string Nombre, object Valor)[] Parametros) Empresa(Guid uuidFirmas, Guid creador, Guid? uuidEmpresas = null) =>
        (
            """
            INSERT INTO T_EMPRESAS (UUID_EMPRESAS, UUID_FIRMAS, TIPO_DOCUMENTO, NUMERO_DOCUMENTO, DIGITO_VERIFICACION, RAZON_SOCIAL, ACTIVO, FECHA_CREACION, UUID_USUARIOS_CREACION)
            VALUES (@id, @firma, '31', @nit, '7', N'Cliente insertado con SQL', 1, SYSUTCDATETIME(), @creador)
            """,
            [("@id", uuidEmpresas ?? Guid.CreateVersion7()), ("@firma", uuidFirmas), ("@nit", DatosDePrueba.NitUnico()), ("@creador", creador)]);

    public static (string Sql, (string Nombre, object Valor)[] Parametros) Usuario(Guid uuidFirmas, Guid creador)
    {
        var correo = DatosDePrueba.CorreoUnico();
        return (
            """
            INSERT INTO T_USUARIOS (UUID_USUARIOS, UUID_FIRMAS, NOMBRE, CORREO, CORREO_NORMALIZADO, ROL, ACTIVO, FECHA_CREACION, UUID_USUARIOS_CREACION)
            VALUES (@id, @firma, N'Usuario insertado con SQL', @correo, @correoNormalizado, 'ADMINISTRADOR', 1, SYSUTCDATETIME(), @creador)
            """,
            [
                ("@id", Guid.CreateVersion7()),
                ("@firma", uuidFirmas),
                ("@correo", correo),
                ("@correoNormalizado", correo.ToUpperInvariant()),
                ("@creador", creador),
            ]);
    }

    public static (string Sql, (string Nombre, object Valor)[] Parametros) Acceso(Guid uuidFirmas, Guid uuidEmpresas, Guid uuidUsuarios, Guid creador) =>
        (
            """
            INSERT INTO T_USUARIOS_EMPRESAS (UUID_USUARIOS_EMPRESAS, UUID_USUARIOS, UUID_EMPRESAS, UUID_FIRMAS, FECHA_CREACION, UUID_USUARIOS_CREACION)
            VALUES (@id, @usuario, @empresa, @firma, SYSUTCDATETIME(), @creador)
            """,
            [
                ("@id", Guid.CreateVersion7()),
                ("@usuario", uuidUsuarios),
                ("@empresa", uuidEmpresas),
                ("@firma", uuidFirmas),
                ("@creador", creador),
            ]);

    /// <summary>Ejecuta una orden de <see cref="InsertsSql"/> con la sesión dada y devuelve las filas afectadas.</summary>
    public static Task<int> EjecutarAsync(
        string cadena,
        SesionSql sesion,
        (string Sql, (string Nombre, object Valor)[] Parametros) orden,
        CancellationToken token) =>
        ConsultasSql.EjecutarAsync(cadena, sesion, orden.Sql, token, orden.Parametros);
}
