METHOS U EDITOR
================

Carpetas de trabajo
- Documentos\Methos U Editor\u completo
- Documentos\Methos U Editor\u por partes

DESCOMPILAR
1. Agrega uno o varios archivos .u.
2. Selecciónalos en la lista.
3. Pulsa DESCOMPILAR.
4. Las clases .uc se guardan en "u por partes", dentro de una carpeta con el nombre del paquete.

COMPILAR
Para compilar se necesita UCC.exe de un entorno de compilación compatible con Lineage II Interlude.
El compilador UCC y sus bibliotecas de terceros no se redistribuyen con este instalador.
Coloca el entorno de compilación dentro de:
  Documentos\Methos U Editor\herramientas\
o selecciónalo cuando el programa lo solicite.

El compilador debe ser de Interlude y debe incluir UCC.exe, los paquetes Core, Engine, Fire, Editor y NWindow, y un archivo .ini válido.
No sirve un UCC de otra crónica (C4, Gracia, High Five, etc.).
El programa trabaja sobre una copia temporal: elimina allí cualquier Interface.u antiguo para obligar a UCC a recompilar, sin tocar tu compilador ni tus archivos originales.
Antes de reemplazar el resultado, vuelve a abrir el .u generado y comprueba que contenga clases reconocibles.
El resultado se guarda en "u completo". Si ya existe un archivo con ese nombre, se crea un backup.
Si UCC falla o el archivo generado no supera la validación, el .u existente se conserva intacto.

IMPORTANTE
Prueba los paquetes compilados en una copia del cliente. No reemplaces tu Interface.u original sin guardar una copia.
