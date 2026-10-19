"""The packaged process keeps stdout exclusively for the JSONL protocol."""
from semojum_braille import sidecar
from editor_composition import compose

upstream_handle = sidecar.handle


def handle(request):
    if request.get("op") == "compose_editor":
        return compose(request.get("job") or {})
    result = upstream_handle(request)
    if request.get("op") == "ping":
        result["editor_protocol"] = 1
    return result


sidecar.handle = handle

if __name__ == "__main__":
    sidecar.main()
