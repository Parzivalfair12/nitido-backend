# F0-01 · Fase 0 · 1 · Esqueleto del repositorio

| | |
| --- | --- |
| Estimación | 2 días |
| Depende de | ninguna |
| Orden de construcción | 1 de 10 |
| Repositorio | Ambos. Se escribió para un monorepo; desde el 18 de octubre de 2026 el backend (.NET, `db/`, `tests/`, `docs/`) va en `nitido-backend` y el frontend (Angular, `e2e/`, `.nvmrc`, mockup y logo) en `nitido-frontend`. Ver «Estructura de repositorios» en `fase-0-indice.md`. |
| Etiquetas | `fase-0`, `historia`, `infraestructura`, `backend`, `frontend` |
| Estado | Especificación aprobada, con diseño técnico. Sin issue en GitHub. |

> **Ver ADR 0001** (`docs/adr/0001-clean-architecture.md`). En F0-01b, la prueba `Compartido_NoDependeDeSdkDeNube` se renombra a `SdkDeNube_SoloEnProyectosDeProveedor` y se amplía, sin aflojar la regla 7. El criterio de abajo queda como registro histórico.

**Prerrequisitos, a cargo del usuario o del coordinador:**
- Instalar el SDK de .NET 10. Hoy no está instalado; Node 24.21.0, npm 11.19.0, Docker 29.8.1 y git 2.55 sí lo están.
- Decidir si se permite `git init` local sin remoto (pregunta abierta 1 del índice). Sin él no se puede verificar el criterio del `.gitignore`.
- Acceso a internet a nuget.org y al registro de npm.

## Historia

Como desarrollador de nítido quiero una solución .NET 10 y una aplicación Angular 22 que compilen y se prueben desde el primer día para que cada historia siguiente arranque sobre una base común y ordenada.

## Criterios de aceptación

### Raíz y solución

- [ ] `global.json` fija el SDK 10.0.100 con `rollForward: latestFeature`. En la raíz, `dotnet --version` muestra `10.0.x`.
- [ ] `Nitido.slnx` contiene exactamente los cinco proyectos de la sección «Estructura» del diseño técnico.
- [ ] `dotnet build` termina con «0 Advertencias» y «0 Errores». `Directory.Build.props` activa `TreatWarningsAsErrors`, `Nullable` e `ImplicitUsings`.
- [ ] `Directory.Packages.props` tiene `ManagePackageVersionsCentrally=true`, y ningún `.csproj` lleva el atributo `Version` en un `PackageReference`. Todas las versiones son exactas, sin `*`.
- [ ] `dotnet list package --vulnerable --include-transitive` no reporta paquetes vulnerables.
- [ ] `dotnet format --verify-no-changes` sale con código 0.
- [ ] `dotnet test` pasa, y cada uno de los tres proyectos de pruebas ejecuta al menos una prueba.

### API

- [ ] `dotnet run --project Nitido.Api` arranca en http://localhost:5080. `GET http://localhost:5080/salud` responde `200` con el cuerpo `Healthy`. Lo comprueba la prueba de integración `Salud_RespondeOk`.
- [ ] `GET http://localhost:5080/no-existe` responde `404` con `Content-Type: application/problem+json`. Lo comprueba la prueba de integración `RutaInexistente_RespondeProblemDetails`.
- [ ] En Development, `GET /openapi/v1.json` responde `200`. Con `ASPNETCORE_ENVIRONMENT=Production` responde `404`. Lo comprueba la prueba `OpenApi_SoloEnDevelopment`.
- [ ] Al arrancar en Development, la consola muestra el registro legible de Serilog. Con `ASPNETCORE_ENVIRONMENT=Production` cada línea es JSON compacto e incluye la propiedad `"Aplicacion":"nitido-api"`.
- [ ] Las peticiones a `/salud` no aparecen en el registro de peticiones con el nivel `Information`.

### Reglas de `CLAUDE.md`

- [ ] La prueba de unidad `Compartido_NoDependeDeSdkDeNube` recorre los ensamblados `Nitido.*` y falla si alguno referencia un ensamblado cuyo nombre empiece por `Azure.`, `Amazon`, `AWSSDK` o `Google.Cloud` (regla 7).
- [ ] Ningún `appsettings*.json` contiene cadenas de conexión, claves ni secretos: `git grep -niE "password=|pwd=|user id=|secret"` no da resultados en archivos versionados.

### Frontend

Estos criterios se verifican en la raíz del repositorio del frontend (`nitido-frontend`).

