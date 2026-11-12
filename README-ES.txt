ESTRUCTURA DE TRABAJO
--------------------
Methos crea junto al ejecutable una única carpeta "Compilados":

Compilados/
  U/
    Archivo/       -> paquetes .u originales de trabajo y resultado compilado
    Decopilado/    -> clases .uc extraídas y editables
  XDAT/
    Archivo/       -> archivos .xdat originales de trabajo y resultado compilado
    Decopilado/    -> ventanas y accesos directos XML extraídos y editables

El botón COMPILAR lee los archivos que estén guardados actualmente en Decopilado y reemplaza el archivo correspondiente dentro de Archivo. Antes de reemplazarlo, Methos conserva una copia de seguridad. El archivo que el usuario seleccionó fuera de Methos nunca se modifica.

METHOS
================

Aplicación unificada de Windows para trabajar con archivos .u y .xdat de Lineage II Interlude.

INTERFAZ UNIFICADA
Al abrir Methos verás dos botones superiores:
- ARCHIVOS U: muestra las funciones para descompilar y compilar paquetes Unreal (.u).
- ARCHIVOS XDAT: cambia a la vista del módulo XDAT, integrado dentro de la misma ventana principal.

La barra superior identifica la vista activa. No necesitas abrir dos programas ni buscar accesos directos distintos.

CARPETAS DE TRABAJO
Dentro de la carpeta donde se instala Methos se crean:
- Archivos U\Comprimido
  Aquí se colocan los paquetes .u que se van a descompilar o usar como base para compilar.
- Archivos U\Descomprimido
  Aquí aparecen las fuentes .uc extraídas y se editan las clases para volver a compilar.

Las carpetas se crean automáticamente al abrir el programa. No es necesario crear nada manualmente.

Cada vista mantiene sus propias acciones DESCOMPILAR y COMPILAR y sus propias carpetas de trabajo.

DESCOMPILAR
1. Coloca o agrega el archivo .u en la lista.
2. Selecciona el paquete.
3. Pulsa DESCOMPILAR.
4. Las fuentes .uc aparecen en la carpeta del paquete dentro de "Archivos U\\Descomprimido".

Para el Interface.u original de Interlude incluido en este proyecto, Methos U reconoce el SHA-256 de la referencia y recupera las 169 fuentes originales verificadas incluidas con el kit Compilador U. Si el paquete fue compilado previamente por Methos U, recupera la copia exacta de fuentes guardada en la caché local.
Para otros paquetes o versiones que no tengan una fuente de referencia, utiliza UELib como lector de compatibilidad. La salida de ese lector puede contener expresiones nativas que no se pueden reconstruir como UnrealScript original.

COMPILAR
1. Selecciona el paquete .u en "Archivos U\\Comprimido".
2. Comprueba que sus fuentes .uc estén en la carpeta correspondiente dentro de "Archivos U\\Descomprimido".
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


DETECCIÓN DE CIFRADO
Methos analiza primero la cabecera de los archivos antes de enviarlos a un lector o compilador.
- Para XDAT, reconoce 18 versiones documentadas Lineage2Ver y conecta el motor público para las 9 variantes que soporta.
- Para U, incorpora un detector de la misma familia de cabeceras y reconoce la firma de paquetes Unreal UE1/UE2 sin cifrar.
- Una versión identificada no significa que todas las claves o protecciones privadas sean compatibles.
- Si no reconoce el cifrado, muestra primero «No se reconoce este cifrado» y después los datos técnicos. El original se conserva.
- En U, la detección está integrada; el descifrado automático de paquetes U es una fase separada que todavía debe conectarse y probarse.

La vista XDAT se ejecuta integrada en la ventana principal de Methos. El instalador incluye sus componentes y el motor de cifrado necesario.
