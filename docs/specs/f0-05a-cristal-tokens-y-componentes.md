# F0-05a · Fase 0 · 5a · Sistema de diseño Cristal: tokens, componentes base, menú lateral y área de trabajo

| | |
| --- | --- |
| Estimación | 2 días |
| Depende de | F0-01. Puede ir en paralelo con F0-02a a F0-04, desde el día 3. |
| Orden de construcción | 8 de 10 (paralela) |
| Repositorio | Frontend (`nitido-frontend`). Las rutas de esta especificación son relativas a su raíz. |
| Etiquetas | `fase-0`, `historia`, `frontend`, `diseno` |
| Estado | Terminada el 2026-10-25 (implementada, con pruebas; sin pull request: todavía no hay repositorios en GitHub). Cuatro diferencias con la especificación: Angular CDK no se instaló (ningún componente lo necesita todavía), «Salir» se agregó al final de la franja en celular (recomendación de la pregunta abierta 4), el toast llegó aquí en vez de en F0-05c, y las capturas están generadas pero **falta que el usuario las apruebe frente al mockup**. |

Esta historia sale de partir la historia 5 original en tres (F0-05a, F0-05b y F0-05c): lo que había que construir no cabía en 2 días. Esta parte no depende del backend.

**Referencias:**
- mockup `docs/mockup-nitido.dc.html` (repositorio del frontend), con el tema Cristal (`.ap.t-cristal`);
- logo `docs/logo-nitido.svg` (repositorio del frontend);
- skill `diseno-cristal`.

## Historia

Como contador quiero que nítido se vea y se comporte como el mockup aprobado, en computador y en celular, para trabajar en una herramienta clara y consistente.

## Criterios de aceptación

### Tokens y estilos

- [ ] `src/styles/tokens.scss` define en `:root` dos grupos de variables:
  - los tokens de la skill `diseno-cristal`;
  - las variables del tema `.ap.t-cristal` del mockup que la skill no lista, con los mismos valores: líneas 44 a 54, `--strong2`, `--border2`, `--bar`, `--track`, `--toast-*`, `--logo-bg` y `--side-*`.

  El fondo de la aplicación y el panel de vidrio también quedan como variables en el mismo archivo.
- [ ] La prueba de Playwright `tokens.spec.ts` lee con `getComputedStyle` `--accent`, `--ink`, `--ok`, `--warn`, `--err`, `--r`, `--r2` y `--side-bg`, y obtiene los valores del mockup.
- [ ] `npm run lint:estilos` (Stylelint) sale con código 0. Si se agrega a propósito un color literal fuera de `tokens.scss`, por ejemplo `color: #ff0000` en un componente, el comando falla.
- [ ] La fuente Geist (400, 500, 600 y 700) se sirve desde la propia aplicación; el mockup la carga de Google Fonts. La prueba `sin-dominios-externos.spec.ts` comprueba que ninguna petición de la página sale de `localhost`.
- [ ] El logo es el de `docs/logo-nitido.svg`, dibujado en línea con las variables `--side-logo-bg` y `--logo-dot`, igual que en el mockup. Va acompañado de la palabra «nítido» en Geist 700, con espaciado de letras de −0,05 em.

### Estructura en modo firma

Corresponde a las líneas 93 a 110 y 252 a 270 del mockup.

- [ ] La estructura tiene:
  - un menú lateral de vidrio de 232 a 248 px de ancho, con el logo y la marca arriba y el elemento «Clientes» marcado como activo;
  - abajo, el bloque de usuario con las iniciales, el nombre, el rol y el botón «Salir»;
  - un área de trabajo de vidrio con encabezado: título de 24 px y peso 600, subtítulo y acciones a la derecha.
- [ ] El bloque de usuario recibe nombre y rol como entradas del componente. En la fase 0 el rol dice «Administrador»; el mockup dice «Contador titular». Los datos reales los conecta F0-05b con `/api/autenticacion/yo`.
- [ ] Por debajo de 760 px el menú pasa a una franja horizontal arriba y se ocultan los títulos de grupo y el bloque de usuario, como en las líneas 198 a 213 del mockup. A 390×844, Playwright verifica que el menú ocupa todo el ancho y empieza arriba.
  - Lo que pasa con «Salir» en celular depende de la pregunta abierta 4 del índice.