- [ ] El proyecto se creó con la plantilla oficial de Angular 22, con componentes standalone, SCSS y sin SSR. `npx ng version` muestra `22.x`.
- [ ] `npm start` sirve http://localhost:4200. La página muestra un encabezado con el texto «nítido», el `<html>` tiene `lang="es-CO"` y el título de la pestaña es «nítido».
- [ ] `LOCALE_ID` es `es-CO`. La prueba unitaria `formato-es-co` comprueba que `formatNumber(1234567.5, 'es-CO', '1.2-2')` devuelve `1.234.567,50`.
- [ ] `npm test` corre una sola vez, sin modo observador, con el framework que trae la plantilla, y pasa.
- [ ] `npm run build` termina sin errores.
- [ ] `npm run lint` (angular-eslint) sale con código 0.
- [ ] Con la API y el frontend corriendo, `GET http://localhost:4200/api/no-existe` responde el `404` en formato ProblemDetails de la API. Eso prueba que el proxy funciona, sin CORS de por medio.
- [ ] El favicon es el logo de `docs/logo-nitido.svg` (del repositorio del frontend).

### Punta a punta

- [ ] Con el frontend corriendo, y desde la raíz del repositorio del frontend, `cd e2e && npx playwright install chromium && npx playwright test` pasa la prueba `inicio.spec.ts`: abre `/`, ve el encabezado «nítido» y comprueba `lang="es-CO"`.

### Repositorio

- [ ] `.gitattributes` normaliza los finales de línea a LF, salvo `*.cmd` y `*.bat`.
- [ ] En el repositorio del backend, después de `dotnet build` y `dotnet test`, `git status --porcelain` no muestra archivos generados. En el del frontend, después de `npm install` y `npm run build`, y de `npm install` en `e2e/`, ocurre lo mismo. Esto requiere `git init` local en cada uno (ver prerrequisitos).
- [ ] Existen `db/README.md`, `docs/adr/README.md` (con la plantilla de ADR) y `Modulos/.gitkeep`, en el repositorio del backend.
- [ ] El `README.md` de la raíz de cada repositorio está en español de Colombia e incluye:
  - requisitos;
  - cómo crear el SQL Server local desde VS Code (lo usará F0-02a);
  - cómo correr la API, el frontend y las tres baterías de pruebas;
  - una nota de que la fase 0 no tiene despliegue;
  - un enlace a `CLAUDE.md`.

  Una persona distinta a quien lo escribió sigue el README en otra máquina y levanta API y frontend sin pasos que no estén escritos.

## Pantalla

No aplica. La página inicial es provisional: solo el texto «nítido». El estilo Cristal llega en F0-05a.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables ni tributarias.

## Diseño técnico

### 1. Módulos que cambian

Ningún módulo de negocio todavía; Firmas y Clientes llegan en F0-02a. Se crean el host `Nitido.Api` y la librería `Nitido.Compartido`, que en esta historia queda vacía salvo por un marcador de ensamblado. `Nitido.Compartido` no referencia ASP.NET Core ni EF Core.

### 2. Estructura exacta

Dos repositorios (decisión del 18 de octubre de 2026). Esta especificación se escribió para un monorepo; el contenido es el mismo, repartido así.

**Repositorio `nitido-backend`:**

```
nitido-backend/
  .editorconfig                  reglas de C#; las aplica dotnet format
  .gitattributes                 * text=auto eol=lf; *.cmd, *.bat eol=crlf; *.png binary
  .gitignore                     .NET + carpeta del almacén local (almacen/) + secretos
  global.json                    { "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
  Directory.Build.props
  Directory.Packages.props
  Nitido.slnx
  README.md
  CLAUDE.md, .claude/            ya existen; no se tocan
  Nitido.Api/
    Nitido.Api.csproj            Microsoft.NET.Sdk.Web; referencia Nitido.Compartido
    Program.cs
    appsettings.json
    appsettings.Development.json
    Properties/launchSettings.json   un perfil "http" en http://localhost:5080
    Nitido.Api.http              GET /salud y GET /openapi/v1.json
  Nitido.Compartido/
    Nitido.Compartido.csproj     Microsoft.NET.Sdk (classlib)
  Modulos/.gitkeep
  tests/
    unidad/Nitido.Pruebas.Unidad/                referencia Nitido.Compartido y Nitido.Api
    integracion/Nitido.Pruebas.Integracion/      referencia Nitido.Api (WebApplicationFactory)
    contables/Nitido.Pruebas.Contables/          referencia Nitido.Compartido; tiene casos/.gitkeep
  db/README.md                   qué va en semillas/ y seguridad-por-fila/ (desde F0-02a y F0-02b)
  docs/adr/README.md             índice y plantilla: contexto, opciones, decisión, consecuencias
  docs/specs/                    ya existe
```

