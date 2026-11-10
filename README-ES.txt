METHOS U EDITOR
================

Herramienta de Windows para trabajar con paquetes Unreal Engine 2 de Lineage II Interlude.

CARPETAS DE TRABAJO
- Documentos\Methos U Editor\u completo
  Aquí se guardan los paquetes compilados (.u).
- Documentos\Methos U Editor\u por partes
  Aquí se guardan las fuentes (.uc), dentro de una carpeta por paquete.

El programa mantiene dos acciones principales: DESCOMPILAR y COMPILAR.

DESCOMPILAR
1. Coloca o agrega el archivo .u en la lista.
2. Selecciona el paquete.
3. Pulsa DESCOMPILAR.
4. Las fuentes .uc aparecen en la carpeta del paquete dentro de "u por partes".

Para el Interface.u original de Interlude incluido en este proyecto, Methos U reconoce el SHA-256 de la referencia y recupera las 169 fuentes originales verificadas incluidas con el kit Compilador U. Si el paquete fue compilado previamente por Methos U, recupera la copia exacta de fuentes guardada en la caché local.
Para otros paquetes o versiones que no tengan una fuente de referencia, utiliza UELib como lector de compatibilidad. La salida de ese lector puede contener expresiones nativas que no se pueden reconstruir como UnrealScript original.

COMPILAR
1. Selecciona el paquete .u en "u completo".
2. Comprueba que sus fuentes .uc estén en la carpeta correspondiente dentro de "u por partes".
3. Pulsa COMPILAR.
4. El programa valida el resultado y solo entonces reemplaza el paquete de destino.

Para Interface.u de Lineage II Interlude se utiliza el compilador UCC de Interlude incluido en el instalador. El instalador incorpora el runtime completo y las 169 fuentes de referencia; no tienes que descargarlos por separado. Se ejecuta internamente en una carpeta temporal; no necesitas instalar UCC, .NET ni ejecutar scripts manualmente.
El resultado se comprueba con el analizador de paquetes y UELib. La prueba automatizada confirma 169 clases y las mismas tablas de nombres, imports y exports que el Interface.u de referencia.
El paquete recompilado no tiene por qué ser idéntico byte por byte al original: UCC vuelve a serializar el paquete.

SEGURIDAD Y COPIAS
- El archivo .u original no se modifica durante la compilación temporal.
- Si existe un destino, se guarda una copia de seguridad antes de reemplazarlo.
- Las copias de seguridad y la caché de fuentes se guardan fuera de las dos carpetas de trabajo, en %LOCALAPPDATA%\Methos U Editor.
- Después de una compilación correcta, Methos U guarda una copia de las fuentes asociada al hash del paquete generado. Así puede recuperarlas exactamente al volver a descompilarlo.

ALCANCE
El backend UCC incluido está preparado para Interface.u de Lineage II Interlude.
Para otros paquetes .u, el motor nativo de Methos todavía solo admite un subconjunto limitado de funciones sencillas. No se debe interpretar esa compatibilidad parcial como soporte completo de todo UnrealScript.

IMPORTANTE
Aunque las pruebas automáticas verifican la estructura del paquete y sus 169 clases, todavía se debe probar cada Interface.u recompilado en una copia de prueba del cliente antes de usarlo en el juego. Conserva siempre el Interface.u original.
