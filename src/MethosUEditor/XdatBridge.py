from pathlib import Path
import tempfile
from xdat_core import decompile_to_folder, compile_from_folder, parse_xdat

def decompile_file(source_path, parts_path):
    source = Path(source_path)
    parts = Path(parts_path)
    parts.mkdir(parents=True, exist_ok=True)
    for name in ("windows", "shortcuts"):
        folder = parts / name
        if folder.exists():
            for item in folder.iterdir():
                if item.is_file():
                    item.unlink()
                elif item.is_dir():
                    import shutil
                    shutil.rmtree(item)
        folder.mkdir(parents=True, exist_ok=True)
    for name in ("__xdat_meta.json",):
        stale = parts / name
        if stale.exists():
            stale.unlink()
    windows, shortcuts = decompile_to_folder(source.read_bytes(), parts)
    return f"{windows}|{shortcuts}"

def compile_file(parts_path, original_path, temporary_output_path):
    parts = Path(parts_path)
    original = Path(original_path)
    temporary = Path(temporary_output_path)
    original_data = original.read_bytes()
    data, windows, shortcuts = compile_from_folder(parts, original_data)
    parsed = parse_xdat(data)
    if len(parsed.get("windows", [])) != windows or len(parsed.get("shortcuts", [])) != shortcuts:
        raise ValueError("La validación binaria no coincide con los XML compilados.")
    temporary.parent.mkdir(parents=True, exist_ok=True)
    temporary.write_bytes(data)
    if temporary.read_bytes() != data:
        temporary.unlink(missing_ok=True)
        raise ValueError("La verificación del archivo temporal falló.")
    return f"{windows}|{shortcuts}|{len(data)}"


def self_test():
    from xdat_core import Reader, put_compact
    probe = 413
    if Reader(put_compact(probe)).compact() != probe:
        raise RuntimeError('Falló la prueba interna del códec XDAT.')
    return 'OK'