**Repositorio `nitido-frontend`** (la aplicación Angular ocupa la raíz del repositorio):

```
nitido-frontend/
  .nvmrc                         24
  .editorconfig, .gitignore, .gitattributes   los que genera la plantilla de Angular y los de Node/Playwright
  README.md
  proxy.conf.json
  public/favicon.svg             copia de docs/logo-nitido.svg
  src/app/nucleo/.gitkeep        servicios, guardias, interceptores (desde F0-03a y F0-05b)
  src/app/compartido/.gitkeep    componentes Cristal (desde F0-05a)
  src/app/areas/.gitkeep         una carpeta de rutas por área del menú (plan: «Repositorio y convenciones»)
  e2e/
    package.json, package-lock.json, tsconfig.json, playwright.config.ts
    pruebas/inicio.spec.ts
  docs/mockup-nitido.dc.html, docs/logo-nitido.svg
```

`tests/xml/` no se crea: llega con el lector de la DIAN en la fase 2.

`Directory.Build.props` en la raíz aplica a todos los proyectos .NET:

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <InvariantGlobalization>false</InvariantGlobalization>
  </PropertyGroup>
</Project>
```

- `InvariantGlobalization=false` deja disponible la cultura es-CO, que se necesitará para PDF y Excel.
- No se sube `AnalysisLevel` en esta historia. Activar reglas CA con advertencias como errores es una decisión aparte.

### 3. Paquetes y versiones (`Directory.Packages.props`)

Regla: versiones exactas, sin comodines. Se toma la última versión estable al implementar y se registra en el archivo.
- Para confirmarla se usa `dotnet package search <id> --exact-match`.
- `dotnet list package --vulnerable --include-transitive` debe quedar limpio.

No pude consultar nuget.org desde mi entorno. La columna «Línea» es la versión mayor esperada, no un número verificado.

| Paquete | Proyecto | Línea | Para qué |
| --- | --- | --- | --- |
| `Serilog.AspNetCore` | Nitido.Api | 10.x (o la última estable que soporte net10.0) | Registro estructurado. Trae la consola, la configuración y el formato compacto. |
| `Microsoft.AspNetCore.OpenApi` | Nitido.Api | 10.0.x, la misma del runtime | Documento OpenAPI (`/openapi/v1.json`). |
| `xunit.v3` | las tres de pruebas | 3.x | Marco de pruebas. |
| `xunit.runner.visualstudio` | las tres de pruebas | 3.x | Ejecución con `dotnet test`. |
| `Microsoft.NET.Test.Sdk` | las tres de pruebas | 18.x | Ejecución con `dotnet test`. |
| `coverlet.collector` | las tres de pruebas | 6.x | Cobertura. El plan pide 90 % en dominio. |
| `Microsoft.AspNetCore.Mvc.Testing` | Pruebas.Integracion | 10.0.x | `WebApplicationFactory` para probar la API. |

**No se agregan:**
- FluentAssertions, que pasó a licencia comercial desde la versión 8. Se usa `Assert` de xUnit.
- Librerías de dobles de prueba: no hacen falta todavía.
- Hangfire y OpenTelemetry: llegan cuando una historia los use.

**Llegan en historias siguientes:**
- EF Core 10 y `Testcontainers.MsSql` (F0-02a).
- `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (F0-03a).
- `FluentValidation` (F0-04).

**Frontend y e2e:**
- `@angular/*` y `@angular/cli` en la línea 22, con el `package-lock.json` versionado. Se conservan los rangos que ponga la plantilla y se instala con `npm ci` en CI.
- `angular-eslint` en la línea 22.
- `@playwright/test` con versión **exacta** en `e2e/package.json`, porque los navegadores dependen de la versión.

### 4. Endpoints

| Método | Ruta | Entrada | Salida | Errores |
| --- | --- | --- | --- | --- |
| GET | `/salud` | ninguna | `200 text/plain` con `Healthy` | `503 Unhealthy` cuando falle una comprobación (desde F0-02a) |
| GET | `/openapi/v1.json` | ninguna | `200 application/json`, solo en Development | `404` fuera de Development |

**`/salud`:**
- Se configura con `AddHealthChecks()` y `MapHealthChecks("/salud")`. En F0-01 no tiene comprobaciones, así que siempre responde `Healthy`.
- F0-02a agrega la comprobación de SQL Server.
- Lleva `.AllowAnonymous()` desde ya, porque F0-03a exigirá sesión por defecto.
- No guarda en caché y no expone detalles: los consumen máquinas, no personas.

