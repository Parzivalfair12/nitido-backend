# nítido

Software contable para firmas de contadores en Colombia que llevan muchos clientes. Lee los XML de la DIAN, propone los asientos con sus retenciones, el contador los aprueba y nítido arma los libros, los estados financieros y la exportación a Siigo.

Plan completo: `docs/plan-de-desarrollo.md` (copia del documento «nítido: plan de desarrollo»). Mockup de referencia: el canvas «Mockup software contable», página «Prototipo nítido»; su copia vive en `docs/mockup-nitido.dc.html` del repositorio del frontend (`nitido-frontend`).

## Dos repositorios

nítido vive en dos repositorios independientes (decisión del 18 de octubre de 2026):

- **`nitido-backend`** (este): la API en .NET, `db/`, `tests/` de .NET, `docs/` (plan, especificaciones y ADR) y este `CLAUDE.md`.
- **`nitido-frontend`**: la aplicación Angular (`src/`), las pruebas de punta a punta con Playwright (`e2e/`), el mockup y el logo (`docs/`) y `.nvmrc`. No se puede abrir con una ruta relativa desde este repositorio; se trabaja en su propia carpeta.

Los agentes y las skills viven en `.claude/` de la carpeta raíz `nitido/`, común a los dos repositorios (no se versiona con ellos).

El contrato entre los dos es OpenAPI: el backend publica `/openapi/v1.json` y el frontend genera su cliente a partir de ese documento. Un cambio que toca la API y una pantalla son dos pull requests coordinados, con el del backend primero.

## Stack

- Frontend: Angular 22, componentes standalone, signals, SCSS con los tokens del estilo Cristal, Angular CDK. Código en `src/` del repositorio del frontend (`nitido-frontend`).
- Backend: ASP.NET Core sobre .NET 10, monolito modular, EF Core 10, Hangfire, FluentValidation, Serilog, OpenTelemetry. Código en `Nitido.Api/`, `Nitido.Compartido/` y `Modulos/` (raíz de este repositorio).
- Base de datos: SQL Server. En local corre en un contenedor creado desde la extensión MSSQL de VS Code (necesita Docker Desktop; no se instala SQL Server). Scripts y semillas en `db/`.
- Pruebas: xUnit, Testcontainers con SQL Server en `tests/` de este repositorio; Playwright en `e2e/` del repositorio del frontend.
- CI/CD: GitHub Actions, un workflow por repositorio. Despliegue: imagen Docker; proveedor por definir (Azure es la primera opción). El código no depende de servicios exclusivos de una nube.

## Comandos

Desde la raíz de este repositorio (backend):

```bash
# SQL Server local: VS Code > vista SQL Server > "Create local SQL Container" (SQL Server 2025, puerto 1433).
# La cadena de conexión va en user-secrets: dotnet user-secrets set "ConnectionStrings:Nitido" "<cadena>" -p Nitido.Api
dotnet build                                # compila la solución
dotnet test                                 # unidad + integración + contables
dotnet ef migrations add <Nombre> --context <Modulo>DbContext -p Modulos/<Modulo>/Infraestructura -s Nitido.Api -o Migraciones
```

Desde la raíz del repositorio del frontend (`nitido-frontend`):

```bash
npm install && npm start                    # frontend en http://localhost:4200
npm test                                    # pruebas del frontend
cd e2e && npx playwright test               # punta a punta (requiere app corriendo)
```

## Reglas que nunca se rompen

1. Todo asiento cuadra: débitos igual a créditos, al peso. Ningún código guarda un asiento descuadrado.
2. Un asiento aprobado no se edita ni se borra. Se corrige con un asiento de reversión.
3. Ningún dato de un cliente se ve desde otro. Toda tabla de datos de cliente lleva `UUID_EMPRESAS` y toda consulta pasa por el filtro de multiempresa.
4. Dinero siempre `decimal` en C# y `DECIMAL(19,2)` en SQL. Nunca `double` ni `float`.
5. Ningún valor tributario (UVT, bases, tarifas, salario mínimo) va escrito en el código. Todo sale de los parámetros con fecha de vigencia. Ver skill `normativa-colombia`.
6. El XML original nunca se modifica. Se guarda con su huella SHA-256 en almacenamiento inmutable.
7. Nada de SDK de una nube en el dominio ni en los casos de uso. Archivos, secretos y colas pasan por interfaces propias (`IAlmacenArchivos`, `ISecretos`) con una implementación local (carpeta en disco) y una por proveedor.
8. Los textos que ve el usuario van en español de Colombia, claros y sin jerga técnica. Números con formato es-CO.

## Cómo se trabaja

- Toda funcionalidad nueva arranca con la skill `nueva-funcionalidad`. Ahí está el ciclo de 10 pasos y qué agente entra en cada uno.
- Agentes en `.claude/agents/` de la carpeta raíz `nitido/` (común a los dos repositorios, no se versiona con ellos): `arquitecto`, `contador`, `dba`, `dev-backend`, `dev-frontend`, `tester`, `revisor`, `devops`. `dev-frontend`, `tester`, `revisor` y `devops` también trabajan en el repositorio del frontend.
- Ningún agente aprueba su propio trabajo. Quien implementa no revisa; el `tester` no corrige código de producción.
- Antes de tocar base de datos, lee la skill `convenciones-bd`. Antes de tocar asientos, `motor-asientos`. Antes de tocar XML, `lector-dian`. Antes de tocar pantallas, `diseno-cristal`.
- Antes de crear un módulo o mover código entre capas, lee `docs/arquitectura-backend.md`. Las pruebas de arquitectura deben seguir en verde.

## Git

- `main` siempre desplegable. Ramas `feat/<modulo>-<tema>`, `fix/<modulo>-<tema>`.
- Commits en formato Conventional Commits, en español: `feat(documentos): leer notas débito`.
- Todo entra por pull request con CI en verde y revisión del agente `revisor`.
- Si el cambio toca la API y una pantalla, son dos pull requests, uno por repositorio, con el del backend primero.

## Estructura

Este repositorio (`nitido-backend`):

```
Nitido.Api/                         host de ASP.NET Core: raíz de composición
Nitido.Compartido/                  núcleo puro: Resultado; multiempresa y puertos comunes desde F0-02a (sin paquetes)
Nitido.Compartido.Infraestructura/  EF Core común: convenciones, auditoría, filtro y SESSION_CONTEXT (nace en F0-02a)
Nitido.Compartido.Api/              web común: Resultado a ProblemDetails, filtro de cliente activo (nace en F0-02b)
Modulos/<Modulo>/                   Dominio, Aplicacion, Contratos (si otro módulo lo usa), Infraestructura, Api
db/                         PUC, semillas, seguridad por fila
tests/unidad, tests/integracion, tests/contables, tests/xml
docs/adr, docs/specs
CLAUDE.md                   reglas del proyecto
```

Repositorio del frontend (`nitido-frontend`):

```
src/                        Angular
public/                     archivos estáticos (marca)
e2e/                        Playwright
docs/                       mockup y logo
```

Módulos: Firmas, Clientes, Terceros, PlanCuentas, Documentos, Reglas, Contabilidad, Impuestos, Reportes, Exogena, Bancos, Nomina, Integraciones, Conservacion.
