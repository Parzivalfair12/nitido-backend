# F0-06 · Fase 0 · 6 · Integración continua en GitHub Actions

| | |
| --- | --- |
| Estimación | 1 día |
| Repositorio | Ambos. La integración continua se parte en dos workflows, uno por repositorio: `ci.yml` en `nitido-backend` (trabajo `backend`) y `ci.yml` en `nitido-frontend` (trabajo `frontend`). El resto del alcance no cambia. |
| Depende de | F0-02a, para que ya existan pruebas con Testcontainers. Requiere los dos repositorios creados en GitHub. |
| Orden de construcción | 3 de 10, justo después de F0-02a |
| Etiquetas | `fase-0`, `historia`, `ci`, `infraestructura` |
| Estado | Especificación aprobada. Diseño técnico completo (paso 3, 2026-10-18, `arquitecto`). Ajustes A1, A2, A5, A6 y A7 decididos por el coordinador (2026-10-18) y reflejados en los criterios; A3 y A4 siguen pendientes del usuario. Workflows `ci.yml` escritos y validados en local (paso 1 del plan, `devops`). Recorrido local, ensayo en Linux y pruebas negativas N1 a N6 hechos por el `tester` el 2026-10-18 (paso 4 del plan, tabla 6.6); revisión del `revisor` hecha el 2026-10-18 (paso 5): sin hallazgos graves ni medios; los hallazgos bajos se corrigieron o se aplazaron (ver el paso 5 de la sección 8). Sin issue en GitHub. |

> **Ver ADR 0001.** Los criterios no cambian. Las pruebas de arquitectura de F0-01b corren dentro de `dotnet test` y leen `Nitido.slnx` y los `.csproj`, así que el trabajo corre desde la raíz del checkout. Con más proyectos por módulo, hay que vigilar la meta de 15 minutos.

**Bloqueo conocido:** hoy ninguno de los dos directorios es un repositorio git y el usuario pidió no subir nada todavía. Esta historia no puede verificarse hasta que existan los repositorios en GitHub.

**Dos workflows, uno por repositorio (decisión del 18 de octubre de 2026).** Los criterios de abajo se reparten así: «Workflow» sobre el trabajo `backend` se cumple en `nitido-backend`, y sobre el trabajo `frontend`, en `nitido-frontend`. Los criterios generales (disparadores, permisos, caché, secretos, acciones fijadas, prueba del workflow y protección de `main`) aplican a cada uno de los dos repositorios.

## Historia

Como equipo de nítido queremos que cada pull request se compile y se pruebe automáticamente para que nada roto llegue a `main`.

## Criterios de aceptación

### Workflow

- [ ] `.github/workflows/ci.yml` corre en `pull_request` hacia `main`, en `push` a `main` (para que `main` tenga caché) y en `workflow_dispatch`. Tiene `permissions: contents: read` y `concurrency`, que cancela la corrida anterior del mismo PR (no cancela la corrida en curso de `main`). Existe uno en cada repositorio.
- [ ] En `nitido-backend`, el trabajo `backend` (Ubuntu 24.04, fijo: `ubuntu-24.04`) toma la versión de .NET de `global.json` y corre:
  - `dotnet build -c Release -warnaserror`, que falla con cualquier advertencia (de `Nitido.slnx`, con `--no-restore`);
  - `dotnet format Nitido.slnx --verify-no-changes`;
  - una búsqueda de paquetes con vulnerabilidades conocidas, directos o transitivos (`dotnet list package --vulnerable --include-transitive`, leyendo su salida JSON con `jq`): el trabajo falla si encuentra alguno;
  - `dotnet test -c Release`: unidad, integración con Testcontainers y SQL Server 2025, y contables. Sube los resultados TRX como artefacto.
- [ ] En `nitido-frontend`, el trabajo `frontend` (Ubuntu 24.04, fijo: `ubuntu-24.04`) toma Node de `.nvmrc` y, en la raíz del repositorio, corre:
  - `npm ci`;
  - `npm run lint`;
  - `npm run lint:estilos`, cuando exista (F0-05a);
  - `npm test`, sin modo observador;
  - `npm run build`.
- [ ] Hay caché de NuGet y de npm, y la corrida completa tarda menos de 15 minutos.
- [ ] El workflow no usa secretos aparte de `GITHUB_TOKEN`, ni contiene cadenas de conexión. La clave de SQL Server de las pruebas es aleatoria en cada corrida (la pone `ContenedorSqlServer`, con `.WithPassword(...)`, ajuste A5).
- [ ] Solo se usan acciones oficiales de GitHub (`actions/*`), fijadas por SHA completo con la etiqueta en un comentario.

### Prueba del propio workflow

- [ ] Un PR con una prueba que falla a propósito queda en rojo.
- [ ] Un PR con una advertencia de compilación queda en rojo.
- [ ] Al corregir ambos, el PR queda en verde. Los enlaces a esas corridas van en el PR de esta historia.

### Protección de `main`

- [ ] El usuario configura a mano la regla de protección de `main` en GitHub: exige PR y el check del propio repositorio en verde (`backend` en `nitido-backend`, `frontend` en `nitido-frontend`). La evidencia es una captura en el PR, porque `gh` no está instalado.

## Pantalla

No aplica.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables.

## Diseño técnico

2026-10-18 · arquitecto. Lo implementa el agente `devops` (paso 5).

**Resumen.** Hay dos workflows independientes, uno por repositorio, cada uno con un solo trabajo de nombre fijo: `backend` y `frontend`. Solo usan acciones oficiales de GitHub (`actions/*`), fijadas por SHA completo. No tienen secretos, no despliegan y no cachean la imagen de SQL Server. Hoy no existe el repositorio en GitHub, así que la historia se cierra en dos etapas:

1. **Ahora:** se implementa y se verifica en local, con un ensayo en contenedores Linux que reproduce cada paso.
2. **Cuando existan los repositorios:** se verifica en GitHub (los PR en rojo y en verde, y la protección de `main`).

Ver los ajustes A1 a A7 al final de esta sección. El coordinador decidió A1, A2, A5, A6 y A7 el 2026-10-18; ya están en los criterios y en los workflows.

**No aplica en esta historia:** tablas, columnas, endpoints, eventos internos, trabajos de Hangfire, pantallas y componentes de Angular. Ningún módulo del backend cambia. Tampoco hace falta un ADR: no entra ninguna librería en el producto, ni cambia un patrón o una frontera entre módulos. La separación en dos repositorios ya quedó registrada en `fase-0-indice.md`.

### 1. Repositorios y archivos que cambian

| Repositorio | Archivo | Qué |
| --- | --- | --- |
| `nitido-backend` | `.github/workflows/ci.yml` | Nuevo. Workflow `CI` con el trabajo `backend`. |
| `nitido-backend` | `README.md` | Sección nueva, «Integración continua», con el recorrido local de la sección 6. También se corrige la frase «En CI […] la cadena va en `ConnectionStrings__Nitido`»: en CI no se usa, porque la pone Testcontainers (ver la sección 4). |
| `nitido-frontend` | `.github/workflows/ci.yml` | Nuevo. Workflow `CI` con el trabajo `frontend`. |
| `nitido-frontend` | `README.md` | Sección «Integración continua»: qué corre y cómo reproducirlo. |

No se crean `dependabot.yml`, CODEOWNERS, `codeql.yml`, `deploy.yml` ni Dockerfile: están fuera de alcance. Tampoco se tocan `global.json`, `Directory.*.props`, los `.csproj` ni `package.json`. La única excepción posible es el ajuste A5, que agrega una línea en el código de pruebas.

### 2. Los workflows

#### 2.1 Lo común a los dos

- **Disparadores:** `pull_request` hacia `main` y `workflow_dispatch`, como pide la especificación. Se propone además `push` a `main` (ajuste A2) para que `main` tenga caché.
- **Permisos:** `permissions: contents: read` en el workflow. La caché y los artefactos no necesitan más permisos.
- **Concurrencia:**
  - Grupo: `${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}`.
  - `cancel-in-progress: ${{ github.event_name == 'pull_request' }}`: se cancela la corrida anterior del mismo PR, pero no la corrida en curso de `main`. Con `cancel-in-progress: false`, GitHub deja terminar la corrida en curso del grupo, pero sí reemplaza a la que está en espera: si se unen tres PR seguidos, la corrida en curso de `main` termina, la del medio se descarta y corre la última. Como cada corrida de `main` solo llena la caché y vuelve a comprobar `main`, no se pierde nada de fondo.