### Componentes base

- [ ] Hay componentes standalone, con entradas por signals, que siguen las clases del mockup. Las pruebas unitarias verifican el alto y el `role="alert"`.

  | Componente | Clase del mockup | Reglas |
  | --- | --- | --- |
  | Botón principal | `.bt.pr` | 36 px de alto, fondo `--accent` |
  | Botón secundario | `.bt` | |
  | Botón de peligro | `.bt.tx` | |
  | Botón fantasma | `.bt.gh` | |
  | Campo | `.cp` + `.in` | 40 px, etiqueta arriba, error con `role="alert"` |
  | Búsqueda | `.bu` | 36 px, ícono de lupa |
  | Estado | `.pt` | punto de 6 px más texto |
  | Aviso | `.nota`, `.nota.wa` | |
  | Estado vacío | `.vac` | |

### Accesibilidad y formato

- [ ] Al recorrer el menú con Tab, cada elemento muestra un contorno de 2 px en `--accent`. Lo verifica `foco.spec.ts`.
- [ ] El análisis de accesibilidad con `@axe-core/playwright` no reporta violaciones graves ni críticas en la estructura.
- [ ] Las funciones compartidas de formato tienen pruebas unitarias:
  - `formatearMonto(-1234567.5)` devuelve `"−1.234.567,50"`, con el signo menos tipográfico;
  - `formatearFechaCorta` del 12 de octubre de 2026 devuelve «12 oct»;
  - `formatearFechaLarga` del 12 de octubre de 2026 devuelve «12 de octubre de 2026»;
  - `formatearNit("800197268", "4")` devuelve «NIT 800.197.268-4».

### Comparación con el mockup

- [ ] El proyecto `celular` (390×844) se agrega a `e2e/playwright.config.ts`.
- [ ] Hay capturas de Playwright (`toHaveScreenshot`) de la estructura a 1440×900 y 390×844. El usuario las aprueba frente al mockup, y las diferencias quedan listadas en la sección «Pantalla» de esta especificación.

## Pantalla

Mockup `docs/mockup-nitido.dc.html`, tema Cristal: estructura general y menú.

**Diferencias aprobadas:**
- **Menú en modo firma:** solo «Clientes». El grupo «Firma» del mockup (Equipo, Plantillas de reglas, Parámetros tributarios, Privacidad y datos) no se dibuja, porque esas pantallas no existen en la fase 0.
- **Selector de cliente y grupos del modo cliente:** el mockup solo los muestra dentro de un cliente, y en la fase 0 no hay pantallas de cliente.
- **Rol del bloque de usuario:** «Administrador» en lugar de «Contador titular».
- **Fuente:** se sirve desde la aplicación, no desde Google Fonts.
- **Opacidad del vidrio:** el menú usa 0,42 y el área de trabajo 0,5, como el mockup. Ver el ajuste sugerido a la skill en el índice.
- **Logo:** la plantilla de Angular sirve los archivos estáticos desde `public/`. El logo, si se usa como archivo, va en `public/marca/`; la skill `diseno-cristal` decía `src/assets/marca/` y ya se corrigió a `public/marca/`.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables.

## Diseño técnico

Se completa en el paso 3. Ya están decididas dos librerías nuevas, solo de desarrollo: Stylelint, para garantizar que no haya colores fuera de los tokens, y `@axe-core/playwright` en `e2e/`. Angular CDK se instala aquí, como pide `CLAUDE.md`.

## Fuera de alcance

- Grupos del modo cliente (Pendientes, Contabilidad, Bancos, Nómina, Reportes, Ajustes), selector de cliente y grupo «Firma».
- Toast: llega con F0-05c, que es la primera que lo usa.
- Temas distintos de Cristal (Papel, Nocturno, Índigo del mockup).
- Modo oscuro.
