namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Quién actúa en una lectura o escritura con SQL directo: las tres llaves de <c>SESSION_CONTEXT</c> que fija
/// el interceptor de conexión (<c>UUID_USUARIOS</c>, <c>UUID_FIRMAS</c>, <c>UUID_EMPRESAS</c>). Con las
/// políticas de seguridad por fila activas, una conexión sin estas llaves no ve ninguna fila, así que toda
/// verificación con SQL directo debe pasar por una sesión (ver <see cref="ConsultasSql"/>).
/// </summary>
/// <param name="UuidUsuarios">Usuario que actúa. <see cref="Guid.Empty"/> si no hay sesión.</param>
/// <param name="UuidFirmas">Firma de la sesión. <see cref="Guid.Empty"/> si no hay sesión.</param>
/// <param name="UuidEmpresas">Cliente activo; <c>null</c> se fija como <see cref="Guid.Empty"/>.</param>
public readonly record struct SesionSql(Guid UuidUsuarios, Guid UuidFirmas, Guid? UuidEmpresas = null)
{
    /// <summary>Las tres llaves en <see cref="Guid.Empty"/>: lo que fija el interceptor cuando no hay sesión.</summary>
    public static SesionSql SinSesion => new(Guid.Empty, Guid.Empty, null);
}