- **Runner:** `ubuntu-24.04` (ajuste A1, en lugar de `ubuntu-latest`).
- **Shell:** `defaults.run.shell: bash`. Con eso GitHub usa `bash -eo pipefail`, y un comando que falla dentro de una tubería hace fallar el paso.
- **Tiempo máximo del trabajo (`timeout-minutes`):** 20 en `backend` y 10 en `frontend`. Si un contenedor se cuelga, el trabajo no consume las 6 horas que GitHub permite por defecto.
- **`actions/checkout` con `persist-credentials: false`:** ningún paso necesita el token después de obtener el código.
- **Nombres:**
  - El workflow se llama `CI`.
  - El trabajo tiene `name` igual a su identificador (`backend` o `frontend`) y no usa matriz. Así, el nombre del check que se exige en la protección de `main` no cambia (ver la sección 5).
- **Acciones**, todas oficiales de GitHub y fijadas por SHA completo, con la etiqueta como comentario. Cada SHA se comprobó contra la API de GitHub (`/repos/<acción>/git/ref/tags/<etiqueta>`) el 2026-10-18:

| Acción | Etiqueta | SHA | Publicada |
| --- | --- | --- | --- |
| `actions/checkout` | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` | 20 de julio de 2026 (el commit es del 17 de julio de 2026) |
| `actions/setup-dotnet` | v6.0.0 | `a98b56852c35b8e3190ac28c8c2271da59106c68` | 16 de julio de 2026 |
| `actions/cache` | v6.1.0 | `55cc8345863c7cc4c66a329aec7e433d2d1c52a9` | 26 de junio de 2026 |
| `actions/upload-artifact` | v7.0.2 | `cf430e030ddbb5b0abf93d22962f4752f3646cd9` | 7 de octubre de 2026 |
| `actions/setup-node` | v7.1.0 | `949feb2413d6458794dcd2491c4babbbce0c15c1` | 8 de octubre de 2026 |

Sin Dependabot (fuera de alcance), estos SHA no se actualizan solos. `devops` los revisa cada trimestre o cuando GitHub anuncie que se retira la versión de Node que usan.

#### 2.2 `nitido-backend`, trabajo `backend`

Referencia que `devops` transcribe. Los nombres de los pasos van en español porque se ven en la interfaz de GitHub.

```yaml
name: CI

on:
  pull_request:
    branches: [main]
  push:                 # ajuste A2 (aprobado)
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read

concurrency:
  group: ${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}
  cancel-in-progress: ${{ github.event_name == 'pull_request' }}

defaults:
  run:
    shell: bash

env:
  DOTNET_NOLOGO: "true"
  DOTNET_CLI_TELEMETRY_OPTOUT: "true"

