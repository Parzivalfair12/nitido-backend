using Nitido.Compartido.Resultados;

namespace Nitido.Pruebas.Unidad.Resultados;

/// <summary>
/// Pruebas de Resultado, Resultado&lt;T&gt; y Error (docs/arquitectura-backend.md, sección 3.2):
/// un error esperado se devuelve, no se lanza.
/// </summary>
public class ResultadoPruebas
{
    [Fact]
    public void Resultado_ExitoExponeElValor()
    {
        Resultado<int> resultado = 42;

        Assert.True(resultado.EsExito);
        Assert.False(resultado.EsError);
        Assert.Equal(42, resultado.Valor);
    }

    [Fact]
    public void Resultado_ErrorExponeElError()
    {
        var error = Error.Conflicto("nit_repetido", "Ya existe un cliente con ese NIT.");

        Resultado<int> resultado = error;

        Assert.True(resultado.EsError);
        Assert.False(resultado.EsExito);
        Assert.Equal(error, resultado.Error);
        Assert.Equal(TipoError.Conflicto, resultado.Error.Tipo);
        Assert.Equal("nit_repetido", resultado.Error.Codigo);
        Assert.Equal("Ya existe un cliente con ese NIT.", resultado.Error.Mensaje);
    }

    [Fact]
    public void Resultado_LeerValorDeUnErrorLanzaExcepcion()
    {
        Resultado<string> resultado = Error.NoEncontrado("cliente_no_existe", "No encontramos ese cliente.");

        Assert.Throws<InvalidOperationException>(() => resultado.Valor);
    }

    [Fact]
    public void Resultado_LeerErrorDeUnExitoLanzaExcepcion()
    {
        Resultado<string> resultado = "Contadores Asociados SAS";

        Assert.Throws<InvalidOperationException>(() => resultado.Error);
    }

    [Fact]
    public void Resultado_SeConvierteImplicitamenteDesdeValorYDesdeError()
    {
        // Un método con Resultado<T> como tipo de retorno puede devolver el valor o el error tal cual.
        Assert.Equal("900123456", Buscar(existe: true).Valor);

        var fallo = Buscar(existe: false);
        Assert.True(fallo.EsError);
        Assert.Equal(TipoError.NoEncontrado, fallo.Error.Tipo);
        Assert.Equal("cliente_no_existe", fallo.Error.Codigo);

        static Resultado<string> Buscar(bool existe)
        {
            if (!existe)
            {
                return Error.NoEncontrado("cliente_no_existe", "No encontramos ese cliente.");
            }

            return "900123456";
        }
    }

    [Fact]
    public void Error_ValidacionConservaLosCampos()
    {
        var campos = new Dictionary<string, string[]>
        {
            ["numeroDocumento"] = ["El NIT es obligatorio.", "El dígito de verificación no coincide."],
            ["razonSocial"] = ["La razón social es obligatoria."],
        };

        var error = Error.Validacion(campos);

        Assert.Equal(TipoError.Validacion, error.Tipo);
        Assert.Equal("validacion", error.Codigo);
        Assert.Equal("Revisa los datos marcados.", error.Mensaje);
        Assert.NotNull(error.Campos);
        Assert.Equal(2, error.Campos.Count);
        Assert.Equal(
            ["El NIT es obligatorio.", "El dígito de verificación no coincide."],
            error.Campos["numeroDocumento"]);
        Assert.Equal(["La razón social es obligatoria."], error.Campos["razonSocial"]);
    }

    [Fact]
    public void Resultado_ExitoDesdeValorDeTipoInterfaz()
    {
        // La conversión implícita desde T no sirve cuando T es una interfaz; la fábrica sí.
        IReadOnlyList<int> lista = [1, 2, 3];

        var resultado = Resultado<IReadOnlyList<int>>.Exito(lista);

        Assert.True(resultado.EsExito);
        Assert.False(resultado.EsError);
        Assert.Same(lista, resultado.Valor);
        Assert.Throws<InvalidOperationException>(() => resultado.Error);
    }

    [Fact]
    public void Error_ValidacionCopiaLosCampos()
    {
        var mensajes = new[] { "El NIT es obligatorio." };
        var campos = new Dictionary<string, string[]> { ["numeroDocumento"] = mensajes };

        var error = Error.Validacion(campos);

        // Quien llama sigue modificando sus propios objetos: el error no debe enterarse.
        campos["razonSocial"] = ["La razón social es obligatoria."];
        mensajes[0] = "Mensaje cambiado después.";

        Assert.NotNull(error.Campos);
        Assert.Equal(["numeroDocumento"], error.Campos.Keys);
        Assert.False(error.Campos.ContainsKey("razonSocial"));
        Assert.Equal(["El NIT es obligatorio."], error.Campos["numeroDocumento"]);
    }

    [Fact]
    public void Error_NoAutorizadoConservaTipoCodigoYMensaje()
    {
        var error = Error.NoAutorizado("sesion_vencida", "Tu sesión venció. Ingresa de nuevo.");

        Assert.Equal(TipoError.NoAutorizado, error.Tipo);
        Assert.Equal("sesion_vencida", error.Codigo);
        Assert.Equal("Tu sesión venció. Ingresa de nuevo.", error.Mensaje);
        Assert.Null(error.Campos);
    }

    [Fact]
    public void Error_ConflictoYNoEncontradoConservanTipoCodigoYMensajeSinCampos()
    {
        var conflicto = Error.Conflicto("correo_repetido", "Ese correo ya está registrado.");
        var noEncontrado = Error.NoEncontrado("firma_no_existe", "No encontramos la firma.");

        Assert.Equal(TipoError.Conflicto, conflicto.Tipo);
        Assert.Equal("correo_repetido", conflicto.Codigo);
        Assert.Equal("Ese correo ya está registrado.", conflicto.Mensaje);
        Assert.Null(conflicto.Campos);

        Assert.Equal(TipoError.NoEncontrado, noEncontrado.Tipo);
        Assert.Equal("firma_no_existe", noEncontrado.Codigo);
        Assert.Equal("No encontramos la firma.", noEncontrado.Mensaje);
        Assert.Null(noEncontrado.Campos);
    }

    [Fact]
    public void TipoError_TieneLosCuatroTiposDelDiseno()
    {
        Assert.Equal(
            ["Validacion", "NoEncontrado", "Conflicto", "NoAutorizado"],
            Enum.GetNames<TipoError>());
    }

    [Fact]
    public void ResultadoSinValor_ExitoNoTieneErrorYErrorNoEsExito()
    {
        var exito = Resultado.Exito();
        Resultado fallo = Error.Conflicto("periodo_cerrado", "El periodo está cerrado.");

        Assert.True(exito.EsExito);
        Assert.False(exito.EsError);
        Assert.Throws<InvalidOperationException>(() => exito.Error);

        Assert.True(fallo.EsError);
        Assert.False(fallo.EsExito);
        Assert.Equal("periodo_cerrado", fallo.Error.Codigo);
    }
}
