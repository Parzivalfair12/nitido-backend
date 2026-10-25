using Nitido.Firmas.Dominio.Firmas;

namespace Nitido.Pruebas.Unidad.Dominio;

public class FirmaPruebas
{
    private static readonly DateTimeOffset Ahora = new(2031, 3, 4, 5, 6, 7, 89, TimeSpan.Zero);

    [Fact]
    public void Crear_AsignaNombreYLlaveVersion7ConElMomentoRecibido()
    {
        var firma = Firma.Crear("Contadores Andinos S.A.S.", Ahora);

        Assert.Equal("Contadores Andinos S.A.S.", firma.Nombre);
        GuidVersion7Ayuda.AssertEsVersion7Con(firma.Uuid, Ahora);
    }

    [Fact]
    public void Crear_ConMomentoEnOtraZonaHoraria_UsaElInstanteUtc()
    {
        var enBogota = new DateTimeOffset(2031, 3, 4, 0, 6, 7, 89, TimeSpan.FromHours(-5));

        var firma = Firma.Crear("Firma", enBogota);

        // El mismo instante que Ahora (05:06:07.089 UTC).
        Assert.Equal(Ahora.ToUnixTimeMilliseconds(), firma.Uuid.MarcaDeTiempoEnMilisegundos());
    }

    [Fact]
    public void Crear_DosVecesEnElMismoMomento_DaLlavesDistintas()
    {
        var primera = Firma.Crear("Firma", Ahora);
        var segunda = Firma.Crear("Firma", Ahora);

        Assert.NotEqual(primera.Uuid, segunda.Uuid);
    }

    [Fact]
    public void Crear_EnMomentosCrecientes_DaLlavesQueOrdenanPorTiempo()
    {
        var antes = Firma.Crear("Firma", Ahora);
        var despues = Firma.Crear("Firma", Ahora.AddMinutes(1));

        Assert.True(antes.Uuid.MarcaDeTiempoEnMilisegundos() < despues.Uuid.MarcaDeTiempoEnMilisegundos());
    }
}
