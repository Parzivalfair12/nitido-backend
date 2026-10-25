# nítido: plan de desarrollo

17 de octubre de 2026 · Andrés

Copia en Markdown del documento «nítido: plan de desarrollo». Los dos dibujos del documento (arquitectura y fases) están aquí como texto.

## Resumen

nítido se construye como un monolito modular en .NET con Angular y SQL Server, en 6 fases y unas 15 semanas hasta el piloto con contadores reales. El MVP llega al final de la fase 4: subir los XML de la DIAN, aprobar asientos propuestos con retenciones, ver los libros y exportar a Siigo.

El trabajo lo hace un desarrollador apoyado por 8 agentes de Claude Code con roles fijos: arquitecto, backend, frontend, base de datos, tester, revisor, contador y DevOps. Cada agente usa 7 skills del proyecto que guardan las reglas del proyecto: convenciones de base de datos, lector DIAN, motor de asientos, normativa, diseño Cristal, flujo de funcionalidad y pruebas contables.

La primera meta es la fase 0: en 2 semanas, ingresar con usuario y contraseña, crear una firma y un cliente, con el estilo Cristal.

## Stack tecnológico

Se mantiene Angular, .NET y SQL Server, en sus versiones con soporte vigente. .NET 10 es la versión de soporte largo hasta noviembre de 2028; .NET 8 y 9 dejan de tener soporte en noviembre de 2026. Angular 22 es la línea estable actual.

| Capa | Elección | Para qué |
| --- | --- | --- |
| Frontend | Angular 22, componentes standalone y signals | Pantallas del mockup; estado local con signals, sin librerías de estado extra |
| Diseño | Sistema propio Cristal en SCSS con tokens CSS, Angular CDK | Mismo aspecto del mockup; CDK para tablas, diálogos y accesibilidad |
| Backend | ASP.NET Core sobre .NET 10, API REST con OpenAPI | Un solo servicio con módulos; contrato generado para el cliente Angular |
| Acceso a datos | Entity Framework Core 10 con convenciones T_ y UUID_ | Migraciones versionadas; consultas pesadas de reportes con SQL a mano |
| Base de datos | SQL Server; en local, un contenedor creado desde la extensión MSSQL de VS Code | Tablas temporales para auditoría; seguridad por fila para separar clientes |
| Identificadores | GUID versión 7, ordenados en el tiempo | UUID sin fragmentar los índices |
| Trabajos en segundo plano | Hangfire sobre SQL Server | Leer ZIP grandes, generar PDF y exportaciones sin bloquear la pantalla |
| Archivos | Almacenamiento de objetos con bloqueo inmutable (Azure Blob, Amazon S3 o MinIO) detrás de una interfaz propia | XML originales guardados 10 años sin posibilidad de cambio |
| Validación y logs | FluentValidation, Serilog, OpenTelemetry | Errores claros en español y trazas de cada operación |
| Autenticación | ASP.NET Core Identity con cookie segura y doble factor | Sin tokens en el navegador; roles por firma y por cliente |
| Reportes | QuestPDF para PDF, ClosedXML para Excel | Libros oficiales y archivo para Siigo |
| Pruebas | xUnit, Testcontainers con SQL Server, Playwright | Unidad, integración con base real y flujos de punta a punta |
| Repositorio y CI | GitHub y GitHub Actions | Pruebas en cada cambio y despliegue automático a staging |
| Despliegue | Imagen Docker que corre en cualquier proveedor; Azure es la primera opción, sin decidir | El código no usa servicios exclusivos de una nube; cambiar de proveedor es cambiar configuración |

Para la librería de pruebas del frontend y la versión exacta de SQL Server se toma lo que traiga por defecto la plantilla oficial al crear el proyecto.

## Arquitectura

nítido es un solo servicio dividido en módulos con fronteras claras, no microservicios. Para una persona con agentes es lo más rápido de construir y desplegar, y cualquier módulo puede separarse después si crece.

