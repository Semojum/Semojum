"""Bounded, process-local memoization for the desktop's pinned engine adapter.

The upstream page operation still owns all contextual flattening and spacing.
Only preparation of unchanged elements is reused. The JSONL process dispatches
one request at a time; the temporary translate hook never spans an await.
"""
from collections import OrderedDict
from copy import deepcopy
import inspect
import json
import uuid


class PageTranslationCache:
    def __init__(self, engine, max_entries=2048, max_bytes=16 * 1024 * 1024):
        self.engine = engine
        self.entries = OrderedDict()
        self.max_entries = max_entries
        self.max_bytes = max_bytes
        self.size = 0

    def handle(self, request, upstream):
        original = self.engine.translate
        signature = inspect.signature(original)
        position = 0

        def translate(*args, **kwargs):
            nonlocal position
            bound = signature.bind(*args, **kwargs)
            bound.apply_defaults()
            page = bound.arguments.pop('_page')
            if page is None:
                return original(*args, **kwargs)
            slot = position
            position += 1
            # Slot keeps identical elements distinct even with duplicate orders.
            key = json.dumps([slot, bound.arguments], sort_keys=True, ensure_ascii=False)
            cached = self.entries.pop(key, None)
            if cached is None:
                pending = []
                result = original(**bound.arguments, _page=pending)
                weight = len(key.encode('utf-8')) + len(json.dumps(result, ensure_ascii=False).encode('utf-8'))
                weight += sum(len(bo.model_dump_json().encode('utf-8')) for bo, _, _ in pending)
                cached = (result, pending, weight)
                blocked = bool(result and result.get('blocked'))
                blocked |= any(self.engine.is_blocked_braille(bo.braille_lines) for bo, _, _ in pending)
                if not blocked and self.max_entries > 0 and weight <= self.max_bytes:
                    while self.entries and (len(self.entries) >= self.max_entries or self.size + weight > self.max_bytes):
                        _, (_, _, removed) = self.entries.popitem(last=False)
                        self.size -= removed
                    self.entries[key] = cached
                    self.size += weight
            else:
                self.entries[key] = cached
            result, pending, _ = cached
            for bo, element, finish in pending:
                copied_bo, copied_element = deepcopy(bo), deepcopy(element)
                ident = uuid.uuid4()
                copied_bo.element_id = ident
                copied_element.element_id = ident
                # The engine's finish closure reads its prepared immutable data;
                # copying its response keeps caller mutation out of the cache.
                page.append((copied_bo, copied_element, lambda flat, finish=finish: deepcopy(finish(flat))))
            return deepcopy(result)

        self.engine.translate = translate
        try:
            return upstream(request)
        finally:
            self.engine.translate = original