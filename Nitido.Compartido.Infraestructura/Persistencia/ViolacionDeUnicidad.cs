using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Nitido.Compartido.Infraestructura.Persistencia;

/// <summary>Reconoce los errores de SQL Server por llave o índice único repetido.</summary>
public static class ViolacionDeUnicidad
{
    private const int IndiceUnicoDuplicado = 2601;
    private const int RestriccionUnicaDuplicada = 2627;

    /// <summary>
    /// Indica si la excepción se debe a que se repitió un valor de un índice o restricción único. El índice único
    /// se ve aunque la política de seguridad por fila oculte las filas de otras firmas.
    /// </summary>
    public static bool Es(DbUpdateException excepcion) =>
        excepcion.InnerException is SqlException { Number: IndiceUnicoDuplicado or RestriccionUnicaDuplicada };
}
