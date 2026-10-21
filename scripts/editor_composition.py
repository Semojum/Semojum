"""Desktop composition with reversible cell coordinates.

Uses the pinned Semojum/braille 3.4.31 helpers for wrapping, rules, numbers,
alignment and BRF encoding. Pagination follows assist.build_pages; this adapter
adds coordinates and independent footer/number policies. No translation rules
are reimplemented here. See tests/engine/test_editor_composition.py for upstream parity.
"""
from collections import Counter
import re
from dataclasses import dataclass, replace
from semojum_braille import assist as a
from semojum_braille.brf import serialize_brf, parse_brf


@dataclass
class Line:
    text: str
    refs: list
    end: list | None = None
    align: str = ""
    kind: str = "body"

    def substitute(self, token, value):
        start = 0
        while (i := self.text.find(token, start)) >= 0:
            self.text = self.text[:i] + value + self.text[i + len(token):]
            self.refs[i:i + len(token)] = [None] * len(value)
            start = i + len(value)
        return self

    def aligned(self, cols):
        text = a._align(self.text, cols, self.align)
        if self.align:
            # _align strips only braille/ASCII spaces, then prepends padding.
            left = len(self.text) - len(self.text.lstrip(a.SPACE + " "))
            core = self.text.strip(a.SPACE + " ")
            pad = len(text) - len(core)
            self.refs = [None] * pad + self.refs[left:left + len(core)]
        self.text = text
        return self


def fold(block, opts):
    raw = block.get("braille", "")
    ident = block.get("id")
    if raw.strip() in a.RULE_TAGS:
        text = a.rule_line(raw.strip(), opts.cols)
        return [Line(text, [None] * len(text), kind="rule")]
    if not raw:
        return []
    lines = re.findall(r"[^\r\n]*(?:\r\n|\r|\n|$)", raw)
    if lines and not lines[-1]:
        lines.pop()
    result, base = [], block.get("offset_base", 0)
    for original in lines:
        logical = original.rstrip("\r\n").replace("\t", a.SPACE).replace(" ", a.SPACE)
        breaks = [b - base for b in block.get("breaks", []) if base < b < base + len(logical)]
        center = block.get("heading_level") == 1
        # The exact upstream wrap output is authoritative. Match each fragment
        # within this ONE logical line; only whitespace removed by wrap may be
        # skipped, never characters in a different block or repeated paragraph.
        chunks = a.wrap(logical, opts.cols, opts.wrap, breaks, center)
        cursor = 0
        plain_chunks = a.wrap(logical, opts.cols, opts.wrap, breaks, False)
        for chunk, content in zip(chunks, plain_chunks):
            pos = logical.find(content, cursor) if content else cursor
            if pos < 0 or logical[cursor:pos].strip(a.SPACE + " "):
                raise ValueError("조판된 줄을 원본 점자 위치에 연결하지 못했습니다")
            pad = len(chunk) - len(content)
            refs = [None] * pad + [[ident, base + pos + i] for i in range(len(content))]
            end = [ident, base + pos + len(content)]
            result.append(Line(chunk, refs, end, block.get("align", "")))
            cursor = pos + len(content)
        base += len(original)
    return result