**Errores:**
- `AddProblemDetails()`, `UseExceptionHandler()` y `UseStatusCodePages()`: toda respuesta de error usa ProblemDetails (RFC 9457).
- El `title` del error 500 se cambia a «Ocurrió un error inesperado. Intenta de nuevo en unos minutos.» Nunca se devuelve el detalle de la excepción.

**Red:**
- En local, la API usa solo HTTP en el puerto 5080. HTTPS y HSTS se definen con el despliegue, que no está en la fase 0.
- No se configura CORS: el navegador ve un solo origen gracias al proxy de Angular.

### 5. Eventos internos y trabajos en segundo plano

Ninguno.

### 6. Serilog

**Arranque en dos etapas:**
- Un registrador inicial (`CreateBootstrapLogger`) escribe en la consola mientras se arma el host.
- `try`/`catch` alrededor de `app.Run()` registra «La API no pudo arrancar» con `Log.Fatal`, y `Log.CloseAndFlush()` va en el `finally`.

**Host:** `builder.Services.AddSerilog((sp, lc) => lc.ReadFrom.Configuration(builder.Configuration).ReadFrom.Services(sp).Enrich.FromLogContext())`.

**Registro de peticiones:** `app.UseSerilogRequestLogging()`, con `GetLevel` en `Verbose` para `/salud`, de modo que no llene el registro.

**`appsettings.json`:**
- `MinimumLevel.Default = Information`.
- `Override`: `Microsoft.AspNetCore = Warning` y `Microsoft.EntityFrameworkCore.Database.Command = Warning`. Este último aplica desde F0-02a.
- `WriteTo`: consola con `Serilog.Formatting.Compact.RenderedCompactJsonFormatter`.
- `Properties.Aplicacion = nitido-api`.
- Se elimina la sección `Logging` de la plantilla.

**`appsettings.Development.json`:** consola legible con `outputTemplate` `[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}`.

**Regla desde el día 1:** no se registran cuerpos de petición, cookies, el encabezado `Authorization` ni datos personales. Se registran identificadores (`UUID_`), no nombres ni NIT.

### 7. Angular 22

**Creación**, en la raíz del repositorio del frontend. En el monorepo original se ejecutó con `--directory frontend`; ahora la aplicación ocupa la raíz del repositorio (`--directory .`):

```bash
npx @angular/cli@22 new nitido-web --directory . --style scss --ssr false --routing --skip-git --package-manager npm --prefix nt
```

- Si la CLI pregunta por configuración de IA, se responde «ninguna».
- Se acepta lo que traiga la plantilla: standalone, modo estricto, sin zone.js si así viene, nombres de archivo sin sufijo `.component` si así vienen, y el framework de pruebas por defecto.
- El plan pide tomar el framework de pruebas por defecto de la plantilla; en Angular 22 se espera Vitest. Si la plantilla trae otro, se usa ese y se anota en el PR.

**Ajustes después de crearlo:**
- `ng add angular-eslint`, que agrega el script `npm run lint`.
- Scripts de `package.json`:
  - `"test": "ng test --watch=false"`, para que `npm test` corra una sola vez como dice `CLAUDE.md`;
  - `"test:observar": "ng test"`.
- `angular.json`, en `serve.options`: `"proxyConfig": "proxy.conf.json"`.
- `app.config.ts`:
  - `registerLocaleData(localeEsCO)`, con `localeEsCO` importado de `@angular/common/locales/es-CO`;
  - `{ provide: LOCALE_ID, useValue: 'es-CO' }`;
  - `provideHttpClient(withFetch())`.
- `index.html`: `lang="es-CO"` y `<title>nítido</title>`.
- `public/favicon.svg`, copiado de `docs/logo-nitido.svg`, y se borra el `favicon.ico` de la plantilla.
- El componente raíz muestra solo `<h1>nítido</h1>`; se elimina el contenido de bienvenida de la plantilla.
- Angular CDK no se instala todavía: llega en F0-05a.
- Se conservan los `.gitignore` y `.editorconfig` que genere la plantilla.

**`proxy.conf.json`:**

```json
{ "/api": { "target": "http://localhost:5080", "secure": false } }
```

### 8. Playwright

- `e2e/` (en la raíz del repositorio del frontend) es un proyecto aparte con su propio `package.json`. Así sus dependencias no se mezclan con las del frontend y F0-06 lo puede correr como un trabajo independiente más adelante.
- `playwright.config.ts`:
  - `testDir: './pruebas'`;
  - `use.baseURL = process.env.NITIDO_URL ?? 'http://localhost:4200'`;
  - un proyecto `computador`: Desktop Chrome a 1440×900. F0-05a agrega `celular`, a 390×844;
  - `trace: 'retain-on-failure'`;
  - reportes `list` y `html` (`open: 'never'`).