```
Angular 22 en el navegador (pantallas del mockup, estilo Cristal)
        │  API REST con OpenAPI, cookie segura
        ▼
ASP.NET Core sobre .NET 10, monolito modular
  ┌──────────────────┬──────────────────┬──────────────┬─────────────────┐
  │ Firmas y usuarios│ Clientes y perfil│ Terceros     │ Plan de cuentas │
  │ Documentos DIAN* │ Reglas*          │ Contabilidad*│ Impuestos*      │
  │ Reportes y libros*│ Exógena         │ Bancos       │ Nómina          │
  │ Integraciones*   │ Conservación*    │ Compartido: multiempresa, eventos, auditoría │
  └──────────────────┴──────────────────┴──────────────┴─────────────────┘
        │                │                    │                    │
   SQL Server      Almacén inmutable    Proveedor DIAN (externo)   Siigo (externo)
   datos y Hangfire  XML originales,    eventos RADIAN y nómina    exportación de asientos
                     10 años
(* núcleo del MVP)
```

Cada módulo tiene sus capas de dominio, aplicación, infraestructura y API, y solo habla con otro por su interfaz pública o por eventos internos. Por ejemplo, Documentos DIAN publica «documento leído», Reglas propone el asiento y Contabilidad lo registra al aprobarse. Cómo se organiza el código de cada módulo (Clean Architecture, un proyecto por capa, reglas de dependencias verificadas por pruebas) está en [`arquitectura-backend.md`](arquitectura-backend.md) y en el [ADR 0001](adr/0001-clean-architecture.md).

- **Multiempresa:** cada fila de un cliente lleva `UUID_EMPRESAS`; EF Core filtra por cliente y SQL Server lo vuelve a filtrar con seguridad por fila.
- **DIAN:** nítido solo lee XML, así que no necesita habilitación. Lo que se envía a la DIAN (eventos RADIAN, nómina electrónica) pasa por un proveedor tecnológico aliado o por el esquema de software adquirido.
- **Trabajos largos:** leer un ZIP, generar un PDF o exportar a Siigo corre en segundo plano y avisa al terminar.

## Repositorio y convenciones

Decisión del 18 de octubre de 2026: dos repositorios (backend y frontend).

El proyecto vive en dos repositorios independientes. El del backend (`nitido-backend`) guarda el plan y las especificaciones, la base de datos y las pruebas de .NET. El del frontend (`nitido-frontend`) guarda la aplicación Angular, las pruebas de punta a punta y el mockup. El contrato entre los dos es OpenAPI: el backend publica `/openapi/v1.json` y el frontend genera su cliente a partir de ese documento. Un cambio que toca la API y una pantalla son dos pull requests coordinados, con el del backend primero.

Repositorio `nitido-backend`:

```
nitido-backend/
  CLAUDE.md                  instrucciones del repositorio para Claude Code
  Nitido.Api/                host de ASP.NET Core
  Nitido.Compartido/         tipos comunes, eventos, multiempresa
  Modulos/<Modulo>/          Dominio, Aplicacion, Infraestructura, Api
  db/                        PUC base, datos semilla, scripts de seguridad por fila
  tests/                     unidad, integracion, contables (casos dorados)
  docs/adr/                  decisiones de arquitectura
  docs/specs/                una especificación por funcionalidad
  .github/workflows/         integración y despliegue del backend
```

Repositorio `nitido-frontend`:

```
nitido-frontend/
  src/                       aplicación Angular
  public/                    archivos estáticos (marca y logo)
  e2e/                       Playwright, los flujos del mockup
  docs/                      mockup aprobado (mockup-nitido.dc.html) y logo (logo-nitido.svg)
  .nvmrc                     versión de Node
  .github/workflows/         integración del frontend
```

Agentes y skills comunes en `.claude/` de la carpeta raíz (decisión del 18 de octubre de 2026): `.claude/agents/` con los 8 agentes y `.claude/skills/` con las 7 skills, fuera de los dos repositorios y sin versionar con ellos.

- **Base de datos:** tablas `T_<NOMBRE>` en plural y mayúsculas, llave primaria `UUID_<TABLA>`, llave foránea con el mismo nombre de la llave que referencia, columnas en mayúsculas con guion bajo. Todas las tablas de datos de un cliente llevan `UUID_EMPRESAS`.
- **Dinero:** `DECIMAL(19,2)` en pesos, nunca `float`. En C# siempre `decimal`.
- **C#:** nombres en español para el dominio contable (`Asiento`, `Tercero`, `Comprobante`) y también en español para lo técnico propio (`IRepositorioClientes`, `CrearCliente`); los tipos del marco conservan su nombre (`DbContext`). EF Core traduce de PascalCase a las convenciones de la base.
- **Angular:** un módulo de rutas por área del menú (`pendientes`, `contabilidad`, `bancos`, `nomina`, `reportes`, `ajustes`, `firma`), componentes standalone y textos en español.
- **Ramas:** `main` siempre desplegable; una rama corta por funcionalidad, `feat/<modulo>-<tema>`.
- **Commits:** formato Conventional Commits en español, por ejemplo `feat(documentos): leer notas débito`.
- **Cambios:** todo entra por pull request con integración continua en verde y la revisión del agente revisor.

