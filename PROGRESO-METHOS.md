# METHOS — Registro de progreso

**Última actualización:** 12-11-2026  
**Proyecto:** Methos (editor de archivos de Lineage II Interlude)  
**Repositorio de trabajo:** https://github.com/Methos48/Methos-U-Editor  
**Módulo complementario:** https://github.com/Methos48/Methos-XDAT

Este documento es la bitácora permanente del proyecto. Cada sesión de trabajo debe actualizarlo con cambios reales, pruebas ejecutadas, resultados, problemas pendientes y el siguiente paso. No marcar una tarea como terminada sin una prueba que lo demuestre.

## Objetivo final

Entregar una aplicación de Windows llamada **Methos**, instalable y con acceso directo en el escritorio, que permita elegir entre **ARCHIVOS U** y **ARCHIVOS XDAT**. Debe conservar copias originales y trabajar sobre copias dentro de su espacio de trabajo. La aplicación debe detectar y aplicar los métodos de protección que realmente pueda reconocer y validar; si no conoce un método, debe detenerse sin dañar ni sobrescribir el archivo original.

Flujo esperado: cargar archivo → copia de seguridad automática → descompilar/descifrar → editar → compilar/cifrar de nuevo → verificar el resultado.

## Registro de avances

### 12-11-2026 — Interfaz principal y presentación

- Se eligió como diseño de referencia el primer concepto visual: panel **ARCHIVOS U** rojo a la izquierda y **ARCHIVOS XDAT** azul a la derecha, sobre fondo oscuro.
- Se modificó el lanzador principal para mostrar ambos módulos y permitir entrar a cada uno.
- Se agregó navegación para volver a INICIO.
- Se preparó el icono de aplicación con una **M roja** y se integró su generación al flujo de compilación de Windows.
- Se configuró el acceso directo de escritorio durante la instalación.
- Se guardaron cambios en la rama principal en estos commits:
  - `590a39c7bcb720c60f6be5862dc3d090a936b4c5` — rediseño del lanzador.
  - `d65d7046041bf3bb476eb8fd2fe0127ee7771b0d` — inicio en el lanzador.
  - `72146b133be93cb581c5e5c8d1b6caaa186675e4` — acceso directo de escritorio.
  - `05d379e99ad500432017f0d3cf973a57cc74d8ff` — normalización de la ruta del icono.
- **Estado de verificación:** estos cambios visuales están guardados en GitHub, pero todavía no se debe afirmar que la última versión compila o instala correctamente hasta revisar el resultado de GitHub Actions correspondiente a esos cambios.

### Base técnica ya incorporada (estado conocido antes de esta actualización)

- La aplicación Windows integra el módulo de trabajo U y aloja el módulo XDAT como proceso separado dentro de la ventana principal.
- El flujo U inspecciona las capas de protección antes de permitir el procesamiento y bloquea los casos en los que no puede garantizar una decodificación/codificación completa.
- El módulo XDAT contiene detección de formato y un pipeline automático que contempla protocolos conocidos de `l2encdec`; los métodos desconocidos deben quedar identificados como no compatibles, no tratarse como descifrados.
- Existen pruebas previas del motor de paquetes UE2 y del módulo XDAT. Una ejecución anterior de Windows CI terminó correctamente y produjo un instalador, pero fue **anterior** a los últimos cambios de interfaz e icono. No cuenta como validación de la versión actual.
- La prueba anterior de instalación/arranque verificó que el instalador incluía Methos, UCC, el módulo XDAT y sus carpetas de trabajo. No demostró que cualquier archivo protegido pueda descifrarse y recompilarse.

## Pendientes prioritarios

