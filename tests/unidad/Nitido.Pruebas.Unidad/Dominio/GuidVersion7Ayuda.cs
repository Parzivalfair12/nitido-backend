namespace Nitido.Pruebas.Unidad.Dominio;

/// <summary>Lectura de un GUID versión 7 (RFC 9562): versión, variante y marca de tiempo de 48 bits.</summary>
internal static class GuidVersion7Ayuda
{
    /// <summary>Los primeros 48 bits del GUID: milisegundos desde 1970-01-01 UTC.</summary>
    public static long MarcaDeTiempoEnMilisegundos(this Guid guid)
    {
        var bytes = guid.ToByteArray(bigEndian: true);
        long marca = 0;
        for (var i = 0; i < 6; i++)
        {
            marca = (marca << 8) | bytes[i];
        }

        return marca;
    }

    /// <summary>La variante RFC 4122/9562 lleva los dos bits altos del byte 8 en «10».</summary>
    public static bool TieneVarianteRfc(this Guid guid) =>
        (guid.ToByteArray(bigEndian: true)[8] & 0xC0) == 0x80;

    public static void AssertEsVersion7Con(Guid guid, DateTimeOffset ahora)
    {
        Assert.NotEqual(Guid.Empty, guid);
        Assert.Equal(7, guid.Version);
        Assert.True(guid.TieneVarianteRfc(), $"El GUID {guid} no tiene la variante RFC.");
        Assert.Equal(ahora.ToUnixTimeMilliseconds(), guid.MarcaDeTiempoEnMilisegundos());
    }
}