## Fases y entregables

Son 6 fases; cada una termina con algo que se puede usar y un criterio de salida que tú verificas. Las semanas son una estimación para un desarrollador que ya conoce .NET y Angular.

| Fase | Semanas | Alcance | Criterio de salida |
| --- | --- | --- | --- |
| 0 · Fundaciones | 1 a 2 | Repositorio, agentes y skills, CI, entorno local, ingreso con doble factor, firmas, clientes y permisos, sistema de diseño Cristal | Ingresar, crear una firma y un cliente |
| 1 · Núcleo contable | 3 a 5 | PUC base editable, terceros con dígito de verificación, comprobantes con consecutivo, asientos manuales, periodos y cierre, libro diario y balance de prueba | Un asiento manual cuadrado aparece en el balance |
| 2 · Lectura DIAN | 6 a 8 | Subir ZIP y XML; leer factura, nota crédito y débito, documento soporte y POS; duplicados por CUFE; huella SHA-256; guardado inmutable; terceros automáticos | Los XML reales de prueba se leen sin errores |
| 3 · Propuesta y aprobación | 9 a 11 | Perfil tributario, parámetros con vigencia, retenciones, reglas que aprenden, Pendientes, aprobar en lote, deshacer, elaboró y aprobó, alerta de acuse de recibo | Los casos dorados de Torremuebles y El Fogón pasan al peso |
| 4 · Libros y exportación (MVP) | 12 a 13 | Libro mayor, auxiliares, inventarios y balances, estados financieros de Grupo 2 y 3, PDF, Excel y archivo para Siigo | Un mes completo de un cliente se cierra y se exporta a Siigo |
| 5 · Piloto | 14 a 15 | Tres firmas reales durante dos semanas, ajustes de uso, rendimiento, revisión legal | Las tres firmas cierran un mes en nítido |
| Después | desde 16 | Exógena, conciliación bancaria, eventos RADIAN y nómina electrónica con proveedor aliado | Se prioriza con lo que pidan las firmas del piloto |

## Ciclo de trabajo

Cada funcionalidad sigue el mismo camino, de una historia en GitHub a producción. Lo arranca la skill `nueva-funcionalidad` con el número de la historia.

1. **Historia.** Se escribe en GitHub qué necesita el contador y cómo se sabrá que funciona, en lenguaje de negocio.
2. **Especificación.** El agente `contador` agrega las reglas contables y tributarias, con norma y ejemplo numérico. Queda en `docs/specs/`.
3. **Diseño técnico.** El `arquitecto` define endpoints, tablas, eventos y qué módulo cambia. Si la decisión es grande, escribe un ADR.
4. **Pruebas primero.** El `tester` escribe las pruebas que deben pasar, incluidos los casos contables con el valor esperado al peso.
5. **Implementación.** El `dba` hace la migración, `dev-backend` el dominio y la API, y `dev-frontend` la pantalla.
6. **Verificación.** El `tester` corre toda la batería y prueba la pantalla con Playwright. Si algo falla, vuelve al paso 5.
7. **Revisión.** El `revisor` busca fallas de seguridad, fugas entre clientes y código fuera de convención. El `contador` revisa que los asientos sean correctos.
8. **Pull request.** Se abre con el resumen, las pruebas y capturas. GitHub Actions corre todo de nuevo.
9. **Staging.** Al unirse a `main`, `devops` despliega a staging (cuando exista). Se prueba como contador.
10. **Producción.** Con aprobación en GitHub se despliega a producción. La historia se cierra.

## Agentes y skills

