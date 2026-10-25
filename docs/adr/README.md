# Decisiones de arquitectura (ADR)

Cada decisión grande que toca varios módulos o que es difícil de cambiar después queda escrita aquí, en un archivo `docs/adr/NNNN-titulo.md` (por ejemplo, `0001-despliegue-en-azure.md`). El número es consecutivo y no se reutiliza. Una decisión que se reemplaza no se borra: se marca como «Reemplazado» y se enlaza a la nueva.

## Índice

| Número | Título | Estado |
| --- | --- | --- |
| [0001](0001-clean-architecture.md) | Clean Architecture dentro de cada módulo del monolito | Aceptado (2026-10-18) |
| [0002](0002-dbcontext-y-migraciones-por-modulo.md) | Un DbContext y un historial de migraciones por módulo, en una sola base | Aceptado (2026-10-18) |
| [0003](0003-multiempresa-en-dos-barreras.md) | Multiempresa en dos barreras: filtro global de EF y seguridad por fila de SQL Server | Aceptado (2026-10-24) |
| [0004](0004-autenticacion-cookies-y-doble-factor.md) | Autenticación: cookies de sesión, doble factor obligatorio e ingreso por correo | Aceptado (2026-10-25) |

Estados posibles: Propuesto, Aceptado, Reemplazado, Rechazado.

## Plantilla

Copia esto en `docs/adr/NNNN-titulo.md` y complétalo.

```markdown
# NNNN. Título de la decisión

| | |
| --- | --- |
| Estado | Propuesto |
| Fecha | AAAA-MM-DD |

## Contexto

Qué problema hay y qué condiciones lo rodean. Qué reglas de CLAUDE.md o del plan pesan sobre la decisión.

## Opciones

1. **Opción A.** Qué es, ventajas y desventajas.
2. **Opción B.** Qué es, ventajas y desventajas.

## Decisión

Qué se decide y por qué.

## Consecuencias

Qué cambia a partir de ahora: lo bueno, lo que cuesta y lo que queda pendiente.
```
