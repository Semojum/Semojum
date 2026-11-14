"""Build selectable WPF legal documents from the approved Notion Markdown snapshots.
Run from any directory: python scripts/build-legal-documents.py
The text is not rewritten; pending decisions remain visible exactly as in the source.
"""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1] / 'src/Semojum.Feature.Account.Wpf/Legal'
NS = 'http://schemas.microsoft.com/winfx/2006/xaml/presentation'
ET.register_namespace('', NS)
def node(parent, name, **attrs):
    return ET.SubElement(parent, '{'+NS+'}'+name, attrs)
def text_value(s):
    return re.sub(r'\\([\[\]~*|])', r'\1', s)

def inline(parent, text):
    pattern = r'(<b>.*?</b>|<code>.*?</code>|<br\s*/?>|\[[^\]\n]+\]\([^\s)]+\))'
    for part in re.split(pattern, text, flags=re.S):
        if not part:
            continue
        if part.startswith('<b>'):
            inline(node(parent, 'Bold'), part[3:-4])
        elif part.startswith('<code>'):
            node(parent, 'Run', Text=text_value(part[6:-7]), FontFamily='Consolas')
        elif re.fullmatch(r'<br\s*/?>', part):
            node(parent, 'LineBreak')
        elif (m := re.fullmatch(r'\[([^\]\n]+)\]\(([^\s)]+)\)', part)) and m[2].startswith(('https://', 'http://', 'mailto:')):
            inline(node(parent, 'Hyperlink', NavigateUri=m[2]), m[1])
        else:
            node(parent, 'Run', Text=text_value(part))

def paragraph(parent, text, **attrs):
    p = node(parent, 'Paragraph', Margin=attrs.pop('Margin', '0,0,0,12'), **attrs)
    inline(p, text)
    return p

LIST = re.compile(r'^(\s*)(\d+\.|-)\s+(.*)$')
def add_list(lines, index, parent):
    first = LIST.match(lines[index]); indent = len(first[1]); numbered = first[2] != '-'
    attrs = {'MarkerStyle': 'Decimal' if numbered else 'Disc', 'Padding': '20,0,0,0', 'Margin':'0,0,0,12'}
    if numbered: attrs['StartIndex'] = first[2][:-1]
    group = node(parent, 'List', **attrs)
    item = None
    while index < len(lines):
        m = LIST.match(lines[index])
        if not m or len(m[1]) < indent: break
        if len(m[1]) > indent:
            if item is None: break
            index = add_list(lines, index, item)
            continue
        if (m[2] != '-') != numbered: break
        item = node(group, 'ListItem')
        paragraph(item, m[3], Margin='0,0,0,6')
        index += 1
    return index

def build(path):
    doc = ET.Element('{'+NS+'}FlowDocument', {'FontSize':'12','LineHeight':'18','TextAlignment':'Left','PagePadding':'0','ColumnWidth':'10000','Foreground':'#1A1A1A'})
    lines = path.read_text(encoding='utf-8-sig').splitlines(); i = 0
    while i < len(lines):
        line = lines[i]
        if not line.strip() or line.strip() == '---': i += 1; continue
        if line.startswith('# '):
            paragraph(doc, line[2:], FontSize='22', FontWeight='Bold', LineHeight='30'); i += 1
        elif line.startswith('## '):
            paragraph(doc, line[3:], FontSize='16', FontWeight='Bold', LineHeight='24', Margin='0,16,0,10'); i += 1
        elif LIST.match(line): i = add_list(lines, i, doc)
        elif line.startswith('|'):
            rows = []
            while i < len(lines) and lines[i].startswith('|'):
                cells = lines[i].strip().strip('|').split('|')
                if not all(re.fullmatch(r'\s*:?-+:?\s*', c) for c in cells): rows.append([c.strip() for c in cells])
                i += 1
            table = node(doc, 'Table', CellSpacing='0', Margin='0,0,0,12')
            columns = node(table, 'Table.Columns')
            for _ in rows[0]: node(columns, 'TableColumn', Width='*')
            group = node(table, 'TableRowGroup')
            for n, cells in enumerate(rows):
                row = node(group, 'TableRow', **({'Background':'#F3F3F3','FontWeight':'Bold'} if n == 0 else {}))
                for c in cells:
                    cell = node(row, 'TableCell', BorderBrush='#D1D1D1', BorderThickness='0.5', Padding='6')
                    paragraph(cell, c, Margin='0')
        else:
            parts = [line]; i += 1
            while i < len(lines) and lines[i].strip() and not lines[i].startswith(('#','|','---')) and not LIST.match(lines[i]):
                parts.append(lines[i]); i += 1
            paragraph(doc, ' '.join(parts))
    ET.indent(doc)
    ET.ElementTree(doc).write(path.with_suffix('.xaml'), encoding='utf-8', xml_declaration=True)
    print(path.stem + ': generated ' + str(sum(1 for _ in doc.iter())) + ' text/layout elements')

if __name__ == '__main__':
    for name in ('Terms', 'Privacy'): build(ROOT / (name + '.md'))