| Agente | Cuándo entra | Puede | Entrega |
| --- | --- | --- | --- |
| `arquitecto` | Antes de construir algo nuevo o cuando una decisión toca varios módulos | Leer todo, escribir en `docs/` | Diseño técnico y ADR |
| `contador` | Al especificar y al revisar algo contable o tributario | Leer, buscar en la web, escribir en `docs/specs/` | Reglas con norma, fuente y ejemplo numérico |
| `dba` | Cuando cambia el modelo de datos | Editar migraciones y `db/`, correr comandos | Migración, índices y seguridad por fila |
| `dev-backend` | Para dominio, API, trabajos y lectura de XML | Editar el repositorio del backend (`Nitido.Api/`, `Nitido.Compartido/`, `Modulos/`), correr dotnet | Código .NET con sus pruebas de unidad |
| `dev-frontend` | Para pantallas y componentes | Editar el repositorio del frontend (`nitido-frontend`), correr npm | Pantalla Angular igual al mockup |
| `tester` | Antes y después de implementar | Editar `tests/` del backend y `e2e/` del repositorio del frontend, correr pruebas; nunca código de producción | Pruebas y reporte de fallas |
| `revisor` | Antes de cada pull request | Solo leer y correr análisis | Hallazgos ordenados por gravedad |
| `devops` | Para CI, infraestructura y despliegues | Editar `.github/` e infraestructura | Pipeline en verde y entornos listos |

Skills: `nueva-funcionalidad`, `convenciones-bd`, `lector-dian`, `motor-asientos`, `normativa-colombia`, `diseno-cristal`, `pruebas-contables`.

Agentes y skills comunes en `.claude/` de la carpeta raíz (decisión del 18 de octubre de 2026).

Regla de oro: ningún agente aprueba su propio trabajo. Quien implementa no revisa, y el `tester` no corrige código de producción.

## Calidad y pruebas

| Tipo | Qué cubre | Herramienta | Meta |
| --- | --- | --- | --- |
| Unidad | Motor de asientos, retenciones, dígito de verificación, vigencias, lectura de XML | xUnit; Vitest o Jest | 90 % de cobertura en dominio |
| Integración | API contra SQL Server real, seguridad por fila, migraciones | Testcontainers | Toda ruta de la API probada |
| Contables doradas | Un lote de documentos produce exactamente el asiento y el balance esperados | xUnit con casos en `tests/contables/` | 100 % deben pasar |
| XML reales | Facturas, notas, documento soporte y POS anonimizados | xUnit | Un caso por tipo y versión de anexo |
| Punta a punta | Ingresar, subir, aprobar, deshacer, ver balance, exportar | Playwright | Los flujos del mockup |
| Seguridad | Dependencias vulnerables y patrones inseguros | Dependabot y CodeQL | Cero hallazgos altos abiertos |

Invariantes: todo asiento cuadra al peso; un asiento aprobado nunca se edita; nada cruza de un cliente a otro; el mismo XML dos veces no crea dos documentos; un periodo cerrado no acepta asientos.

Una funcionalidad está terminada cuando cumple los criterios, tiene pruebas, la pantalla coincide con el mockup en computador y celular, el revisor no dejó hallazgos graves ni medios, la CI está en verde y el usuario la probó.

## Entornos y seguridad

| Entorno | Dónde | Cómo llega un cambio |
| --- | --- | --- |
| Local | Tu computador: SQL Server en un contenedor creado desde VS Code, archivos en una carpeta local y la app | Al guardar |
| Staging | Proveedor por definir, plan pequeño | Automático al unir a `main` |
| Producción | Proveedor por definir, con respaldo y alta disponibilidad | Tras aprobación en GitHub |

- **Acceso:** contraseña más doble factor; roles de contador titular, auxiliar y solo lectura, con acceso por cliente.
- **Separación de clientes:** filtro por `UUID_EMPRESAS` en EF Core y seguridad por fila en SQL Server.
- **Cifrado:** TLS, cifrado transparente de la base y secretos en el gestor de secretos del proveedor (en local, user-secrets).
- **Auditoría:** tablas temporales de SQL Server.
- **Conservación:** XML en almacenamiento inmutable por 10 años, huella SHA-256 y exportación anual de libros en PDF.
- **Referencia:** OWASP ASVS nivel 2.

## Decisiones pendientes

| Decisión | Opciones | Bloquea |
| --- | --- | --- |
| Dónde desplegar | Azure, AWS o un servidor propio con Docker | Producción |
| Proveedor tecnológico aliado | API por volumen (tipo MATIAS) o proveedor autorizado (Afacturar, Plemsi) | Eventos RADIAN y nómina electrónica |
| Marca | Registrar «nítido» en la SIC, clases 9 y 42, y el dominio | Lanzamiento público |
| Modelo de precio | Por cliente activo, por documento o plan fijo por firma | Piloto pagado |
| Equipo | Solo con agentes, o sumar a alguien | Fases 5 en adelante |
