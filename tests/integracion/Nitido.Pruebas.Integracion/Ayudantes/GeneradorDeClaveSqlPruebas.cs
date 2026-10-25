using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;

namespace Nitido.Pruebas.Integracion;

/// <summary>
/// La clave de <c>sa</c> del SQL Server de pruebas cambia en cada corrida y cumple la política de SQL Server.
/// Ninguna aserción usa <c>Assert.Equal/NotEqual</c> sobre la clave, para que un fallo no la imprima.
/// </summary>
public class GeneradorDeClaveSqlPruebas(ContenedorSqlServer contenedor)
{
    [Fact]
    public void Generar_CumpleLaPoliticaDeComplejidadDeSqlServer()
    {
        for (var i = 0; i < 200; i++)
        {
            var clave = GeneradorDeClaveSql.Generar();

            Assert.True(clave.Length >= 8, "La clave debe tener al menos 8 caracteres.");
            Assert.Equal(GeneradorDeClaveSql.Longitud, clave.Length);
            Assert.True(clave.Any(char.IsAsciiLetterUpper), "La clave debe tener una mayúscula.");
            Assert.True(clave.Any(char.IsAsciiLetterLower), "La clave debe tener una minúscula.");
            Assert.True(clave.Any(char.IsAsciiDigit), "La clave debe tener un número.");
            Assert.True(clave.Any(c => GeneradorDeClaveSql.Simbolos.Contains(c)), "La clave debe tener un símbolo.");
            Assert.True(clave.All(c => char.IsAsciiLetterOrDigit(c) || GeneradorDeClaveSql.Simbolos.Contains(c)), "La clave tiene caracteres no permitidos.");
        }
    }

    [Fact]
    public void Generar_NuncaEmpiezaConGuion_PorqueSqlcmdLoLeeriaComoOpcion()
    {
        // Testcontainers ejecuta «sqlcmd -P <clave>»: una clave que empiece con «-» se confundiría con una opción.
        Assert.False(GeneradorDeClaveSql.Simbolos.Contains('-'), "El conjunto de símbolos no debe incluir «-».");

        for (var i = 0; i < 1000; i++)
        {
            var clave = GeneradorDeClaveSql.Generar();

            Assert.False(clave[0] == '-', "Se generó una clave que empieza con «-».");
        }
    }

    [Fact]
    public void Generar_NoContieneCaracteresQueRompenUnaCadenaDeConexion()
    {
        char[] prohibidos = [';', '=', '\'', '"', '{', '}', ' ', '\\'];

        for (var i = 0; i < 200; i++)
        {
            var clave = GeneradorDeClaveSql.Generar();

            Assert.False(clave.IndexOfAny(prohibidos) >= 0, "La clave tiene un carácter que rompe la cadena de conexión.");

            // Y sobrevive a armar y releer una cadena de conexión.
            var cadena = new SqlConnectionStringBuilder { DataSource = "x", UserID = "sa", Password = clave }.ConnectionString;
            Assert.True(new SqlConnectionStringBuilder(cadena).Password == clave, "La clave no sobrevive a la cadena de conexión.");
        }
    }

    [Fact]
    public void Generar_NoRepiteClaveEntreLlamadas()
    {
        var claves = Enumerable.Range(0, 500).Select(_ => GeneradorDeClaveSql.Generar()).ToHashSet();

        Assert.True(claves.Count == 500, "Dos llamadas del generador produjeron la misma clave.");
        Assert.False(claves.Contains(MsSqlBuilder.DefaultPassword), "La clave no puede ser la constante por defecto de Testcontainers.");
    }

    [Fact]
    public async Task Contenedor_UsaUnaClaveGeneradaYNoLaPorDefectoDeTestcontainers()
    {
        var token = TestContext.Current.CancellationToken;
        var cadena = await contenedor.CadenaDeBaseSinTablasAsync(token);

        var clave = new SqlConnectionStringBuilder(cadena).Password;

        Assert.False(clave == MsSqlBuilder.DefaultPassword, "El contenedor usa la clave por defecto de Testcontainers.");
        Assert.Equal(GeneradorDeClaveSql.Longitud, clave.Length);
    }

    [Fact]
    public async Task Contenedores_DistintosUsanClavesDistintas()
    {
        // Sin arrancar contenedores: cada instancia genera su clave al construirse (una estática compartida
        // dejaría a todas con la misma). La comparación no imprime las claves.
        var instancias = new List<ContenedorSqlServer>();
        try
        {
            for (var i = 0; i < 50; i++)
            {
                instancias.Add(new ContenedorSqlServer());
            }

            for (var a = 0; a < instancias.Count; a++)
            {
                for (var b = a + 1; b < instancias.Count; b++)
                {
                    Assert.False(
                        instancias[a].UsaLaMismaClaveQue(instancias[b]),
                        "Dos instancias del contenedor usaron la misma clave.");
                }
            }
        }
        finally
        {
            foreach (var instancia in instancias)
            {
                await instancia.DisposeAsync();
            }
        }
    }
}
