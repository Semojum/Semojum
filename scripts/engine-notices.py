"""Preserve dependency licenses beside the frozen executable."""
import importlib.metadata
from pathlib import Path
import shutil
import sys

target = Path(sys.argv[1]) / "dependency-licenses"
target.mkdir(parents=True, exist_ok=True)
for dist in importlib.metadata.distributions():
    for item in dist.files or []:
        if not any(s in Path(str(item)).name.lower() for s in ("license", "notice", "copying")):
            continue
        source = Path(dist.locate_file(item))
        if source.is_file():
            folder = target / dist.metadata["Name"]
            folder.mkdir(exist_ok=True)
            shutil.copyfile(source, folder / str(item).replace("/", "_").replace("\\", "_"))
shutil.copyfile(Path(sys.base_prefix) / "LICENSE.txt", target / "Python-LICENSE.txt")