jobs:
  backend:
    name: backend
    runs-on: ubuntu-24.04          # ajuste A1
    timeout-minutes: 20
    steps:
      - name: Obtener el código
        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false

      - name: Instalar .NET según global.json
        uses: actions/setup-dotnet@a98b56852c35b8e3190ac28c8c2271da59106c68 # v6.0.0
        with:
          global-json-file: global.json

      # La caché queda fuera del checkout (~/.nuget/packages): las pruebas de
      # arquitectura recorren el árbol buscando Directory.Build.props.
      - name: Caché de paquetes NuGet
        uses: actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9 # v6.1.0
        with:
          path: ~/.nuget/packages
          key: nuget-${{ runner.os }}-${{ hashFiles('global.json', 'Directory.Packages.props', '**/*.csproj') }}
          restore-keys: |
            nuget-${{ runner.os }}-

      - name: Restaurar paquetes
        run: dotnet restore Nitido.slnx

      - name: Compilar en Release (cualquier advertencia es error)
        run: dotnet build Nitido.slnx -c Release --no-restore -warnaserror

      - name: Revisar el formato
        run: dotnet format Nitido.slnx --verify-no-changes --no-restore

      # dotnet list package --vulnerable termina con código 0 aunque encuentre
      # algo, por eso se lee la salida JSON con jq.
      - name: Buscar paquetes con vulnerabilidades
        run: |
          dotnet list Nitido.slnx package --vulnerable --include-transitive --format json --output-version 1 > vulnerables.json
          jq -e '(.projects | length) > 0' vulnerables.json > /dev/null \
            || { echo "::error::dotnet list no devolvió proyectos"; exit 1; }
          jq -e '(.sources // []) | any(. == "https://api.nuget.org/v3/index.json")' vulnerables.json > /dev/null \
            || { echo "::error::La búsqueda no consultó nuget.org, así que no hay datos de vulnerabilidades"; exit 1; }
          vulnerables=$(jq -r '[.projects[].frameworks[]? | (.topLevelPackages[]?, .transitivePackages[]?)
            | select((.vulnerabilities // []) | length > 0) | "\(.id) \(.resolvedVersion)"] | unique | .[]' vulnerables.json)
          if [ -n "$vulnerables" ]; then
            echo "::error::Hay paquetes con vulnerabilidades conocidas:"
            echo "$vulnerables"
            exit 1
          fi

      # La etiqueta se lee de la constante ContenedorSqlServer.Imagen para no
      # tener una segunda copia que se desactualice. El "|| true" evita que
      # pipefail aborte antes de imprimir el ::error:: si el grep no encuentra nada.
      - name: Descargar la imagen de SQL Server
        timeout-minutes: 8
        run: |
          imagen=$(grep -oE 'mcr\.microsoft\.com/mssql/server:[A-Za-z0-9._-]+' \
            tests/integracion/Nitido.Pruebas.Integracion/Ayudantes/ContenedorSqlServer.cs | head -n 1 || true)
          [ -n "$imagen" ] || { echo "::error::No se encontró la imagen en ContenedorSqlServer.cs"; exit 1; }
          for _ in 1 2 3; do
            docker pull --quiet "$imagen" && exit 0
            sleep 15
          done
          exit 1

      - name: Probar (unidad, integración y contables)
        id: pruebas
        run: dotnet test Nitido.slnx -c Release --no-build --logger trx --results-directory TestResults

      - name: Subir los resultados TRX
        if: ${{ !cancelled() && steps.pruebas.outcome != 'skipped' }}
        uses: actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9 # v7.0.2
        with:
          name: resultados-pruebas-backend
          path: TestResults/**/*.trx
          if-no-files-found: error
          retention-days: 14
```

Decisiones de este trabajo:

- **.NET:** `setup-dotnet` lee `global.json` (`10.0.100`, `rollForward: latestFeature`). Con `latestFeature` instala el SDK 10.0 más alto disponible; hoy es de la banda 4xx, igual que en local (10.0.402), y la imagen ya trae 10.0.401. No hace falta tocar `global.json`. La 4xx suele ser la última banda de una versión, así que en adelante solo llegarán parches.
- **Caché de NuGet con `actions/cache`, no con `setup-dotnet` (`cache: true`):** el repositorio no tiene `packages.lock.json`, y sin ese archivo `setup-dotnet` falla. La llave sale de `global.json`, `Directory.Packages.props` y los `.csproj`. Pasar a archivos de bloqueo (`RestorePackagesWithLockFile` y `--locked-mode`) mejoraría la cadena de suministro, pero cambia todos los proyectos: queda para después.
- **La caché queda fuera del checkout.** `NUGET_PACKAGES` no se mueve a la carpeta del repositorio. Las pruebas de arquitectura recorren el árbol del checkout buscando `Directory.Build.props` y solo saltan `bin`, `obj`, `node_modules`, `.git`, `.vs`, `TestResults`, `coverage` y `almacen`. Una caché dentro del checkout las haría leer los archivos de MSBuild que traen algunos paquetes y fallar.
- **Orden de los pasos:** restaurar, compilar, formato, vulnerabilidades, imagen y pruebas. El primero que falla detiene el trabajo y deja el check en rojo.
  - La lista de los criterios no fija un orden; este pone primero lo que más falla durante el desarrollo.
  - Los TRX se suben aunque fallen las pruebas: es lo que se mira en el PR en rojo.
  - Si el trabajo se detuvo antes de las pruebas, no se intenta subir nada.
- **Advertencias:**
  - `TreatWarningsAsErrors` (en `Directory.Build.props`) ya convierte en error las advertencias del compilador, de los analizadores y de NuGet, incluidas las de auditoría NU1901 a NU1904 al restaurar.
  - `-warnaserror` agrega las advertencias de MSBuild (MSB*), que esa propiedad no cubre. Cumple al pie de la letra el criterio «falla con cualquier advertencia».
  - El `tester` confirma en local que hoy no aparece ninguna (sección 6).
- **Vulnerabilidades:**
  - Este paso no está en los criterios: lo pidió el coordinador, y el comando ya se usa en el repositorio.
  - Es una segunda barrera junto a la auditoría de NuGet al restaurar.
  - `dotnet list package --vulnerable` termina con código 0 aunque encuentre algo, por eso se lee la salida JSON con `jq`, que viene instalado en el runner.
  - La primera comprobación evita que el paso pase en vacío; si falla, imprime `::error::dotnet list no devolvió proyectos` (la salida de `jq -e` sola no diría nada).
  - La segunda comprueba que la lista `sources` de la salida incluya `https://api.nuget.org/v3/index.json`, para que el paso no pase en silencio con una fuente sin datos de vulnerabilidades. Sin `nuget.config`, la única fuente es nuget.org; la salida real de `dotnet list package --vulnerable --include-transitive --format json --output-version 1` trae `"sources": ["https://api.nuget.org/v3/index.json"]` y la comprobación pasa (2026-10-18). Si algún día se agrega una fuente privada, nuget.org debe seguir en la lista.
  - El filtro `jq` se valida contra la salida real (sección 6, prueba N4).
- **Imagen de SQL Server:**
  - Se descarga en un paso aparte, para separar una falla de red de una falla de pruebas y para medir el tiempo.
  - La etiqueta se lee de la constante `ContenedorSqlServer.Imagen`, así que no hay una segunda copia que se desactualice. Si la constante se mueve, el paso falla y lo avisa.
  - **No se cachea con `actions/cache`.** La descarga comprimida pesa unos 1,5 GB y la imagen extraída ocupa 2,42 GB. Guardar y cargar eso (`docker save` y `docker load`) no es más rápido que bajarla de MCR, que corre sobre Azure, y gastaría buena parte de la cuota de 10 GB de caché del repositorio.
- **Testcontainers en el runner:**
  - `ubuntu-24.04` trae Docker 28 y el usuario del runner tiene acceso al socket. Ryuk (la imagen `testcontainers/ryuk`, desde Docker Hub) funciona sin configuración.
  - No se define ninguna variable `TESTCONTAINERS_*`.
  - La espera de Testcontainers ejecuta `SELECT 1` con `sqlcmd` dentro del contenedor. El código ya tiene topes de 15 minutos para el arranque y 5 para la base compartida.
- **Cobertura:** la especificación no la pide. No se agrega `--collect "XPlat Code Coverage"` aunque `coverlet.collector` esté referenciado.
- **`ASPNETCORE_ENVIRONMENT` no se define en CI.** `Arranque_SinCadenaDeConexionNoArranca` acepta los dos formatos de registro (JSON fuera de Development, texto en Development), y `FabricaNitido` pone su propia cadena.

**Tiempo esperado**, estimado: no hay una medición previa en Linux, porque F0-02a no dejó anotado el tiempo de su corrida. Se mide en la primera corrida.

| Paso | En frío | Con caché |
| --- | --- | --- |
| Obtener el código e instalar .NET | 0,5 a 1 min | 0,5 a 1 min |
| Caché y restauración | 1 a 1,5 min | 0,3 min |
| Compilación Release (14 proyectos) | 1,5 a 2,5 min | 1,5 a 2,5 min |
| Formato | 0,5 a 1 min | 0,5 a 1 min |
| Vulnerabilidades | 0,3 min | 0,3 min |
| Imagen de SQL Server (descarga y extracción) | 1 a 2 min | 1 a 2 min |
| Pruebas: unas 300, con 2 contenedores y migraciones | 2,5 a 4 min | 2,5 a 4 min |
| **Total** | **unos 8 a 12 min** | **unos 7 a 11 min** |

Las cifras corresponden a un runner de repositorio privado (2 vCPU y 8 GB); en uno público (4 vCPU y 16 GB) baja un 30 a 40 %. `ContenedorCompartidoPruebas` levanta un segundo SQL Server mientras el compartido sigue vivo: suma unos 30 s y unos 2 GB de memoria; en un runner de 8 GB (unos 7 GB útiles) cabe, pero con poco margen: ver el riesgo de memoria en la sección 7. La meta de 15 minutos se vigila con cada historia que agregue proyectos. Si se acerca:

1. Primero se separan las pruebas en dos trabajos (unidad y contables por un lado, integración por otro), que corren en paralelo.
2. No se quitan pasos.

#### 2.3 `nitido-frontend`, trabajo `frontend`

```yaml
name: CI

on:
  pull_request:
    branches: [main]
  push:                 # ajuste A2 (aprobado)
    branches: [main]
  workflow_dispatch:

permissions:
  contents: read

concurrency:
  group: ${{ github.workflow }}-${{ github.event.pull_request.number || github.ref }}
  cancel-in-progress: ${{ github.event_name == 'pull_request' }}

defaults:
  run:
    shell: bash

env:
  NG_CLI_ANALYTICS: "false"

jobs:
  frontend:
    name: frontend
    runs-on: ubuntu-24.04          # ajuste A1
    timeout-minutes: 10
    steps:
      - name: Obtener el código
        uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
        with:
          persist-credentials: false

      - name: Instalar Node según .nvmrc
        uses: actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0
        with:
          node-version-file: .nvmrc
          cache: npm
          cache-dependency-path: package-lock.json

      - name: Instalar dependencias
        run: npm ci

      - name: Revisar el código (lint)
        run: npm run lint

      # Hoy no hace nada y termina con código 0; empieza a correr cuando exista
      # el script (F0-05a).
      - name: Revisar los estilos (desde F0-05a)
        run: npm run --if-present lint:estilos

      - name: Probar
        run: npm test

      - name: Compilar
        run: npm run build
```

Decisiones de este trabajo:

- **Node:** `.nvmrc` dice `24`, y la imagen ya trae 24.21.0. La caché de `setup-node` guarda `~/.npm` (no `node_modules`), con la llave sacada de `package-lock.json`.
- **`package-lock.json` sirve en Linux aunque se generó en Windows.** Incluye los binarios `linux-x64` opcionales (esbuild, rolldown, lightningcss, lmdb, sass-embedded y otros). Se comprobó leyendo el archivo; `npm ci` en Linux lo confirma en el ensayo de la sección 6.
- **`npm run --if-present lint:estilos`** cumple «cuando exista (F0-05a)» sin tener que editar el workflow después. Hoy no hace nada y termina con código 0.
- **`npm test`** ya es `ng test --watch=false` (Vitest con jsdom). GitHub define `CI=true`, que también desactiva los avisos interactivos de Angular CLI. `NG_CLI_ANALYTICS=false` lo deja explícito.
- **`npm run build`** usa la configuración `production`. Los presupuestos de `angular.json` hacen fallar el build solo al pasar el umbral de error, y la especificación no pide más.
- No se suben artefactos: la especificación no lo pide.
- **Tiempo esperado:** 2 a 4 minutos.

#### 2.4 Playwright: fuera de esta historia

Se respeta la especificación: Playwright no entra en CI en la fase 0. Queda para la historia del ADR de despliegue o una posterior. Necesitará:

- **Frontend:** `npm ci` y `npm start`, o un servidor estático con el resultado de `ng build` y un proxy de `/api`.
- **API y base:** la API corriendo con una base migrada.
- **Navegador:** `npx playwright install --with-deps chromium`, dentro de `e2e/`.

La API vive en otro repositorio. Opciones para obtenerla, de la preferida a la menos conveniente:

1. **Imagen Docker de la API**, publicada por `nitido-backend` en GitHub Container Registry cuando exista el Dockerfile (historia de despliegue). El trabajo de e2e la levanta como servicio junto a SQL Server, con la etiqueta de la versión del contrato.
2. **Playwright contra staging dentro de `deploy.yml`**, como describe el agente `devops`. Prueba lo que de verdad se despliega.
3. **Clonar `nitido-backend` desde el workflow del frontend.** Exige un token de acceso como secreto en el frontend: un secreto nuevo y un acoplamiento entre los repositorios. No se recomienda.

### 3. De Windows a Linux: lo que puede romper en CI

Se revisaron el código de pruebas y los archivos de configuración. No se edita nada en esta historia.

| Riesgo | ¿Real hoy? | Detalle y mitigación |
| --- | --- | --- |
| `\` en los `ProjectReference` | No | MSBuild en Linux convierte `\` en `/`. Las pruebas de arquitectura normalizan con `LectorDeMsBuild.Normalizar` y comparan siempre con `/`. `ModeloReal.LeerProyecto` convierte `/` al separador del sistema antes de abrir el archivo. |
| Mayúsculas y minúsculas en rutas (`Nitido.slnx`, `ProjectReference`, `Directory.Build.props`, `Directory.Packages.props`, `global.json`, `[**/Migraciones/*.cs]` en `.editorconfig`) | No hoy; **sí a futuro** | Se compararon las 14 rutas de `Nitido.slnx` y los 22 `ProjectReference` con los nombres reales en disco: coinciden letra por letra. El riesgo real aparece al **renombrar solo mayúsculas** en Windows: con `core.ignorecase=true`, git no registra el cambio, y en Linux falla la compilación o la regla `Solucion_ProyectosSiguenConvencionDeNombresYCarpetas`. Mitigaciones: renombrar con `git mv` y correr el ensayo Linux de la sección 6 antes del PR. El propio CI lo detecta. |
| `Assembly.Load(new AssemblyName(n))` en las pruebas de arquitectura | No | Se resuelve con el `deps.json` del proyecto de pruebas, que en Release tiene los mismos ensamblados (`Nitido.Pruebas.Unidad` referencia a `Nitido.Api`, que compone todos los módulos). La comparación de nombres de ensamblado no distingue mayúsculas. `AppContext.BaseDirectory` pasa a ser `bin/Release/net10.0/`, y `BuscarRaiz` sube igual hasta `Nitido.slnx`. |
| Finales de línea | No, si se respeta `.gitattributes` | Los dos repositorios tienen `* text=auto eol=lf`, y `.editorconfig` dice `end_of_line = lf`. `dotnet format --verify-no-changes` revisa los finales de línea, así que un archivo con CRLF fallaría. Con `.gitattributes` desde el primer commit, git guarda LF y saca LF en los dos sistemas. `Arranque_SinCadenaDeConexionNoArranca` parte por `\n` y recorta, así que no depende del sistema. **Condición:** el primer commit incluye `.gitattributes`, que ya existe. |
| Compilación Release frente a Debug | Posible | En local se compila en Debug; CI compila en Release. Puede aparecer una advertencia que solo existe en Release. El recorrido local de la sección 6 usa `-c Release`. |
| Carpeta vacía `tests/contables/casos/` | No | `CasosDorados_CarpetaExiste` la necesita, y git no guarda carpetas vacías, pero ya tiene `.gitkeep`. **Condición:** que `.gitkeep` entre en el primer commit; ningún `.gitignore` lo excluye. |
| Cultura y zona horaria | No | El runner usa la cultura invariante y UTC; el equipo local, es-CO y UTC-5. No hay `DateTime.Now`, `TimeZoneInfo.Local` ni `CurrentCulture` en el backend: las pruebas usan `RelojDePrueba` y `InvariantCulture`. `InvariantGlobalization=false` necesita ICU, y la imagen de Ubuntu lo trae. En el frontend, `formatNumber` usa los datos de locale de Angular, que no dependen del sistema. |
| `Salud_Responde503SinBaseDeDatos` | No | En Linux la conexión al puerto cerrado se rechaza al instante (lo dice F0-02a). Es más rápido que en Windows. |
| Memoria con dos SQL Server a la vez | Bajo | En un runner de 8 GB cabe, con poco margen (ver la sección 7). Si aparece una falla por falta de memoria: `dotnet build-server shutdown` antes de las pruebas, o `ContenedorCompartidoPruebas` en una corrida aparte. |
| Smart App Control (`0x800711C7`) | No aplica | Es una función de Windows 11. El runner es Linux. |

### 4. Secretos y variables

- **Ningún secreto nuevo.** Los workflows no usan `secrets.*`. El `GITHUB_TOKEN` queda con `contents: read`, y `checkout` no lo guarda.
- **`ConnectionStrings__Nitido` no se necesita en CI.**
  - Cada prueba de integración recibe la cadena de `ContenedorSqlServer`, y `FabricaNitido` la pone con `ConfigureAppConfiguration`, que gana a las variables de entorno.
  - `Arranque_LaCadenaDeLaFabricaGanaALasVariablesDeEntorno` define y restaura esa variable dentro de la propia prueba.
  - Las pruebas de arranque sin cadena pasan el valor por argumento.
  - Ningún workflow contiene cadenas de conexión. El `tester` lo verifica buscando con `grep` en los dos `ci.yml` (sección 6).
- **La clave de SQL Server es aleatoria en cada corrida (ajuste A5, opción b, ya aplicada).** `MsSqlBuilder` usa por defecto una constante de la librería (`DefaultPassword`), la misma en todas las corridas, pero `ContenedorSqlServer` la reemplaza con `.WithPassword(GeneradorDeClaveSql.Generar())`, una clave aleatoria que cumple la política de complejidad de SQL Server. Así el criterio de la especificación es cierto y, en local, el puerto publicado mientras corren las pruebas no queda con una clave conocida. En el runner el riesgo ya era nulo, porque el contenedor es efímero y la máquina no recibe conexiones de afuera.
- Las variables `DOTNET_NOLOGO`, `DOTNET_CLI_TELEMETRY_OPTOUT` y `NG_CLI_ANALYTICS` no son secretos.

### 5. Protección de `main` y checks requeridos

| Repositorio | Check requerido (nombre exacto) | Workflow |
| --- | --- | --- |
| `nitido-backend` | `backend` | `CI` (`.github/workflows/ci.yml`) |
| `nitido-frontend` | `frontend` | `CI` (`.github/workflows/ci.yml`) |

Los nombres son estables porque el trabajo no usa matriz y su `name` es fijo. **No se renombran** sin actualizar antes la regla: un check requerido que nunca llega deja los PR bloqueados para siempre.

El usuario configura en GitHub, en cada repositorio, en Settings, Rules, Rulesets, New branch ruleset (o con la «branch protection rule» clásica):

1. **Destino:** la rama por defecto (`main`).
2. **«Require a pull request before merging»**, con **0 aprobaciones requeridas.** Con un solo desarrollador, exigir una aprobación bloquearía todos los PR, porque nadie aprueba su propio PR. La revisión la hace el agente `revisor` antes del PR.
3. **«Require status checks to pass»**, agregando `backend` (o `frontend`). GitHub solo ofrece checks que ya corrieron en los últimos 7 días: **la regla se configura después de la primera corrida del workflow.**
4. **«Block force pushes»** y **«Restrict deletions».**
5. **Opcional:** «Require branches to be up to date before merging». Obliga a actualizar el PR con `main` antes de unir. Con un solo desarrollador cuesta poco y evita romper `main` cuando se unen PR que, por separado, estaban en verde.
6. **Opcional**, en Settings, Actions, General: «Require actions to be pinned to a full-length commit SHA» y permitir solo acciones de GitHub. Hace que GitHub obligue a cumplir lo que ya hacen estos workflows.

**Condición de plan (ajuste A4):** GitHub solo aplica la protección de ramas y los rulesets en repositorios **públicos**, o en **privados con GitHub Pro, Team o Enterprise**. Con repositorios privados en GitHub Free, la regla no se puede aplicar y el criterio no se puede cumplir.

La evidencia sigue siendo una captura de la regla en el PR, porque `gh` no está instalado.

### 6. Cómo se valida sin GitHub

Lo hace el `tester`, sin instalar nada global. Usa Docker Desktop, que ya está.

**6.1 Sintaxis y reglas del workflow.** Se usa actionlint 1.7.12, publicado el 2026-03-30, con su imagen oficial de Docker. Incluye shellcheck, que revisa los scripts de `run:`. Se corre desde PowerShell, en la raíz de cada repositorio:

```powershell
docker run --rm -v "${PWD}:/repo" --workdir /repo rhysd/actionlint:1.7.12 -color .github/workflows/ci.yml
```

Se pasa el archivo porque, sin un directorio `.git`, actionlint no encuentra el proyecto por su cuenta («no project was found in any parent directories»). Cuando exista el repositorio, basta sin argumento.

- **Resultado esperado:** ninguna salida y código 0.
- **Alternativa sin Docker:** descargar `actionlint_1.7.12_windows_amd64.zip` de las versiones publicadas de `rhysd/actionlint` en una carpeta temporal nueva, comprobar su huella contra `actionlint_1.7.12_checksums.txt` y ejecutar `actionlint.exe` sin agregarlo al PATH.
- Este diseño no lo ejecutó: el arquitecto no corre comandos. Se verificó que la imagen y el binario existen y que esa versión es la última publicada.
- actionlint valida el YAML, las expresiones `${{ }}`, los nombres de entradas de cada acción y los scripts. **No valida que un SHA corresponda a una etiqueta.** Eso se comprueba aparte (6.4).

**6.2 Recorrido local en Windows.** Son los mismos comandos del workflow, en orden; el recorrido se detiene en el primero que termine con código distinto de 0. En este equipo hay que poner `dotnet` en el PATH primero (`$env:DOTNET_ROOT="$HOME\.dotnet"; $env:PATH="$HOME\.dotnet;$env:PATH"`).

Backend, en la raíz de `backend/`:

```powershell
dotnet restore Nitido.slnx
dotnet build Nitido.slnx -c Release --no-restore -warnaserror
dotnet format Nitido.slnx --verify-no-changes --no-restore
dotnet list Nitido.slnx package --vulnerable --include-transitive --format json --output-version 1 > $env:TEMP\vulnerables.json
dotnet test Nitido.slnx -c Release --no-build --logger trx --results-directory TestResults
```

- **Paquetes vulnerables:** se revisan con `Get-Content $env:TEMP\vulnerables.json | ConvertFrom-Json`. La lista de proyectos no puede estar vacía y ningún paquete puede tener `vulnerabilities`.
- **Resultados TRX:** se confirma que en `TestResults/` quedaron 3 archivos `.trx`, uno por proyecto de pruebas.

Frontend, en la raíz de `frontend/`:

```powershell
npm ci
npm run lint
npm run --if-present lint:estilos
npm test
npm run build
```

**6.3 Ensayo en Linux, en contenedores.** Reproduce lo que Windows no muestra: las mayúsculas, el `package-lock.json` en Linux y Release en Linux.

- **Copia, no carpeta montada:** el código se **copia** dentro del contenedor, porque una carpeta montada desde Windows no distingue mayúsculas y escondería justo ese riesgo.
- **Experimentos negativos:** se hacen en esa copia, así que el árbol de trabajo nunca se toca.

Backend. La imagen `mcr.microsoft.com/dotnet/sdk:10.0` usa el SDK 10.0 más reciente, compatible con `global.json`. El socket de Docker y `TESTCONTAINERS_HOST_OVERRIDE` permiten que Testcontainers cree los contenedores de SQL Server como hermanos:

```powershell
docker run --rm -v "${PWD}:/origen:ro" -v /var/run/docker.sock:/var/run/docker.sock `
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal -e CI=true `
  mcr.microsoft.com/dotnet/sdk:10.0 bash -c "set -euo pipefail; mkdir /src; cd /origen; tar --exclude=bin --exclude=obj --exclude=TestResults -cf - . | tar -xf - -C /src; cd /src; dotnet restore Nitido.slnx; dotnet build Nitido.slnx -c Release --no-restore -warnaserror; dotnet format Nitido.slnx --verify-no-changes --no-restore; dotnet test Nitido.slnx -c Release --no-build --logger trx --results-directory TestResults"
```

Si Testcontainers no logra conectarse desde dentro del contenedor, el `tester` corre el ensayo con `--filter "FullyQualifiedName!~Nitido.Pruebas.Integracion"` y anota que la integración en Linux queda para la primera corrida en GitHub.

Frontend:

```powershell
docker run --rm -v "${PWD}:/origen:ro" -e CI=true -e NG_CLI_ANALYTICS=false node:24 bash -c "set -euo pipefail; mkdir /app; cd /origen; tar --exclude=node_modules --exclude=e2e --exclude=dist --exclude=.angular -cf - . | tar -xf - -C /app; cd /app; npm ci; npm run lint; npm run --if-present lint:estilos; npm test; npm run build"
```

Se anota el tiempo de cada ensayo como referencia. No equivale al runner, pero muestra una tendencia.

**6.4 Comprobaciones estáticas.**

- Buscar `secrets.`, `ConnectionStrings`, `Password` y `Server=` en los dos `ci.yml`. Resultado esperado: ninguna coincidencia.
- Cada `uses:` tiene un SHA de 40 caracteres y un comentario con la etiqueta.
- Cada SHA coincide con su etiqueta en `https://api.github.com/repos/actions/<nombre>/git/ref/tags/<etiqueta>`, con lo cual se repite la tabla de 2.1.
- No hay `pull_request_target` ni otras acciones que no sean `actions/*`.

**6.5 Pruebas negativas.** Equivalen en local a «el PR queda en rojo» y se hacen en la copia del ensayo Linux:

| N | Cambio temporal | Esperado |
| --- | --- | --- |
| N1 | Una prueba con `Assert.True(false)` | `dotnet test` termina con un código distinto de 0 y el TRX muestra la prueba fallida. |
| N2 | Una variable local sin usar (CS0219) en `Nitido.Compartido` | `dotnet build -c Release --no-restore -warnaserror` termina con un código distinto de 0. |
| N3 | Un espacio sobrante al final de una línea de un `.cs` | `dotnet format --verify-no-changes` termina con un código distinto de 0. |
| N4 | Quitar la línea de `System.Security.Cryptography.Pkcs` de `Directory.Packages.props` (vuelve la 9.0.11, con avisos conocidos) | `dotnet restore` falla por NU1902 o NU1903, y el filtro `jq` del paso de vulnerabilidades lista el paquete. Para probar solo el filtro se corre con `jq` dentro del contenedor (`apt-get install -y jq`). |
| N5 | Un error de lint en `src/app/app.ts` (por ejemplo, una variable sin usar) | `npm run lint` termina con un código distinto de 0. |
| N6 | Un `expect` que falla en `app.spec.ts` | `npm test` termina con un código distinto de 0. |

**6.6 Cada criterio: cómo se verifica hoy y cuándo de verdad.**

| Criterio | Hoy, sin GitHub (resultado real, 2026-10-18) | Con los repositorios en GitHub |
| --- | --- | --- |
| `ci.yml` con `pull_request` hacia `main`, `workflow_dispatch`, `contents: read` y `concurrency`, uno por repositorio | **Verificado.** Lectura de los dos archivos y actionlint 1.7.12 (6.1): sin salida y código 0 en ambos. | La corrida del PR muestra el evento. Dos pushes seguidos al PR cancelan la primera corrida. «Run workflow» aparece cuando el workflow está en `main`. |
| `backend`: .NET de `global.json`, formato, build sin advertencias, pruebas y TRX como artefacto | **Verificado, salvo la subida del artefacto.** Recorrido en Windows (6.2), Release, SDK 10.0.402: restauración 4 s, build 9 s (incremental; 0 advertencias), formato 25 s, vulnerabilidades 19 s (0 paquetes), pruebas 75 s (303 de 303; 3 `.trx`), total 132 s. Ensayo en Linux (6.3), copia en `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04, SDK 10.0.402): restauración 25 s en frío, build 30 s, formato 23 s, vulnerabilidades 22 s, pruebas 62 s (303 de 303, incluida la integración con Testcontainers por el socket de Docker y `host.docker.internal`; 3 `.trx`), total 162 s. Repetido sobre una copia sacada de un repositorio git temporal (`git archive`, con `.gitattributes` y `.gitkeep`): mismo resultado verde. N1 a N4 en rojo y de nuevo en verde (ver abajo). | El registro de la corrida y el artefacto `resultados-pruebas-backend` (`upload-artifact` solo se ejecuta en GitHub) |
| `frontend`: Node de `.nvmrc`, `npm ci`, lint, `lint:estilos` cuando exista, test y build | **Verificado.** Copia limpia sin `node_modules` ni `dist`. En `node:24` (Linux, Node 24.22.0): `npm ci` 25 s, lint 3 s, `lint:estilos` 0 s (no existe, código 0), test 7 s (4 de 4), build 4 s; total 38 s. En Windows (Node 24.21.0): 38 s, 13 s, 5 s, 20 s y 11 s. N5 y N6 en rojo y de nuevo en verde. | El registro de la corrida |
| Caché de NuGet y npm, y menos de 15 minutos | **Solo referencia.** Llave y ruta por lectura. Tiempos de arriba (162 s el backend en Linux, 38 s el frontend): muy por debajo de 15 min, aunque no equivalen al runner (más lento, más la descarga de la imagen de SQL Server). | «Cache restored from key» en la segunda corrida. Duración de la primera corrida (en frío) y de la segunda. |
| Sin secretos ni cadenas de conexión | **Verificado.** `secrets.`, `ConnectionStrings`, `Password`, `Server=` y `pull_request_target`: ninguna coincidencia en los dos `ci.yml`. `ContenedorSqlServer` usa `.WithPassword(GeneradorDeClaveSql.Generar())` (A5). | Settings, Secrets and variables: vacío |
| Acciones fijadas | **Verificado.** Todas las líneas `uses:` son `actions/*@<40 hex> # vX.Y.Z`, y los 5 SHA coinciden con `api.github.com/repos/actions/<nombre>/git/ref/tags/<etiqueta>`. | Igual. Opcional: la política «pinned to full-length SHA» (sección 5). |
| Un PR con una prueba que falla queda en rojo | **Equivalente en local: N1.** `dotnet test` termina con código 1 y el TRX trae la prueba fallida. Al corregir, vuelve a verde. | **Solo con el repositorio:** PR de prueba que no se une |
| Un PR con una advertencia queda en rojo | **Equivalente en local: N2.** `dotnet build -warnaserror` termina con código 1 (CS0219). Al corregir, vuelve a verde. | **Solo con el repositorio** |
| Al corregir, el PR queda en verde y los enlaces van en el PR de la historia | No se puede (equivalente: cada N vuelve a verde). | **Solo con el repositorio** |
| Protección de `main` | No se puede. Pendiente del usuario: A3 y A4 siguen abiertos. | **Solo con el repositorio** y con el plan del ajuste A4. Captura en el PR. |

**Pruebas negativas (6.5), ejecutadas el 2026-10-18 en copias (Linux para N1 a N4 con SDK 10.0.402, sin la integración para ir más rápido; `node:24` para N5 y N6):**

| N | Qué se rompió | Resultado en rojo | Al corregir |
| --- | --- | --- | --- |
| N1 | `Assert.True(false)` en una prueba nueva de `Nitido.Pruebas.Unidad` | `dotnet test`: código 1; `Failed: 1, Passed: 245`; TRX con `outcome="Failed"` y «Assert.True() Failure». El build con `-warnaserror` pasó (el analizador xUnit2020 no avisa). | Se borra la prueba: pipeline verde |
| N2 | Variable local sin usar en `Nitido.Compartido` | `dotnet build -c Release --no-restore -warnaserror`: código 1, `error CS0219`. También falla sin `-warnaserror` (lo cubre `TreatWarningsAsErrors`). N2b: un `<Warning>` de MSBuild (código propio) termina con 0 sin `-warnaserror` y con 1 con él. | Se borra el archivo: verde |
| N3 | Un espacio al final de la línea 1 de `MarcadorDeEnsamblado.cs` | `dotnet format --verify-no-changes`: código 2, `error WHITESPACE: Fix whitespace formatting` | Se restaura el archivo: verde |
| N4 | Se quita la línea de `System.Security.Cryptography.Pkcs` de `Directory.Packages.props` | `dotnet restore`: código 1, «Failed to restore» por NU1902 y NU1903 (9.0.11). Con la restauración tolerante (`-p:TreatWarningsAsErrors=false`), el paso de vulnerabilidades tal como está escrito también falla (`dotnet list package` dice «Restore failed», código 1). Con `--no-restore` solo para aislar el filtro, `jq` imprime `System.Security.Cryptography.Pkcs 9.0.11` y el paso termina con 1. | Se restaura la línea: verde |
| N5 | Variable sin usar en `src/app/app.ts` | `npm run lint`: código 1, `@typescript-eslint/no-unused-vars` | Se restaura el archivo: verde |
| N6 | `expect(...).toBe("otro texto")` en `app.spec.ts` | `npm test`: código 1, `1 failed, 3 passed` | Se restaura el archivo: verde |

**No verificable hoy:** la subida del artefacto TRX, la caché (`actions/cache`, `setup-node`), la concurrencia, el tiempo real del runner de 2 vCPU, la descarga de la imagen de SQL Server en frío desde el runner, los PR en rojo y en verde y la protección de `main`.

### 7. Riesgos

| Riesgo | Mitigación |
| --- | --- |
| **El trabajo `backend` pasa de 15 minutos** con más proyectos por módulo (ADR 0001) | Se mide en la primera corrida y en cada historia que agregue proyectos. Si se acerca al límite, se parten las pruebas en dos trabajos paralelos, y el check requerido pasa a ser un trabajo final que depende de los dos. El tope duro del trabajo es de 20 minutos. |
| **El contenedor de SQL Server falla de vez en cuando** (descarga, arranque lento) | La descarga va en su propio paso, con 3 intentos y un tope de 8 minutos. La etiqueta de la imagen es fija (no `2025-latest`). El código ya trae topes de 15 y 5 minutos. Si una falla se repite, se reintenta la corrida, y si pasa más de una vez por semana, se investiga: no se agregan reintentos a las pruebas. |
| **`ubuntu-latest` cambia de imagen a mitad de la fase 0.** GitHub lo mueve de Ubuntu 24.04 a 26.04 de forma gradual entre el 19 de octubre y el 19 de noviembre de 2026; en esas semanas dos corridas seguidas pueden caer en imágenes distintas. | Ajuste A1: `ubuntu-24.04` fijo. El paso a 26.04 se hace a propósito, en un PR propio, después de probarlo. |
| **Minutos de GitHub Actions** | Repositorio público: gratis y sin límite. Privado en GitHub Free: 2.000 minutos al mes para la cuenta. Con unas 2 corridas por PR (unos 10 min el backend y unos 3 el frontend), más la corrida de `main` del ajuste A2, salen unos 25 a 35 minutos por historia: la fase 0 queda muy por debajo. Desde 2026 los runners alojados bajaron de precio. Las corridas canceladas por `concurrency` también ahorran minutos. |
| **Caché envenenada** | Solo hay `pull_request` (nunca `pull_request_target`). La caché de un PR solo la usa ese mismo PR. La de `main` solo la escriben corridas de `main`. `actions/cache` no guarda nada si el trabajo falla. La llave depende de los archivos de dependencias. Límites de la defensa: NuGet verifica la firma de un paquete cuando extrae el `.nupkg` a `~/.nuget/packages`; lo que ya está en esa carpeta global (restaurado desde `actions/cache`) se usa tal cual, y sin `packages.lock.json` no hay comparación de hash. npm sí verifica la integridad de cada paquete contra `package-lock.json`. La mitigación real que queda pendiente son los archivos de bloqueo de NuGet (`RestorePackagesWithLockFile` y `--locked-mode`), ya mencionados en la sección 2.2. |
| **Acciones de terceros** | Solo se usan acciones oficiales (`actions/*`), fijadas por SHA. No se usa ninguna otra. actionlint se usa solo en local, como imagen de Docker y con versión fija, y no entra en el workflow. |
| **SHA desactualizados sin Dependabot**, o acciones que GitHub retira cuando cambia la versión de Node | Revisión trimestral a cargo de `devops`. Dependabot y CodeQL quedan fuera de alcance, como dice la especificación. |
| **Límite de descargas anónimas de Docker Hub** para la imagen de Ryuk (`testcontainers/ryuk`) | Riesgo bajo: es una imagen pequeña, una vez por corrida. SQL Server viene de MCR, que no tiene ese límite. Si pasa, se reintenta la corrida; si se repite, se define `TESTCONTAINERS_RYUK_DISABLED=true` en el paso de pruebas (el runner es efímero y se destruye completo al terminar, así que Ryuk no limpia nada que importe en CI). |
| **Memoria del runner.** Un runner privado trae unos 7 GB útiles de los 8 GB; dos SQL Server a la vez (el compartido y el de `ContenedorCompartidoPruebas`), más el compilador y las pruebas, pueden quedarse cortos. (Riesgo hallado por el `revisor` al revisar la primera corrida prevista.) | Si aparece un reinicio del contenedor o un error de memoria, se limita cada SQL Server con `MSSQL_MEMORY_LIMIT_MB=2048` (variable de entorno de la imagen, puesta con `.WithEnvironment(...)` en `ContenedorSqlServer`; es un cambio en código de pruebas, a cargo del `tester`). Antes se prueba `dotnet build-server shutdown`. |
| **Espacio y tiempo de la imagen de SQL Server.** La descarga comprimida es de unos 1,5 GB, pero extraída ocupa 2,42 GB. | Se vigila el espacio en disco del runner en la primera corrida (`df -h`, si hiciera falta). El paso propio de descarga mide el tiempo y tiene tope de 8 minutos. |
| **Falla temporal de nuget.org (NU1900).** Si la API de vulnerabilidades no responde durante la restauración, NuGet emite NU1900, y con `TreatWarningsAsErrors` la restauración falla aunque no haya nada vulnerable. | Se reintenta la corrida. Si se repite, se investiga antes de bajar la exigencia: no se silencia NU1900. Ver también el paso de vulnerabilidades, que ahora exige que la fuente sea nuget.org. |
| **La primera corrida real puede mostrar cifras o fallas distintas** a las estimadas (no hay medición previa en Linux ni en el runner). | El usuario lo detecta mirando el registro de la primera corrida en GitHub (pestaña Actions) y la duración de cada paso (se ve en el resumen del trabajo). Si el total pasa de 15 minutos o algún paso es mucho más lento que la tabla de la sección 2.2, se aplican las mitigaciones de estas filas y se anota el tiempo real en esta especificación. |
| **Advertencias nuevas por un SDK más nuevo** (`latestFeature` toma el SDK más alto) | La banda 4xx ya es la de local, y los parches casi nunca agregan analizadores. Si un parche rompe CI, se fija la versión en `global.json` en un PR propio. |
| **Release solo en CI** (advertencias que no salen en Debug) | El recorrido local usa `-c Release`. |
| **Seguridad del registro** | Ningún secreto. La cadena del contenedor solo vive en memoria de las pruebas. `Arranque_SinCadenaDeConexionNoArranca` comprueba que no se registra `Server=`. |
| **Multiempresa y datos existentes** | No aplica: no hay datos ni consultas nuevas. |

### 8. Plan de implementación

Participan `devops`, `tester` y `revisor`. **`dba`, `dev-backend` y `dev-frontend` no participan**, porque no hay código de producción. Tampoco el `contador`.

| Paso | Agente | Qué | Repositorio | En paralelo con | Tiempo |
| --- | --- | --- | --- | --- | --- |
| 0 | usuario | Decide los ajustes A1 a A7 | — | — | — |
| 1 | `devops` | `.github/workflows/ci.yml` según 2.2, más la sección «Integración continua» del README y la corrección de `ConnectionStrings__Nitido`. **Hecho (2026-10-18):** workflows de los dos repositorios y sus README escritos; actionlint 1.7.12 sin avisos, SHA verificados contra la API de GitHub, pasos del frontend y comandos del backend probados en local. | `backend/` | 2 | 0,25 día |
| 2 | `devops` | `.github/workflows/ci.yml` según 2.3, más la sección del README. **Hecho (2026-10-18)**, junto con el paso 1. | `frontend/` | 1 | 0,1 día |
| 3 | `devops` | actionlint en los dos repositorios y comprobación de los SHA (6.1 y 6.4). Corrige lo que salga. **Hecho (2026-10-18)** junto con el paso 1. Nota: actionlint exige un directorio `.git` para encontrar el proyecto; mientras no haya repositorio, se le pasa el archivo: `actionlint .github/workflows/ci.yml`. | ambos | — | 0,1 día |
| 4 | `tester` | Recorrido local (6.2), ensayo Linux (6.3) y pruebas negativas N1 a N6 (6.5), todo en copias. Anota los tiempos y llena la columna «Hoy» de 6.6. Si algo falla por código de producción, lo reporta; no lo corrige. **Hecho (2026-10-18):** todo verde, sin fallos de producción; resultados en la tabla 6.6. | ambos | — | 0,4 día |
| 5 | `revisor` | Revisión: permisos mínimos, SHA y etiquetas, ausencia de secretos, nombres estables de los checks, que el TRX se suba aunque fallen las pruebas, que nada pase en vacío (el paso de vulnerabilidades, `--if-present`) y la coherencia con esta especificación. **Hecho (2026-10-18):** sin hallazgos graves ni medios. Los bajos, resueltos por `devops` el mismo día: (1) `|| true` en la lectura de la imagen y mensaje `::error::` en la comprobación de `jq`, con pruebas en Ubuntu 24.04 (jq 1.7); (2) la sección 4 y el YAML de referencia de 2.2 y 2.3 sincronizados con los `ci.yml` reales; (4) precisión sobre `concurrency` en el criterio, 2.1 y los README; (5) límites de la defensa contra la caché envenenada; (6) comprobación de que la fuente de vulnerabilidades sea nuget.org; (9) fechas, SDK de `setup-dotnet`, tamaño de la imagen y riesgos de la primera corrida (sección 7). Aplazado: archivos de bloqueo de NuGet (`--locked-mode`), que cambian todos los proyectos. | ambos | — | 0,15 día |
| — | — | **Aquí termina la etapa 1.** Estado de la historia: «Implementada y verificada en local; pendiente de verificación en GitHub». Según el ajuste A3, F0-02b puede empezar. | | | **≈ 1 día** |
| 6 | usuario, con `devops` | Cuando existan los repositorios: primer commit de `main` **sin** `.github/` (con `.gitattributes` y `tests/contables/casos/.gitkeep`). Luego la rama `feat/ci-integracion-continua` con los workflows y su PR. **Primero el backend y después el frontend**, igual que en cualquier cambio que cruza los dos repositorios. | ambos | — | 0,2 día |
| 7 | `tester` | Por repositorio: un PR con una prueba que falla y otro con una advertencia (en rojo), y la corrección (en verde). Son ramas de prueba que se cierran sin unir. Mide los tiempos en frío y con caché, y deja los enlaces en el PR de la historia. | ambos | — | 0,2 día |
| 8 | usuario | Configura la protección de `main` (sección 5) después de la primera corrida y agrega la captura al PR | ambos | — | 0,1 día |
| 9 | `revisor` | Revisión final de los PR con la evidencia. Se unen el del backend primero y luego el del frontend. | ambos | — | 0,1 día |

**Total:** unos 1,5 días; 1 día ahora y medio día cuando exista GitHub. La especificación estimaba 1 día y no contaba la verificación en dos etapas.

### Ajustes propuestos a la especificación (pendientes del usuario)

No cambian el alcance de fondo. **Estado (2026-10-18):** A1, A2, A5 (opción b), A6 y A7 están decididos y aplicados en los criterios. A3 y A4 siguen pendientes del usuario.

- **A1. Runner `ubuntu-24.04` en lugar de `ubuntu-latest`.**
  - Entre el 19 de octubre y el 19 de noviembre de 2026, GitHub mueve `ubuntu-latest` a Ubuntu 26.04 de forma gradual, y él mismo recomienda fijar `ubuntu-24.04` a quien no esté listo.
  - Con la etiqueta fija, una corrida roja no se debe a un cambio de imagen.
  - Se cambia el texto del criterio a «el trabajo `backend` (Ubuntu 24.04, fijo) …».
  - Si el usuario prefiere `ubuntu-latest`, se usa y se acepta ese riesgo durante un mes.
- **A2. Agregar `push` a `main` a los disparadores.**
  - GitHub limita la caché que crea un PR a ese PR. Un PR nuevo solo puede usar la de `main`, y `main` nunca corre el workflow si solo hay `pull_request` y `workflow_dispatch`. En la práctica, el criterio «hay caché» solo serviría dentro de un mismo PR.
  - Correr también al unir llena la caché para el siguiente PR y comprueba `main` después de cada unión.
  - Cuesta una corrida por unión, y con la protección de `main` no hay pushes directos.
  - La alternativa sin cambiar el criterio es lanzar `workflow_dispatch` en `main` a mano, pero GitHub borra la caché que no se usa en 7 días.
- **A3. Cerrar la historia en dos etapas, sin bloquear la ruta crítica.**
  - Los criterios «un PR con … queda en rojo», «al corregir … en verde con enlaces en el PR» y «protección de `main`» no se pueden verificar sin repositorio.
  - **Etapa 1 (ahora):** todos los demás, más sus equivalentes en local (N1 a N6).
  - **Etapa 2 (con GitHub):** los PR de prueba, los enlaces y la captura.
  - **F0-02b puede empezar al terminar la etapa 1.** Mientras no haya GitHub, el `tester` corre el recorrido local de 6.2 en Release en cada historia, como sustituto de la integración continua.
  - Hay que reflejarlo en el índice de la fase 0, que hoy pone F0-06 en la ruta crítica antes de F0-02b. Este diseño no lo edita.
- **A4. Plan de GitHub para proteger `main`.** Con repositorios privados, la protección de ramas exige GitHub Pro (cuenta personal) o Team (organización). En GitHub Free solo funciona en repositorios públicos. El usuario decide entre:
  - repositorios privados con Pro, unos US$4 al mes por usuario según la página de precios de GitHub;
  - repositorios públicos;
  - quitar el criterio de protección. No se recomienda: es lo que garantiza que «nada roto llega a `main`».
- **A5. El criterio sobre la clave de SQL Server era incorrecto (resuelto con la opción b, ya aplicada en `ContenedorSqlServer`).** Dice «la clave de SQL Server la genera Testcontainers en cada corrida», pero `Testcontainers.MsSql` 4.16 usa siempre la constante `DefaultPassword`. Dos opciones:
  - **(a) Corregir el texto:** «la clave la pone Testcontainers en un contenedor efímero; no es un secreto del repositorio».
  - **(b) Que el `tester` agregue `.WithPassword(...)` en `ContenedorSqlServer`**, con una clave aleatoria por corrida que cumpla la política de complejidad de SQL Server. Es una línea en el código de pruebas y hace cierto el criterio. Además evita que, en local, quede publicado un puerto con una clave conocida mientras corren las pruebas.
  - **Recomendación:** la (b).
- **A6. Paso de vulnerabilidades.**
  - El coordinador lo pidió en el diseño, pero no está en los criterios. Se incluye porque cuesta unos 20 segundos y deja un informe visible.
  - Si el usuario prefiere ceñirse a la especificación, se quita: la auditoría de NuGet al restaurar ya hace fallar el trabajo por `TreatWarningsAsErrors`.
  - Si se queda, se agrega un criterio: «el trabajo falla si hay paquetes con vulnerabilidades conocidas, directos o transitivos».
- **A7. Precisión en los criterios del backend.** Cambiar «`dotnet format --verify-no-changes`» por «`dotnet format Nitido.slnx --verify-no-changes`» y «`dotnet build -c Release`» por «`dotnet build -c Release -warnaserror`». La primera es la forma que ya usa el README. La segunda cubre también las advertencias de MSBuild que `TreatWarningsAsErrors` no alcanza.

**Fuera de este archivo, para el coordinador:** `f0-02a-conexion-y-tablas.md` (sección de configuración) dice que en CI la cadena va en `ConnectionStrings__Nitido`. No es así: ver la sección 4. Este diseño no edita otras especificaciones.

**Fuentes consultadas el 2026-10-18:**

- Páginas de versiones y API de etiquetas de `actions/checkout`, `actions/setup-dotnet`, `actions/setup-node`, `actions/cache` y `actions/upload-artifact`.
- README de `actions/setup-dotnet` (caché que exige `packages.lock.json`; `rollForward`).
- Versiones publicadas y documentación de uso de `rhysd/actionlint`.
- `actions/runner-images`, `Ubuntu2404-Readme.md` (imagen 20261012).
- [Anuncio de Ubuntu 26.04 y migración de `ubuntu-latest`](https://github.blog/changelog/2026-09-17-ubuntu-26-generally-available-and-latest-migration).
- [Caché de dependencias: alcance por rama y desalojo a los 7 días](https://docs.github.com/en/actions/reference/workflows-and-actions/dependency-caching).
- [Runners alojados por GitHub: hardware de repositorios públicos y privados](https://docs.github.com/en/actions/reference/runners/github-hosted-runners).
- [Protección de ramas según el plan](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-protected-branches).
- [Cambio de precios de Actions en 2026](https://github.blog/changelog/2025-12-16-coming-soon-simpler-pricing-and-a-better-experience-for-github-actions/).
- `MsSqlBuilder.cs` de `testcontainers-dotnet` (`DefaultPassword`).
- [Comando `dotnet package list`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-package-list).

## Fuera de alcance

- **Despliegue de cualquier tipo:** `deploy.yml`, imágenes Docker, staging y producción. El proveedor de nube no está decidido y la fase 0 no tiene despliegue.
- Playwright en CI. Necesita levantar API, base y frontend en el runner; queda para una historia posterior a la fase 0.
- CodeQL y Dependabot. El plan los pide en «Calidad y pruebas»; quedan para una historia posterior a la fase 0.
- Migraciones sobre base limpia como paso aparte. En la fase 0 las cubre la prueba `Migraciones_SeAplicanSobreBaseLimpia` (F0-02a).

## Resultado de la etapa 2 (2026-10-25)

Hecha con los repositorios públicos [nitido-backend](https://github.com/Parzivalfair12/nitido-backend) y [nitido-frontend](https://github.com/Parzivalfair12/nitido-frontend).

- **Primer push:** los dos CI quedaron en verde sobre `main` (jobs `backend` y `frontend`).
- **Protección de `main`:** regla «Proteger main» en los dos repositorios, sin excepciones (ni para el dueño): todo entra por pull request, el chequeo `backend` (o `frontend`) debe estar en verde y actualizado respecto de `main`, las conversaciones deben quedar resueltas, y no se permite empujar a la fuerza ni borrar `main`. Al ser repositorios públicos no hace falta un plan de pago (resuelve la pregunta A4).
- **Pull request en rojo:** [backend #9](https://github.com/Parzivalfair12/nitido-backend/pull/9) y [frontend #6](https://github.com/Parzivalfair12/nitido-frontend/pull/6) llevaban una prueba que fallaba a propósito. El CI salió en rojo ([ejecución del backend](https://github.com/Parzivalfair12/nitido-backend/actions/runs/40240543347), [ejecución del frontend](https://github.com/Parzivalfair12/nitido-frontend/actions/runs/40240552508)) y GitHub marcó el pull request como **bloqueado** («BLOCKED»).
- **Pull request en verde:** el segundo commit de cada pull request quita la prueba rota; el CI vuelve a verde y el pull request se une con *squash*, para que el commit roto no quede en la historia de `main`.
- **Pendientes que quedan fuera de la fase 0 (como decía el diseño):** Playwright en el CI, CodeQL y Dependabot.
