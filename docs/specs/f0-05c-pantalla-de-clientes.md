# F0-05c · Fase 0 · 5c · Pantalla de Clientes

| | |
| --- | --- |
| Estimación | 2 días |
| Depende de | F0-05a, F0-04. Para el flujo punta a punta, también F0-05b. |
| Orden de construcción | 10 de 10 |
| Repositorio | Frontend (`nitido-frontend`). Consume la API de F0-04, que vive en `nitido-backend`: el pull request del backend va primero y el cliente se genera desde su OpenAPI. |
| Etiquetas | `fase-0`, `historia`, `frontend`, `diseno`, `modulo:clientes` |
| Estado | Terminada el 2026-10-25 (implementada, con pruebas; sin pull request: todavía no hay repositorios en GitHub). Capturas generadas de los cuatro estados; **falta que el usuario las apruebe frente al mockup**. |

**Referencia:** mockup `docs/mockup-nitido.dc.html` (repositorio del frontend), líneas 272 a 298 (pantalla) y 1118 a 1154 (comportamiento y mensajes), más la skill `diseno-cristal`.

## Historia

Como contador de una firma quiero ver, buscar y agregar mis clientes en una sola pantalla para tener a la mano todas las empresas que llevo.

## Criterios de aceptación

### Encabezado

- [ ] `/clientes` es la pantalla de inicio después del ingreso y tiene:
  - el título «Clientes»;
  - el subtítulo «<Mes> <año>. <N> clientes.», por ejemplo «Octubre 2026. 6 clientes.», o «1 cliente.» en singular;
  - a la derecha, la búsqueda con placeholder y `aria-label` «Buscar por nombre o NIT», y el botón principal «Agregar cliente».

### Tabla

- [ ] La tabla tiene las columnas «Cliente», «Pendientes», «Contabilizado en <mes en curso>» (en octubre de 2026: «Contabilizado en octubre») y «Estado». Usa las mismas proporciones de la línea 292 del mockup y un ancho mínimo de 760 px, con desplazamiento horizontal en celular.
- [ ] En cada fila:
  - «Cliente» muestra la razón social en peso 500 y debajo el documento con formato «NIT 800.197.268-4»;
  - «Pendientes» y «Contabilizado» muestran «—», sin barra;
  - «Estado» muestra el punto y «Sin documentos», como el mockup para un cliente nuevo.
- [ ] Las filas miden al menos 60 px, como el mockup, tienen separador `--line2` y se ordenan por razón social.
- [ ] La búsqueda filtra en el navegador mientras se escribe, por razón social o por número de documento. Ignora mayúsculas, puntos y guion: «800.197» y «800197» encuentran lo mismo.

### Estados vacíos

- [ ] Si la búsqueda no encuentra nada, se muestran «Ningún cliente coincide con «<texto>»» y «Busca por razón social o por número de documento.», como el mockup.
- [ ] Si la firma no tiene clientes, se muestran «Aún no tienes clientes» y «Agrega el primero con el botón Agregar cliente.»

### Agregar cliente

- [ ] «Agregar cliente» abre el formulario en línea del mockup (líneas 282 a 289), con:
  - el campo «Razón social o nombre» (placeholder «Distribuidora Andina SAS»);
  - el campo «NIT o cédula» (placeholder «901234567»);
  - los botones «Guardar cliente» (principal) y «Cancelar» (secundario). «Cancelar» cierra el formulario sin guardar.
- [ ] La validación en pantalla muestra en un `role="alert"`, debajo del formulario, los mensajes del mockup:
  - «Escribe la razón social o el nombre del cliente.»
  - «Escribe el NIT o la cédula del cliente.»
- [ ] Los errores de la API (F0-04) se muestran con su mensaje (el `title` de la respuesta ProblemDetails; Ver ADR 0001) en el mismo lugar:
  - un número que no es NIT: «Por ahora solo puedes agregar clientes con NIT…»;
  - un dígito de verificación que no corresponde;
  - un `409`: «Este cliente ya está en tu firma.»
- [ ] Al guardar, el formulario se cierra, la fila aparece en la tabla y se muestra el toast «<Razón social> agregado» durante 6 segundos, abajo al centro y con `role="status"`. El toast es un componente nuevo de Cristal, según la línea 196 del mockup.

### Pruebas y comparación

- [ ] `clientes.spec.ts` pasa con dos casos:
  - agregar un cliente con NIT y verlo en la lista con su dígito de verificación;
  - una segunda firma ingresa y no ve ese cliente (regla 3, comprobada de punta a punta).
- [ ] Hay capturas a 1440×900 y 390×844 de cuatro estados: lista con clientes, sin clientes, búsqueda sin resultados y formulario abierto con error. El usuario las aprueba frente al mockup, y axe no reporta violaciones graves ni críticas.
- [ ] Todos los textos están en español de Colombia y en segunda persona.

## Pantalla

Mockup `docs/mockup-nitido.dc.html`, líneas 272 a 298.

**Diferencias aprobadas:**
- **Etiqueta «NIT o cédula»:** se conserva la del mockup, pero en la fase 0 solo se acepta NIT de persona jurídica (F0-04). Con una cédula, la pantalla muestra el mensaje de la API.
- **Subtítulo:** el mockup resume los pendientes del mes («Octubre 2026. 12 documentos pendientes en 3 clientes.»). En la fase 0 no hay documentos, así que muestra el número de clientes.
- **Pendientes y Contabilizado:** se muestran con «—» y sin barra; los llenan las fases 2 y 3.
- **Estado:** siempre «Sin documentos» en la fase 0.
- **Filas no clicables:** en el mockup, la fila lleva a Pendientes del cliente, pantalla que no existe en la fase 0.
- **Orden:** el mockup ordena por pendientes; en la fase 0 se ordena por razón social.
- **Lista vacía:** el caso «Aún no tienes clientes» no está en el mockup. Se agrega con el mismo estilo `.vac`.

## Reglas contables y tributarias

No aplica. La fase 0 no tiene reglas contables.

## Diseño técnico

Se completa en el paso 3.

## Fuera de alcance

- Clientes con cédula.
- Abrir el detalle de un cliente, editarlo o desactivarlo.
- Valores reales de Pendientes, Contabilizado y Estado.
- Paginación.
- Botón «Deshacer» en el toast.
