# nítido (backend)

Software contable para firmas de contadores en Colombia que llevan muchos clientes. Lee los XML de la DIAN, propone los asientos con sus retenciones, el contador los aprueba y nítido arma los libros, los estados financieros y la exportación a Siigo.

Este es el repositorio del backend (`nitido-backend`): la API en .NET, la base de datos, las pruebas de .NET y las especificaciones. La aplicación web vive en otro repositorio, `nitido-frontend` (ver [Frontend](#frontend)). Los agentes y las skills de Claude Code viven en `.claude/` de la carpeta raíz `nitido/`, común a los dos repositorios (no se versiona con ellos).

Estado actual: fase 0 (fundaciones). Ya corren la API y las pruebas, y la API se conecta a SQL Server; todavía no hay funciones de negocio.

## Requisitos

Instala esto en tu computador antes de empezar:

- **SDK de .NET 10.** La versión exacta la fija `global.json` (10.0.100 o una más nueva de la misma línea). Descárgalo en https://dotnet.microsoft.com/download. Para comprobarlo, abre una terminal en la carpeta del proyecto y ejecuta `dotnet --version`: debe mostrar `10.0.x`. Si la terminal dice que `dotnet` no se reconoce, instala el SDK o agrega su carpeta al PATH de tu sistema y abre una terminal nueva.
- **Docker Desktop**, para el SQL Server local.
- **VS Code** con la extensión **SQL Server (MSSQL)**.

No instales SQL Server en tu computador: corre dentro de un contenedor de Docker que se crea desde VS Code (ver abajo). No necesitas Node.js para trabajar en este repositorio; solo el frontend lo usa.

## Base de datos local (SQL Server)

La API necesita SQL Server para arrancar: si falta la cadena de conexión, no inicia. Pasos, una sola vez:

1. Abre Docker Desktop y espera a que diga que está en ejecución.
2. En VS Code, abre la vista **SQL Server** (el ícono de la extensión MSSQL en la barra lateral).
3. Elige **Create local SQL Container** y, cuando te lo pida, selecciona **SQL Server 2025** y el puerto **1433**. Anota la contraseña que definas.
4. La cadena de conexión no se escribe en ningún archivo del repositorio: va en los secretos de usuario de .NET. Desde la raíz del repositorio:

   ```bash
   dotnet user-secrets set "ConnectionStrings:Nitido" "<cadena>" -p Nitido.Api
   ```

   Reemplaza `<cadena>` por la de tu contenedor. Con el contenedor de VS Code (usuario `sa`, puerto 1433) queda así:

   ```
   Server=localhost,1433;Database=nitido;User Id=sa;Password=<la que definiste>;Encrypt=True;TrustServerCertificate=True
   ```

   `TrustServerCertificate=True` es solo para local, porque el contenedor usa un certificado autofirmado. Nunca lo uses en un ambiente compartido. Para ver o borrar lo guardado: `dotnet user-secrets list -p Nitido.Api` y `dotnet user-secrets remove "ConnectionStrings:Nitido" -p Nitido.Api`.
5. Crea las tablas aplicando las migraciones, en este orden (Clientes depende de las tablas de Firmas, así que Firmas va primero). La primera línea instala la herramienta `dotnet ef` en la versión que fija el repositorio. La API no aplica migraciones sola al arrancar.

   ```bash
   dotnet tool restore
   dotnet ef database update --context FirmasDbContext -p Modulos/Firmas/Infraestructura -s Nitido.Api
   dotnet ef database update --context ClientesDbContext -p Modulos/Clientes/Infraestructura -s Nitido.Api
   ```

   El comando crea la base `nitido` si no existe.

En la nube la cadena va en la variable de entorno `ConnectionStrings__Nitido` (con dos guiones bajos), o en el gestor de secretos del proveedor. Los secretos de usuario solo se leen en el entorno `Development`. En CI no hace falta ninguna cadena: cada prueba de integración levanta su propio SQL Server y le pone la suya (ver [Integración continua](#integración-continua)).

### Si la API dice «Falta la cadena de conexión»

Si al arrancar ves el mensaje `Falta la cadena de conexión ConnectionStrings:Nitido. Configúrala con dotnet user-secrets (ver README).` y la API se cierra (código de salida 1), no hay cadena configurada o está vacía. Ejecuta el comando del paso 4 con la cadena de tu contenedor y vuelve a arrancar. `dotnet ef` muestra el mismo mensaje si falta. Si ya la configuraste y sigue fallando, revisa que ejecutas los comandos desde la raíz de este repositorio y que el entorno es `Development` (los secretos de usuario no se leen en `Production`).

### Usuario para probar a mano (solo Development)

Para entrar a la aplicación sin registrar una firma por API, guarda un correo y una clave en user-secrets. Al arrancar en Development, la API crea una firma «Firma de desarrollo» y un administrador con esos datos (si no existen):

```bash
dotnet user-secrets set "Desarrollo:UsuarioInicial:Correo" "tu-correo@ejemplo.co" -p Nitido.Api
dotnet user-secrets set "Desarrollo:UsuarioInicial:Clave" "una-clave-de-mas-de-12-caracteres" -p Nitido.Api
```

La primera vez que ingreses la pantalla te pide inscribir el doble factor con una aplicación autenticadora (Google Authenticator, Microsoft Authenticator…) y te entrega 10 códigos de recuperación. Si la base aún no tiene las migraciones, la siembra avisa en el registro y deja arrancar la API. El registro de firmas por API (`POST /api/firmas/registro`) está habilitado en Development y deshabilitado por defecto (`Registro:Habilitado`); las peticiones de ejemplo están en `Nitido.Api/Nitido.Api.http`.

### Comprobar que la base responde

`GET /salud` responde `200` con `Healthy` si SQL Server contesta y `503` con `Unhealthy` si no lo hace en 3 segundos (por ejemplo, con el contenedor apagado).

## Cómo correr la API

Todos los comandos se ejecutan desde la raíz de este repositorio.

```bash
dotnet run --project Nitido.Api
```

La API queda en http://localhost:5080 y necesita la cadena de conexión y el contenedor de SQL Server encendido (ver arriba). Para comprobar que está viva, abre http://localhost:5080/salud: debe responder `Healthy`.

En desarrollo, el documento OpenAPI (el contrato que usa el frontend para generar su cliente) queda en http://localhost:5080/openapi/v1.json. Solo se publica en el entorno `Development`.

Para probar el entorno de producción (los registros salen en JSON, una línea por evento, en vez de texto legible):

```bash
ASPNETCORE_ENVIRONMENT=Production ConnectionStrings__Nitido="<cadena>" dotnet run --project Nitido.Api --no-launch-profile --urls http://localhost:5080
```

Hace falta `--no-launch-profile` porque el perfil de lanzamiento de la API fuerza el entorno `Development` y pisaría la variable `ASPNETCORE_ENVIRONMENT`. Al quitar el perfil también se pierde su dirección, por eso se indica con `--urls`. En PowerShell, define antes la variable con `$env:ASPNETCORE_ENVIRONMENT = "Production"`. En ese entorno `/openapi/v1.json` no existe, y la cadena de conexión hay que darla con la variable de entorno `ConnectionStrings__Nitido` (como en el ejemplo), porque los secretos de usuario no se leen fuera de `Development`.

## Cómo correr las pruebas

Desde la raíz de este repositorio:

```bash
dotnet test
```

Corre las tres baterías de .NET: unidad (`tests/unidad`), integración de la API (`tests/integracion`) y casos contables dorados (`tests/contables`). Para revisar el formato del código sin cambiarlo:

```bash
dotnet format Nitido.slnx --verify-no-changes
```

Las pruebas de integración y de esquema levantan su propio SQL Server en un contenedor (Testcontainers), así que necesitan Docker Desktop encendido; la primera vez descargan la imagen de SQL Server (cerca de 1,5 GB). No usan la cadena de tus secretos de usuario ni tu contenedor de VS Code.

### Si Windows bloquea un archivo

En Windows 11 con **Smart App Control** activo, el sistema puede bloquear al azar un archivo recién compilado y la API o las pruebas fallan al cargarlo con un error como «Una directiva de Control de aplicaciones bloqueó este archivo» (código `0x800711C7`), nombrando un `.dll` de `bin/`. El síntoma suele cambiar de un archivo a otro y a veces desaparece al volver a compilar con `dotnet build`. No es un error del código. Smart App Control no se debe desactivar a la ligera: una vez apagado no se puede volver a encender sin reinstalar Windows, así que si el problema persiste, avisa al equipo con el nombre del archivo bloqueado en vez de cambiar la configuración del sistema.

Las pruebas del frontend (`npm test`) y las de punta a punta (Playwright) se corren en el repositorio del frontend; sus instrucciones están en su README.

## Integración continua

El workflow `.github/workflows/ci.yml` (nombre `CI`) corre en cada pull request hacia `main`, al unir a `main` y a mano desde la pestaña Actions («Run workflow»). Tiene un solo trabajo, llamado `backend`, en un runner `ubuntu-24.04`. Ese nombre es el check que debe exigir la protección de `main`; no lo cambies sin actualizar la regla antes. Si subes cambios nuevos al mismo pull request, GitHub cancela la corrida anterior de ese pull request; no cancela la corrida en curso de `main`.

Qué hace, en este orden, y se detiene en el primer paso que falle:

1. Instala el SDK 10.0 más reciente que permite `global.json` y restaura los paquetes (con caché de NuGet).
2. Compila en Release con `-warnaserror`: cualquier advertencia es error.
3. Revisa el formato con `dotnet format Nitido.slnx --verify-no-changes`.
4. Busca paquetes con vulnerabilidades conocidas, directos o transitivos; si hay alguno, falla (también falla si la búsqueda no devuelve proyectos o no consultó nuget.org).
5. Descarga la imagen de SQL Server que usan las pruebas (la etiqueta sale de `ContenedorSqlServer.Imagen`).
6. Corre `dotnet test` en Release: unidad, integración con Testcontainers y contables. Sube los resultados `.trx` como artefacto `resultados-pruebas-backend`, aunque las pruebas fallen.

El workflow no usa secretos ni cadenas de conexión. Tiene como meta tardar menos de 15 minutos.

Para reproducirlo en tu computador, desde la raíz de este repositorio y con Docker Desktop encendido:

```bash
dotnet restore Nitido.slnx
dotnet build Nitido.slnx -c Release --no-restore -warnaserror
dotnet format Nitido.slnx --verify-no-changes --no-restore
dotnet test Nitido.slnx -c Release --no-build --logger trx --results-directory TestResults
```

El paso de vulnerabilidades se prueba con `dotnet list Nitido.slnx package --vulnerable --include-transitive`: no debe listar ningún paquete. Ojo: ese comando termina con código 0 aunque encuentre algo, así que lee su salida.

La protección de `main` no se crea sola: se configura a mano en GitHub (Settings, Rules, Rulesets), exigiendo pull request y el check `backend` en verde. GitHub solo ofrece un check después de que haya corrido al menos una vez, así que se configura después de la primera corrida. En repositorios privados, la protección de ramas necesita GitHub Pro o Team.

## Frontend

La aplicación Angular vive en el repositorio `nitido-frontend`, que se clona como carpeta hermana de este (por ejemplo, `nitido/backend` y `nitido/frontend`). Contiene también las pruebas de punta a punta (`e2e/`), el mockup y el logo.

El contrato entre los dos repositorios es OpenAPI: el backend publica `/openapi/v1.json` y el frontend genera su cliente a partir de él. Para ver el frontend funcionando contra esta API, deja la API corriendo en el puerto 5080 y sigue el README del repositorio del frontend. Un cambio que toca la API y una pantalla son dos pull requests coordinados, con el del backend primero.

## Estructura de carpetas

```
nitido-backend/
  CLAUDE.md                    reglas y forma de trabajo del proyecto
  Nitido.slnx                  solución de .NET
  global.json                  versión del SDK de .NET
  Directory.Build.props, Directory.Packages.props   configuración común y versiones de paquetes
  Nitido.Api/                  host de ASP.NET Core (API)
  Nitido.Compartido/           tipos comunes, eventos, multiempresa
  Nitido.Compartido.Infraestructura/   EF Core común: convenciones de nombres, auditoría, tablas temporales
  Modulos/<Modulo>/            un módulo por área: Dominio, Aplicacion, Infraestructura, Api
  tests/
    unidad/                    pruebas de unidad
    integracion/               pruebas de la API
    contables/                 casos dorados de contabilidad
  db/                          scripts/ con el SQL de las migraciones (semillas/ y seguridad-por-fila/ llegan después)
  docs/
    plan-de-desarrollo.md      plan completo
    specs/                     una especificación por funcionalidad
    adr/                       decisiones de arquitectura
```

## Despliegue

La fase 0 no tiene despliegue: el proveedor donde se va a publicar nítido todavía no está decidido. Por ahora todo corre en tu computador.

## Para saber más

- [CLAUDE.md](CLAUDE.md): reglas que nunca se rompen, comandos y forma de trabajo.
- [docs/plan-de-desarrollo.md](docs/plan-de-desarrollo.md): plan de desarrollo completo.
- [docs/specs/fase-0-indice.md](docs/specs/fase-0-indice.md): historias de la fase 0 y estructura de los repositorios.
- Repositorio del frontend: `nitido-frontend`.