def compose(job):
    o = job.get("opts", {})
    opts = a.Options(cols=int(o.get("cols", 32)), rows=int(o.get("rows", 26)),
        page_row_on=o.get("page_row_pages", "odd"),
        show_orig_page=o.get("show_orig_page", True), show_braille_page=o.get("show_braille_page", True),
        cover_pages=int(o.get("cover_orig_page_count", 0)), orig_page_start=o.get("orig_page_first_no"),
        show_change_line=o.get("show_change_line", True), footer_align=o.get("footer_align", "center"),
        wrap=o.get("wrap", "syllable"), pad_to_even=o.get("pad_to_even", False))
    if not 8 <= opts.cols <= 100 or not 4 <= opts.rows <= 100:
        raise ValueError("지원하지 않는 판면 크기")
    footer_on = o.get("footer_pages", opts.page_row_on)
    footers = {int(k): v for k, v in job.get("footers", {}).items()}
    aligns = {int(k): v for k, v in job.get("footer_aligns", {}).items()}
    headers = {int(k): v for k, v in job.get("headers", {}).items()}
    flat = []
    sources = a._renumber(job.get("sources", []), opts)
    for i, source in enumerate(sources):
        op = source["orig_page"]
        cover = i < opts.cover_pages
        if i and opts.show_change_line and opts.show_orig_page:
            text = a.page_change_line(op, opts)
            flat.append((Line(text, [None] * len(text), kind="change"), op, cover))
        for block in sorted(source.get("blocks", []), key=lambda b: b.get("order", 0)):
            raw = block.get("braille", "")
            pattern = "(" + "|".join(re.escape(t) for t in [a.PAGE_BREAK_TAG, a.CHAPTER_TAG, *a.RULE_TAGS]) + ")"
            cursor = 0
            for match in re.finditer(pattern, raw):
                prefix = raw[cursor:match.start()]
                if prefix:
                    part = dict(block, braille=prefix, offset_base=cursor)
                    for line in fold(part, opts):
                        flat.append((line.substitute(a.ORIG_PAGE_TAG, a._number(op)), op, cover))
                marker = match.group()
                if marker in (a.PAGE_BREAK_TAG, a.CHAPTER_TAG):
                    flat.append((Line(a._BREAK if marker == a.PAGE_BREAK_TAG else a._CHAPTER, [],
                        end=[block.get("id"), match.start()], kind="break"), op, cover))
                else:
                    text = a.rule_line(marker, opts.cols)
                    flat.append((Line(text, [None] * len(text), kind="rule"), op, cover))
                cursor = match.end()
                if raw[cursor:cursor + 2] == "\r\n":
                    cursor += 2
                elif raw[cursor:cursor + 1] in ("\r", "\n"):
                    cursor += 1
            if cursor < len(raw):
                for line in fold(dict(block, braille=raw[cursor:], offset_base=cursor), opts):
                    flat.append((line.substitute(a.ORIG_PAGE_TAG, a._number(op)), op, cover))
    last = -1
    for i, (line, _, _) in enumerate(flat):
        if line.kind == "break" or line.text.strip():
            last = i
    pos = 0
    while pos <= last and flat[pos][0].kind != "break" and not flat[pos][0].text.strip():
        pos += 1
    pages, maps, ends, kinds, numbers = [], [], [], [], []
    first_seen = {}
    bpn = int(o.get("first_braille_page", 1))
    need_odd, prev_op = False, None

    def page_policy(bp):
        number = a._has_page_row(bp, opts.page_row_on)
        if "number_ranges" in o and o["number_ranges"] is not None:
            number = number and any(r[0] <= bp <= r[1] for r in o["number_ranges"])
        f = footers.get(bp, job.get("footer", ""))
        for rule in job.get("footer_ranges", []):
            if rule["start"] <= bp and (rule.get("end") is None or bp <= rule["end"]):
                f = rule["cells"]
                if rule.get("align"):
                    aligns[bp] = rule["align"]
                break
        return number, f if a._has_page_row(bp, footer_on) else ""

    def finish(body, bp, op, cover, head="", empty_anchor=None):
        number, footer = page_policy(bp)
        has_row = not cover and (number or bool(footer))
        cap = opts.rows - int(has_row) - int(bool(head))
        if not body and empty_anchor is not None:
            body.append(Line("", [], end=empty_anchor))
        body += [Line("", [], kind="padding") for _ in range(cap - len(body))]
        if head:
            h = a._header_line(head, op, a._number(bp), opts.cols)
            body.insert(0, Line(h, [None] * len(h), kind="header"))
        if has_row:
            row_opts = replace(opts, show_orig_page=number and opts.show_orig_page,
                show_braille_page=number and opts.show_braille_page, footer_align=aligns.get(bp, opts.footer_align))
            text = a.page_row(op, len(pages) - first_seen.get(op, len(pages)), bp, footer, row_opts)
            body.append(Line(text, [None] * len(text), kind="page"))
        pages.append([line.text for line in body])
        maps.append([line.refs for line in body])
        ends.append([line.end for line in body])
        kinds.append([line.kind for line in body])
        numbers.append(None if cover else bp)

    while pos <= last:
        while pos <= last and flat[pos][0].kind == "break":
            marker, op, cover = flat[pos]
            if marker.text == a._BREAK:
                finish([], bpn, op if prev_op is None else prev_op, cover, empty_anchor=marker.end)
                if not cover:
                    bpn += 1
            else:
                need_odd = True
            pos += 1
        if pos > last:
            break
        on_cover = flat[pos][2]
        if need_odd and not on_cover and bpn % 2 == 0:
            finish([], bpn, flat[pos][1] if prev_op is None else prev_op, False)
            bpn += 1
        need_odd = False
        head_page = flat[pos][1]
        number, footer = page_policy(bpn)
        has_row = not on_cover and (number or bool(footer))
        head = "" if on_cover else headers.get(bpn, job.get("header", ""))
        cap = opts.rows - int(has_row) - int(bool(head))
        end = pos
        while end < len(flat) and end - pos < cap and flat[end][0].kind != "break":
            end += 1
        num = "" if on_cover else a._number(bpn)
        body = [line.substitute(a.BRAILLE_PAGE_TAG, num).aligned(opts.cols) for line, _, _ in flat[pos:end]]
        prev_op = flat[end - 1][1]
        pos = end
        if pos < len(flat) and flat[pos][0].kind == "break":
            need_odd = flat[pos][0].text == a._CHAPTER
            pos += 1
        first_seen.setdefault(head_page, len(pages))
        finish(body, bpn, head_page, on_cover, head)
        if not on_cover:
            bpn += 1
    if opts.pad_to_even and len(pages) % 2:
        finish([], bpn, flat[last][1], flat[last][2])
    bad = Counter()
    brf = serialize_brf(pages, rows=opts.rows, cols=opts.cols, replaced=bad).decode("ascii")
    # Use decoded export cells (ASCII spaces become U+2800) so preview and grid
    # are byte-for-byte representations of the exported BRF, not another layout.
    return {"brf": brf, "replaced": {f"U+{ord(c):04X}": n for c, n in bad.items()},
            "pages": parse_brf(brf) if pages else [], "maps": maps, "ends": ends,
            "kinds": kinds, "numbers": numbers}
