using Nitido.Clientes.Dominio.Clientes;

namespace Nitido.Pruebas.Unidad.Dominio;

/// <summary>
/// Pruebas de <see cref="Nit"/>. No se usan los NIT del mockup como datos: sus dígitos de verificación son de
/// relleno y no cuadran con el cálculo (spec F0-04).
/// </summary>
public class NitPruebas
{
    [Theory]
    [InlineData("800197268", "4")] // Dirección de Impuestos y Aduanas Nacionales (DIAN)
    [InlineData("890903938", "8")] // Bancolombia
    [InlineData("860002964", "4")] // Banco de Bogotá
    [InlineData("899999068", "1")] // Ecopetrol
    public void DigitoVerificacion_CalculaComoLaDian(string numero, string esperado)
    {
        Assert.Equal(esperado, Nit.Calcular(numero));
    }

    [Theory]
    [InlineData("800197268")]
    [InlineData("800.197.268")]
    [InlineData("800197268-4")]
    [InlineData("800.197.268-4")]
    [InlineData("  800197268  ")]
    public void Crear_AceptaLosFormatosDelNitYGuardaSoloLosDigitos(string texto)
    {
        var resultado = Nit.Crear(texto);

        Assert.True(resultado.EsExito);
        Assert.Equal("800197268", resultado.Valor.Numero);
        Assert.Equal("4", resultado.Valor.DigitoVerificacion);
    }

    [Theory]
    [InlineData("800197268-5")]
    [InlineData("800.197.268-0")]
    public void Crear_RechazaUnDigitoDeVerificacionQueNoCorresponde(string texto)
    {
        var resultado = Nit.Crear(texto);

        Assert.True(resultado.EsError);
        Assert.Equal(Nit.MensajeDigitoIncorrecto, Assert.Single(resultado.Error.Campos!["numeroDocumento"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12345678")] // 8 dígitos
    [InlineData("1234567890")] // 10 dígitos
    [InlineData("700123456")] // no empieza por 8 ni por 9
    [InlineData("100123456")] // cédula
    [InlineData("80019726x")]
    [InlineData("800 197 268")]
    [InlineData("8001.97268")]
    [InlineData("800197268-45")]
    [InlineData("800197268-")]
    [InlineData("abc")]
    public void Crear_RechazaLoQueNoEsUnNitDeNueveDigitos(string? texto)
    {
        var resultado = Nit.Crear(texto);

        Assert.True(resultado.EsError);
        Assert.Equal(Nit.MensajeSoloNit, Assert.Single(resultado.Error.Campos!["numeroDocumento"]));
    }

    [Fact]
    public void Crear_NullEsUnErrorDeValidacion()
    {
        Assert.True(Nit.Crear(null).EsError);
    }

    [Fact]
    public void Calcular_RechazaLoQueNoSonDigitos()
    {
        Assert.Throws<ArgumentException>(() => Nit.Calcular("80019726x"));
        Assert.Throws<ArgumentException>(() => Nit.Calcular("1234567890123456"));
    }
}