- Sin `webServer`: como dice `CLAUDE.md`, las pruebas punta a punta requieren la aplicación corriendo.
- Navegador: solo Chromium en la fase 0.
- El comando es `cd e2e && npx playwright test`, desde la raíz del repositorio del frontend (ya reflejado en `CLAUDE.md`).

### 9. Pruebas que define el tester

| Prueba | Proyecto | Qué verifica |
| --- | --- | --- |
| `Compartido_NoDependeDeSdkDeNube` | Pruebas.Unidad | Regla 7: ningún ensamblado `Nitido.*` referencia SDK de nube. |
| `Salud_RespondeOk` | Pruebas.Integracion | `GET /salud` responde `200 Healthy`. |
| `RutaInexistente_RespondeProblemDetails` | Pruebas.Integracion | `404` con `application/problem+json`. |
| `OpenApi_SoloEnDevelopment` | Pruebas.Integracion | `200` en Development y `404` en Production. |
| `CasosDorados_CarpetaExiste` | Pruebas.Contables | La carpeta `tests/contables/casos/` existe. Es solo un marcador: la fase 0 no tiene casos contables. |
| `formato-es-co` | frontend (unitaria) | Formato es-CO de números. |
| `inicio.spec.ts` | e2e | La página carga, con el texto y el idioma correctos. |

### 10. Riesgos

- **SDK de .NET no instalado:** bloquea todo el backend hasta que el coordinador lo instale. El frontend puede avanzar mientras tanto.
- **Plantilla de Angular 22 distinta a lo supuesto** (framework de pruebas, zone.js, nombres de archivo): manda la plantilla. Las diferencias se anotan en el PR y no se fuerzan.
- **Advertencias como errores:** el código de las plantillas de .NET puede generar advertencias de estilo. Se corrigen al crear los proyectos; no se apaga la regla.
- **Finales de línea** entre Windows (desarrollo) y Linux (CI): los resuelve `.gitattributes` desde el primer commit.
- **Versiones de paquetes no verificadas por el arquitecto:** las fija `dev-backend` al implementar, según la regla de la sección 3.
- **Puerto 5080 ocupado:** se cambia en `launchSettings.json` y en `proxy.conf.json` a la vez.
- **Seguridad y multiempresa:** no aplican en esta historia; no hay datos. La única regla activa es la 7 (sin SDK de nube), verificada por prueba, junto con la de no versionar secretos.
- **Despliegue:** no hay en la fase 0. No se crean Dockerfile ni imágenes.

### 11. Plan de implementación

| Paso | Agente | Qué | En paralelo con |
| --- | --- | --- | --- |
| 0 | usuario o coordinador | Instalar el SDK de .NET 10; decidir `git init` local. | ninguno |
| 1 | `dev-backend` | Archivos de la raíz del repositorio del backend: `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitattributes`, `.gitignore` (el `.nvmrc` va en el repositorio del frontend). Solución `Nitido.slnx` con los cinco proyectos vacíos y sus referencias. | paso 1-F |
| 1-F | `dev-frontend` | `ng new` en la raíz del repositorio del frontend, `.nvmrc`, angular-eslint, scripts, proxy, locale es-CO, página «nítido», favicon, carpetas `nucleo`, `compartido` y `areas`. | pasos 1, 2 y 3 |
| 2 | `tester` | Pruebas de .NET de la sección 9 (deben compilar y fallar). Proyecto `e2e/` (repositorio del frontend) con `inicio.spec.ts`. | paso 1-F |
| 3 | `dev-backend` | `Program.cs` (Serilog, ProblemDetails, OpenAPI, `/salud`), `appsettings*.json`, `launchSettings.json`, `Nitido.Api.http`. | paso 1-F |
| 4 | `dev-backend` y `dev-frontend` | README (cada uno su parte), `db/README.md`, `docs/adr/README.md`. | entre sí |
| 5 | `tester` | Verificación: corre cada criterio con su comando y reporta. | ninguno |
| 6 | `revisor` | Revisión: secretos, convenciones, regla 7, `.gitignore`. | ninguno |

## Fuera de alcance

- Módulos de negocio y base de datos (F0-02a).
- Hangfire, OpenTelemetry, Dockerfile y `docker-compose.yml`: se agregan cuando una historia los necesite.
- `tests/xml/`, que llega en la fase 2.
- Integración continua (F0-06).
- Estilo Cristal (F0-05a).
- **Cualquier despliegue:** el proveedor de nube no está decidido.
