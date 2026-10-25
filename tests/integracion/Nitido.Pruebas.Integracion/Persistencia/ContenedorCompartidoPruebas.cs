namespace Nitido.Pruebas.Integracion;

/// <summary>
/// Pruebas del ayudante <see cref="ContenedorSqlServer"/>: la base compartida la crea la primera prueba que la
/// pide, y que esa prueba se cancele no puede dejar sin base a todas las demás. Usa su propio contenedor
/// (no el de la corrida) para poder ser «la primera» sin afectar a nadie.
/// </summary>
public class ContenedorCompartidoPruebas
{
    [Fact]
    public async Task BaseCompartida_SiLaPrimeraPruebaSeCancela_LasDemasLaSiguenTeniendo()
    {
        var token = TestContext.Current.CancellationToken;
        await using var propio = new ContenedorSqlServer();

        // La primera en pedirla ya viene cancelada: deja de esperar, pero no debe cancelar la creación.
        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => propio.CadenaDeBaseSinTablasAsync(cancelada.Token));

        // Las siguientes reciben la base creada, y es la misma en cada llamada.
        var cadena = await propio.CadenaDeBaseSinTablasAsync(token);
        // Sin Assert.Equal: si fallara, imprimiría las dos cadenas con la clave de sa.
        Assert.True(
            cadena == await propio.CadenaDeBaseSinTablasAsync(token),
            "La base compartida cambió entre llamadas.");
        var tablas = await ConsultasSql.EscalarAsync(cadena, "SELECT COUNT(*) FROM sys.tables", token);
        Assert.Equal(0, Convert.ToInt32(tablas));
    }
}