1. **Validar la versión actual en Windows CI:** comprobar compilación, generación del icono, instalador, acceso directo, instalación silenciosa y arranque. Revisar logs si falla y corregir antes de dar por finalizada la interfaz.
2. **Revisar el recorrido real de ARCHIVOS U:** selector de archivos, copia de seguridad, descompilación, importación de fuentes, compilación, exportación y preservación de la protección original. Probar con archivos de referencia y verificar integridad/round-trip.
3. **Revisar el recorrido real de ARCHIVOS XDAT:** selección, copia de seguridad, detección, descifrado, edición, recompilación y validación. Separar claramente protocolos confirmados de los desconocidos.
4. **Investigar la segunda interfaz personalizada:** utilizar los dos lados (archivo protegido y archivo descifrado) que el usuario mantiene en sus repositorios Methos. No asumir que es Dunk3l ni confundirla con el ejemplo XDAT del proyecto.
5. **Integrar cada método nuevo solo después de probarlo** con archivos de referencia y comprobar que el resultado vuelve a ser legible por el formato/herramienta correspondiente.
6. **Preparar la entrega final llamada Methos:** instalador Windows, código fuente, instrucciones claras y registro actualizado en GitHub.

## Reglas para continuar el trabajo

- Continuar desde el estado real de GitHub; consultar la rama principal y los últimos commits antes de modificar archivos.
- No volver a empezar ni reemplazar componentes que ya funcionan sin motivo.
- No declarar compilación, descifrado, cifrado o round-trip exitosos sin evidencia de pruebas.
- Nunca sobrescribir el archivo original seleccionado por el usuario. Trabajar con copias y conservar respaldo.
- No inventar métodos de cifrado ni forzar un protocolo desconocido.
- Mantener esta bitácora actualizada al terminar cada bloque de trabajo, incluyendo fecha, archivos/commits modificados, pruebas y resultado.
- El usuario no programa: entregar archivos y pasos concretos; explicaciones breves y de a un punto por vez.

## Próxima acción

Consultar GitHub Actions de la rama principal para los commits recientes del lanzador/icono. Si el workflow falló, leer el log del paso fallido y corregirlo. Si terminó correctamente, registrar el run y pasar a la validación funcional de ARCHIVOS U y ARCHIVOS XDAT.

### 12-11-2026 — Instalador Methos Windows x64 generado y probado

- **Resultado: compilación e instalación superadas.** GitHub Actions ejecutó el workflow `Methos U Editor - Windows Release` para el commit `05d379e99ad500432017f0d3cf973a57cc74d8ff`.
- Ejecución: [Windows Release #438](https://github.com/Methos48/Methos-U-Editor/actions/runs/43234204448) — **SUCCESS**.
- Los 19 pasos principales finalizaron correctamente, incluidos:
  - generación del icono M roja;
  - publicación autónoma de Methos para Windows x64;
  - integración del runtime UCC y 169 fuentes de referencia;
  - compilación y pruebas del módulo XDAT;
  - creación del instalador Inno Setup;
  - instalación silenciosa, comprobación de componentes y arranque de Methos;
  - arranque de prueba del ejecutable XDAT;
  - publicación del instalador como artefacto.
- Artefacto generado: `Methos-Windows-Installer-x64`, ID `13950374516`, tamaño 77,461,895 bytes. SHA-256: `045b010a645c89f881e8e565c1cb99039a03e7083aa59aa8d859ce0c60658d9a`.
- **Qué demuestra:** el instalador Windows x64 se generó; pudo instalarse silenciosamente; Methos y XDAT arrancaron; se encontraron los componentes UCC y las carpetas de trabajo. El instalador incluye el acceso directo de escritorio configurado en Inno Setup.
- **Qué NO demuestra:** que todas las operaciones de descompilación/compilación U funcionen con cualquier archivo, ni que todos los formatos XDAT personalizados puedan descifrarse. Eso sigue siendo trabajo funcional separado.
- **Entrega actual del instalador:** se encuentra guardada como artefacto de GitHub Actions en la ejecución enlazada arriba. Aún no es una publicación permanente de GitHub Releases.

## Próximo bloque de trabajo

1. Probar y corregir el recorrido funcional de ARCHIVOS U con paquetes reales: carga/copia de seguridad, descompilación, importación de fuentes, compilación y verificación.
2. Probar el recorrido XDAT con archivos de referencia protegidos y no protegidos.
3. Publicar una versión estable en GitHub Releases cuando el comportamiento funcional esté validado; conservar el nombre de producto **Methos**.
